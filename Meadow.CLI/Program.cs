﻿using CommandLine;
using System;
using System.Diagnostics;
using MeadowCLI.DeviceManagement;
using System.IO;
using System.IO.Ports;
using System.Threading.Tasks;
using Meadow.CLI;
using System.Linq;
using System.Threading;
using LibUsbDotNet;
using Meadow.CLI.Core.Auth;
using Meadow.CLI.Core.CloudServices;

namespace MeadowCLI
{
    class Program
    {

        [Flags]
        enum CompletionBehavior
        {
            Success = 0x00,
            RequestFailed = 1 << 0,
            ExitConsole = 1 << 2,
            KeepConsoleOpen = 1 << 3
        }

        static void Main(string[] args)
        {
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
            };

            CompletionBehavior behavior = CompletionBehavior.Success;

            DownloadManager downloadManager = new DownloadManager();
            var check = downloadManager.CheckForUpdates().Result;
            if (check.updateExists)
            {
                Console.WriteLine($"CLI version {check.latestVersion} is available. To update, run: {DownloadManager.UpdateCommand}");
            }

            if (args.Length == 0)
            {
                args = new string[] { "--help" };
            }

            var parser = new Parser(settings => { 
                settings.CaseSensitive = false;
                settings.AutoVersion = false; // needed to supercede the built in --version command
                settings.HelpWriter = Console.Out;
                settings.IgnoreUnknownArguments = true;
            });

            parser.ParseArguments<Options>(args)
            .WithParsed<Options>(options =>
            {
                if (options.ListPorts)
                {
                    Console.WriteLine("Available serial ports\n----------------------");

                    var ports = MeadowSerialDevice.GetAvailableSerialPorts();
                    if (ports == null || ports.Length == 0)
                    {
                        Console.WriteLine("\t <no ports found>");
                    }
                    else
                    {
                        foreach (var p in ports)
                        {
                            Console.WriteLine($"\t{p}");
                        }
                    }
                    Console.WriteLine($"\n");
                }
                else
                {
                    if (options.DownloadLatest)
                    {
                        downloadManager.DownloadLatest().Wait();
                    }
                    else if (options.FlashOS)
                    {
                        DfuUpload.FlashOS(options.FileName);
                    }
                    else if (options.Login)
                    {
                        IdentityManager identityManager = new IdentityManager();
                        var result = identityManager.LoginAsync().Result;
                        if (result)
                        {
                            var cred = identityManager.GetCredentials(identityManager.WLRefreshCredentialName);
                            Console.WriteLine($"Signed in as {cred.username}");
                        }
                    }
                    else if (options.Logout)
                    {
                        IdentityManager identityManager = new IdentityManager();
                        identityManager.Logout();
                    }
                    else if (options.InstallDfuUtil)
                    {
                        if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                        {
                            Console.WriteLine("To install on macOS, run: brew install dfu-util");
                        }
                        else
                        {
                            downloadManager.InstallDfuUtil(Environment.Is64BitOperatingSystem);
                        }
                    }
                    else if (args[0].Equals("--version", StringComparison.InvariantCultureIgnoreCase))
                    {
                        Console.WriteLine($"Current version: {check.currentVersion}");
                    }
                    else
                    {
                        SyncArgsCache(options);
                        try
                        {
                            ProcessHcom(options).Wait();
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"An unexpected error occurred: {ex?.InnerException?.Message}");
                        }
                    }

                }
            });

            //if (System.Diagnostics.Debugger.IsAttached)
            //{
            //    behavior = CompletionBehavior.KeepConsoleOpen;
            //}

            Environment.Exit(0);
        }

        static void SyncArgsCache(Options options)
        {
            var port = SettingsManager.GetSetting(Setting.PORT);
            if (string.IsNullOrEmpty(options.SerialPort) && !string.IsNullOrEmpty(port))
            {
                options.SerialPort = port;
            }
            else if (!string.IsNullOrEmpty(options.SerialPort))
            {
                SettingsManager.SaveSetting(Setting.PORT, options.SerialPort);
            }
        }

        private static async Task<bool> DeviceInDfuMode(int timeout = 5000)
        {
            var endTime = DateTime.UtcNow.AddMilliseconds(timeout);
            bool deviceFound;
            while ((deviceFound = DfuUpload.CheckForValidDevice()) == false && endTime < DateTime.UtcNow)
            {
                await Task.Delay(1_000).ConfigureAwait(false);
            }

            return deviceFound;
        }

        private static async Task<string> GetMeadowSerialNumber(MeadowSerialDevice device)
        {
            var (success, deviceInfo) = await MeadowDeviceManager.GetDeviceInfo(device);
            if (success)
            {
                const string key = "Serial Number: ";
                var startIndex = deviceInfo.IndexOf(key, StringComparison.Ordinal) + key.Length;
                var endIndex = deviceInfo.IndexOf(',', startIndex);
                return deviceInfo.Substring(startIndex, endIndex - startIndex);
            }

            throw new Exception("Unable to determine Meadow Serial Number");
        }

        private static async Task<string> FindMeadow(string serialNumber)
        {
            var ports = SerialPort.GetPortNames();
            foreach (var port in ports)
            {
                using var device = await MeadowDeviceManager.GetMeadowForSerialPort(port);
                var (success, deviceInfo) = await MeadowDeviceManager.GetDeviceInfo(device);
                if (success && deviceInfo.Contains(serialNumber))
                    return port;
            }

            throw new Exception("Meadow not found after DFU flash.");
        }

        private static async Task FlashEverything(Options options)
        {
            // TODO: Add ability to specify serial on command line. This is important for devices that are too old to talk nicely to the CLI and have to be put in DFU mode to start.
            var serialNumber = string.Empty;
            var dfuAttempts = 0;
            do
            {
                try
                {
                    using var device =
                        await MeadowDeviceManager.GetMeadowForSerialPort(options.SerialPort);
                    serialNumber = await GetMeadowSerialNumber(device);

                    Console.WriteLine("Entering DFU Mode");
                    await MeadowDeviceManager.ProcessCommand(
                        device,
                        MeadowFileManager.HcomMeadowRequestType.HCOM_MDOW_REQUEST_ENTER_DFU_MODE,
                        null);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                    Environment.Exit(-1);
                }

                if (dfuAttempts > 5)
                {
                    Console.WriteLine(
                        "Unable to place device in DFU mode, please disconnect the Meadow, hold the BOOT button, reconnect the Meadow, release the BOOT button and try again.");

                    Environment.Exit(-1);
                }

                dfuAttempts++;

            } while (await DeviceInDfuMode(100)
                         .ConfigureAwait(false)
                  == false);

            Console.WriteLine("Device in DFU Mode, flashing OS");

            DfuUpload.FlashOS();
            Console.WriteLine("Device Flashed.");

            
            try
            {
                var serialPort = await FindMeadow(serialNumber);
                using var device =
                    await MeadowDeviceManager.GetMeadowForSerialPort(serialPort);
                
                Console.WriteLine("Waiting for Meadow to be ready.");
                await WaitForReady(device).ConfigureAwait(false);

                Console.WriteLine("Disabling Mono");
                do
                {
                    await SendCommandAndWaitForReady(device, () => MeadowDeviceManager.MonoDisable(device)).ConfigureAwait(false);
                    await SendCommandAndWaitForReady(device, () => MeadowDeviceManager.ResetMeadow(device)).ConfigureAwait(false);
                } while (await MeadowDeviceManager.MonoRunState(device).ConfigureAwait(false));

                Console.WriteLine("Updating Mono Runtime");
                await UpdateMonoRt(options, device).ConfigureAwait(false);
                await SendCommandAndWaitForReady(device, () => MeadowDeviceManager.ResetMeadow(device)).ConfigureAwait(false);

                Console.WriteLine("Flashing ESP");
                await FlashEsp(device).ConfigureAwait(false);
                await SendCommandAndWaitForReady(device, () => MeadowDeviceManager.ResetMeadow(device)).ConfigureAwait(false);

                Console.WriteLine("Enabling Mono and Resetting.");
                do
                {
                    await SendCommandAndWaitForReady(device, () => MeadowDeviceManager.MonoEnable(device)).ConfigureAwait(false);
                    await SendCommandAndWaitForReady(device, () => MeadowDeviceManager.ResetMeadow(device)).ConfigureAwait(false);
                } while (await MeadowDeviceManager.MonoRunState(device).ConfigureAwait(false) == false);
            

                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                Environment.Exit(-1);
            }
        }

        //Probably rename

        static async Task ProcessHcom(Options options)
        {
            if (string.IsNullOrEmpty(options.SerialPort))
            {
                Console.WriteLine("Please specify a --SerialPort");
                return;
            }

            if (options.FlashEverything)
            {
                await FlashEverything(options).ConfigureAwait(false);
            }

            MeadowSerialDevice device = null;
            try
            {
                Console.WriteLine($"Opening port '{options.SerialPort}'");
                device = await MeadowDeviceManager.GetMeadowForSerialPort(options.SerialPort);
            }
            catch(Exception ex)
            {
                Console.WriteLine($"Error connecting to device: {ex.Message}");
                return;
            }

            using (device)
            {
                // verify that the port was actually connected
                if (device.Socket == null && device.SerialPort == null)
                {
                    Console.WriteLine($"Port is unavailable.");
                    return;
                }

                try
                {
                    if (options.WriteFile.Any())
                    {
                        string[] parameters = options.WriteFile.ToArray();

                        string[] files = parameters[0].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        string[] targetFileNames;

                        if (parameters.Length == 1)
                        {
                            targetFileNames = new string[files.Length];
                        }
                        else
                        {
                            targetFileNames = parameters[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        }

                        if (files.Length != targetFileNames.Length)
                        {
                            Console.WriteLine($"Number of files to write ({files.Length}) does not match the number of target file names ({targetFileNames.Length}).");
                        }
                        else
                        {
                            for (int i = 0; i < files.Length; i++)
                            {
                                string targetFileName = targetFileNames[i];

                                if (String.IsNullOrEmpty(targetFileName))
                                {
                                    targetFileName = null;
                                }

                                if (!File.Exists(files[i]))
                                {
                                    Console.WriteLine($"Cannot find {files[i]}");
                                }
                                else
                                {
                                    if (string.IsNullOrEmpty(targetFileName))
                                    {
#if USE_PARTITIONS
                                        Console.WriteLine($"Writing {files[i]} to partition {options.Partition}");
#else
                                        Console.WriteLine($"Writing {files[i]}");
#endif
                                    }
                                    else
                                    {
#if USE_PARTITIONS
                                        Console.WriteLine($"Writing {files[i]} as {targetFileName} to partition {options.Partition}");
#else
                                        Console.WriteLine($"Writing {files[i]} as {targetFileName}");
#endif
                                    }

                                    await MeadowFileManager.WriteFileToFlash(device, files[i], targetFileName, options.Partition).ConfigureAwait(false);
                                }
                            }
                        }
                    }
                    else if (options.DeleteFile.Any())
                    {
                        string[] parameters = options.DeleteFile.ToArray();
                        string[] files = parameters[0].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                        foreach (string file in files)
                        {
                            if (!String.IsNullOrEmpty(file))
                            {
#if USE_PARTITIONS
                                Console.WriteLine($"Deleting {file} from partion {options.Partition}");
#else
                                Console.WriteLine($"Deleting {file}");
#endif
                                await MeadowFileManager.DeleteFile(device, file, options.Partition);
                            }
                        }
                    }
                    else if (options.EraseFlash)
                    {
                        Console.WriteLine("Erasing flash");
                        await MeadowFileManager.EraseFlash(device);
                    }
                    else if (options.VerifyErasedFlash)
                    {
                        Console.WriteLine("Verifying flash is erased");
                        await MeadowFileManager.VerifyErasedFlash(device);
                    }
                    else if (options.PartitionFileSystem)
                    {
                        Console.WriteLine($"Partioning file system into {options.NumberOfPartitions} partition(s)");
                        await MeadowFileManager.PartitionFileSystem(device, options.NumberOfPartitions);
                    }
                    else if (options.MountFileSystem)
                    {
#if USE_PARTITIONS
                        Console.WriteLine($"Mounting partition {options.Partition}");
#else
                        Console.WriteLine("Mounting file system");
#endif
                        await MeadowFileManager.MountFileSystem(device, options.Partition);
                    }
                    else if (options.InitFileSystem)
                    {
#if USE_PARTITIONS
                        Console.WriteLine($"Intializing filesystem in partition {options.Partition}");
#else
                        Console.WriteLine("Intializing filesystem");
#endif
                        await MeadowFileManager.InitializeFileSystem(device, options.Partition);
                    }
                    else if (options.CreateFileSystem) //should this have a partition???
                    {
                        Console.WriteLine($"Creating file system");
                        await MeadowFileManager.CreateFileSystem(device);
                    }
                    else if (options.FormatFileSystem)
                    {
#if USE_PARTITIONS
                        Console.WriteLine($"Format file system on partition {options.Partition}");
#else
                        Console.WriteLine("Format file system");
#endif
                        await MeadowFileManager.FormatFileSystem(device, options.Partition);
                    }
                    else if (options.ListFiles)
                    {
#if USE_PARTITIONS
                        Console.WriteLine($"Getting list of files on partition {options.Partition}");
#else
                        Console.WriteLine($"Getting list of files");
#endif
                        await MeadowFileManager.ListFiles(device, options.Partition);
                    }
                    else if (options.ListFilesAndCrcs)
                    {
#if USE_PARTITIONS
                        Console.WriteLine($"Getting list of files and CRCs on partition {options.Partition}");
#else
                        Console.WriteLine("Getting list of files and CRCs");
#endif
                        await MeadowFileManager.ListFilesAndCrcs(device, options.Partition);
                    }
                    else if (options.SetTraceLevel)
                    {
                        Console.WriteLine($"Setting trace level to {options.TraceLevel}");
                        await MeadowDeviceManager.SetTraceLevel(device, options.TraceLevel);
                    }
                    else if (options.SetDeveloper1)
                    {
                        Console.WriteLine($"Setting developer level to {options.DeveloperValue}");
                        await MeadowDeviceManager.SetDeveloper1(device, options.DeveloperValue);
                    }
                    else if (options.SetDeveloper2)
                    {
                        Console.WriteLine($"Setting developer level to {options.DeveloperValue}");
                        await MeadowDeviceManager.SetDeveloper2(device, options.DeveloperValue);
                    }
                    else if (options.SetDeveloper3)
                    {
                        Console.WriteLine($"Setting developer level to {options.DeveloperValue}");
                        await MeadowDeviceManager.SetDeveloper3(device, options.DeveloperValue);
                    }
                    else if (options.SetDeveloper4)
                    {
                        Console.WriteLine($"Setting developer level to {options.DeveloperValue}");
                        await MeadowDeviceManager.SetDeveloper4(device, options.DeveloperValue);
                    }
                    else if (options.NshEnable)
                    {
                        Console.WriteLine($"Enable Nsh");
                        await MeadowDeviceManager.NshEnable(device);
                    }
                    else if (options.MonoDisable)
                    {
                        await MeadowDeviceManager.MonoDisable(device);
                    }
                    else if (options.MonoEnable)
                    {
                        await MeadowDeviceManager.MonoEnable(device);
                    }
                    else if (options.MonoRunState)
                    {
                        await MeadowDeviceManager.MonoRunState(device);
                    }
                    else if (options.MonoFlash)
                    {
                        await MeadowDeviceManager.MonoFlash(device);
                    }
                    else if (options.MonoUpdateRt)
                    {
                        await UpdateMonoRt(options, device).ConfigureAwait(false);
                    }
                    else if (options.GetDeviceInfo)
                    {
                        await MeadowDeviceManager.GetDeviceInfo(device);
                    }
                    else if (options.GetDeviceName)
                    {
                        await MeadowDeviceManager.GetDeviceName(device);
                    }
                    else if (options.ResetMeadow)
                    {
                        Console.WriteLine("Resetting Meadow");
                        await MeadowDeviceManager.ResetMeadow(device);
                    }
                    else if (options.EnterDfuMode)
                    {
                        Console.WriteLine("Entering Dfu mode");
                        await MeadowDeviceManager.EnterDfuMode(device);
                    }
                    else if (options.TraceDisable)
                    {
                        Console.WriteLine("Disabling Meadow trace messages");
                        await MeadowDeviceManager.TraceDisable(device);
                    }
                    else if (options.TraceEnable)
                    {
                        Console.WriteLine("Enabling Meadow trace messages");
                        await MeadowDeviceManager.TraceEnable(device);
                    }
                    else if (options.Uart1Apps)
                    {
                        Console.WriteLine("Use Uart1 for .NET Apps");
                        await MeadowDeviceManager.Uart1Apps(device);
                    }
                    else if (options.Uart1Trace)
                    {
                        Console.WriteLine("Use Uart1 for outputting Meadow trace messages");
                        await MeadowDeviceManager.Uart1Trace(device);
                    }
                    else if (options.RenewFileSys)
                    {
                        Console.WriteLine("Recreate a new file system on Meadow");
                        await MeadowDeviceManager.RenewFileSys(device);
                    }
                    else if (options.QspiWrite)
                    {
                        Console.WriteLine($"Executing QSPI Flash Write using {options.DeveloperValue}");
                        await MeadowDeviceManager.QspiWrite(device, options.DeveloperValue);
                    }
                    else if (options.QspiRead)
                    {
                        Console.WriteLine($"Executing QSPI Flash Read using {options.DeveloperValue}");
                        await MeadowDeviceManager.QspiRead(device, options.DeveloperValue);
                    }
                    else if (options.QspiInit)
                    {
                        Console.WriteLine($"Executing QSPI Flash Initialization using {options.DeveloperValue}");
                        await MeadowDeviceManager.QspiInit(device, options.DeveloperValue);
                    }
                    else if (options.StartDebugging)
                    {
                        MeadowDeviceManager.StartDebugging(device, options.VSDebugPort);
                        Console.WriteLine($"Ready for Visual Studio debugging");
                        options.KeepAlive = true;
                    }
                    else if (options.Esp32WriteFile)
                    {
                        if (string.IsNullOrEmpty(options.FileName))
                        {
                            Console.WriteLine($"option --Esp32WriteFile requires option --File (the local file you wish to write)");
                        }
                        else
                        {
                            if (string.IsNullOrEmpty(options.TargetFileName))
                            {
                                Console.WriteLine($"Writing {options.FileName} to ESP32");
                            }
                            else
                            {
                                Console.WriteLine($"Writing {options.FileName} as {options.TargetFileName}");
                            }
                            await MeadowFileManager.WriteFileToEspFlash(device,
                                options.FileName, options.TargetFileName, options.Partition, options.McuDestAddr);
                        }
                    }
                    else if (options.FlashEsp)
                    {
                        await FlashEsp(device).ConfigureAwait(false);
                    }
                    else if (options.Esp32ReadMac)
                    {
                        await MeadowDeviceManager.Esp32ReadMac(device);
                    }
                    else if (options.Esp32Restart)
                    {
                        await MeadowDeviceManager.Esp32Restart(device);
                    }
                    else if (options.DeployApp && !string.IsNullOrEmpty(options.FileName))
                    {
                        await MeadowDeviceManager.DeployApp(device, options.FileName);
                    }
                    else if (options.RegisterDevice)
                    {
                        var sn = await MeadowDeviceManager.GetDeviceSerialNumber(device);

                        if (string.IsNullOrEmpty(sn)) 
                        {
                            Console.WriteLine("Could not get device serial number. Reconnect device and try again.");
                            return;
                        }

                        Console.WriteLine($"Registering device {sn}");

                        DeviceRepository repository = new DeviceRepository();
                        var result = await repository.AddDevice(sn);
                        if (result.isSuccess)
                        {
                            Console.WriteLine("Device registration complete");
                        }
                        else
                        {
                            Console.WriteLine(result.message);
                        }
                    }

                    if (options.KeepAlive)
                    {
                        Console.Read();
                    }
                }
                catch (IOException ex)
                {
                    if (ex.Message.Contains("semaphore"))
                    {
                        if (ex.Message.Contains("semaphore"))
                        {
                            Console.WriteLine("Timeout communicating with Meadow");
                        }
                        else
                        {
                            Console.WriteLine($"Unexpected error occurred: {ex.Message}");
                        }
                        return; // KeepConsoleOpen?
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Unexpected error occurred: {ex.Message}");
                    return; // KeepConsoleOpen?
                }
            }
        }

        private static async Task UpdateMonoRt(Options options, MeadowSerialDevice device)
        {
            string sourcefilename = options.FileName;
            if (string.IsNullOrWhiteSpace(sourcefilename))
            {
                // check local override
                sourcefilename = Path.Combine(Directory.GetCurrentDirectory(), DownloadManager.RuntimeFilename);
                if (File.Exists(sourcefilename))
                {
                    Console.WriteLine($"Using current directory '{DownloadManager.RuntimeFilename}'");

                }
                else
                {
                    sourcefilename = Path.Combine(DownloadManager.FirmwareDownloadsFilePath, DownloadManager.RuntimeFilename);
                    if (File.Exists(sourcefilename))
                    {
                        Console.WriteLine("FileName not specified, using latest download.");
                    }
                    else
                    {
                        Console.WriteLine("Unable to locate a runtime file. Either provide a path or download one.");
                        return; // KeepConsoleOpen?
                    }
                }
            }

            if (!File.Exists(sourcefilename))
            {
                Console.WriteLine($"File '{sourcefilename}' not found");
                return; // KeepConsoleOpen?
            }

            await MeadowFileManager.MonoUpdateRt(device, sourcefilename, options.TargetFileName, options.Partition);
        }

        private static async Task FlashEsp(MeadowSerialDevice device)
        {
            Console.WriteLine($"Transferring {DownloadManager.NetworkMeadowCommsFilename}");
            await MeadowFileManager.WriteFileToEspFlash(device,
                                                        Path.Combine(DownloadManager.FirmwareDownloadsFilePath, DownloadManager.NetworkMeadowCommsFilename), mcuDestAddr: "0x10000")
                                   .ConfigureAwait(false);
            await Task.Delay(1000).ConfigureAwait(false);

            Console.WriteLine($"Transferring {DownloadManager.NetworkBootloaderFilename}");
            await MeadowFileManager.WriteFileToEspFlash(device,
                                                        Path.Combine(DownloadManager.FirmwareDownloadsFilePath, DownloadManager.NetworkBootloaderFilename), mcuDestAddr: "0x1000")
                                   .ConfigureAwait(false);
            await Task.Delay(1000).ConfigureAwait(false);

            Console.WriteLine($"Transferring {DownloadManager.NetworkPartitionTableFilename}");
            await MeadowFileManager.WriteFileToEspFlash(device,
                                                        Path.Combine(DownloadManager.FirmwareDownloadsFilePath, DownloadManager.NetworkPartitionTableFilename), mcuDestAddr: "0x8000")
                                   .ConfigureAwait(false);
            await Task.Delay(1000).ConfigureAwait(false);
        }

        private static async Task<bool> SendCommandAndWaitForReady(MeadowSerialDevice device, Func<Task> command, int timeout = 60_000)
        {
            Debug.WriteLine("Invoking command.");
            await command().ConfigureAwait(false);
            Debug.WriteLine("Command invoked, waiting for Meadow to be ready.");

            return await WaitForReady(device, timeout).ConfigureAwait(false);
        }

        private static async Task<bool> WaitForReady(MeadowSerialDevice device, int timeout = 60_000)
        {
            var now = DateTime.UtcNow;
            var then = now.AddMilliseconds(timeout);
            while (DateTime.UtcNow < then)
            {
                try
                {
                    var (isSuccessful, _) = await MeadowDeviceManager.GetDeviceInfo(device);
                    if (isSuccessful)
                        return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"An exception occurred. Retrying. Exception: {ex}");
                }
            }

            throw new Exception($"Device not ready after {timeout}ms");
        }
    }
}
