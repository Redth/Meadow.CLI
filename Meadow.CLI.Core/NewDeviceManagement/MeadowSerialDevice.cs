﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Meadow.CLI.Core.NewDeviceManagement.MeadowComms;
using Meadow.CLI.Core.NewDeviceManagement.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meadow.CLI.Core.NewDeviceManagement
{
    public partial class MeadowSerialDevice : MeadowLocalDevice
    {
        private readonly string _serialPortName;
        public SerialPort SerialPort { get; private set; }

        public MeadowSerialDevice(string serialPortName, ILogger<MeadowSerialDevice>? logger = null)
            : this(serialPortName, OpenSerialPort(serialPortName), logger)
        {
        }

        private MeadowSerialDevice(string serialPortName,
                                   SerialPort serialPort,
                                   ILogger<MeadowSerialDevice>? logger = null)
            : base(new MeadowSerialDataProcessor(serialPort), logger)
        {
            SerialPort = serialPort;
            _serialPortName = serialPortName;
        }

        public sealed override bool IsDeviceInitialized()
        {
            return SerialPort != null;
        }

        public override void Dispose()
        {
            SerialPort.Dispose();
        }

        public override async Task Write(byte[] encodedBytes, int encodedToSend)
        {
            if (SerialPort == null)
                throw new NotConnectedException();

            if (SerialPort.IsOpen == false)
            {
                await AttemptToReconnectToMeadow();
            }

            SerialPort.Write(encodedBytes, 0, encodedToSend);
        }

        public override async Task<bool> Initialize(CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            if (!SerialPort.IsOpen)
            {
                SerialPort.Open();
            }

            return SerialPort.IsOpen;
        }

        private static SerialPort OpenSerialPort(string portName)
        {
            // Create a new SerialPort object with default settings
            var port = new SerialPort
                       {
                           PortName = portName,
                           BaudRate = 115200, // This value is ignored when using ACM
                           Parity = Parity.None,
                           DataBits = 8,
                           StopBits = StopBits.One,
                           Handshake = Handshake.None,

                           // Set the read/write timeouts
                           ReadTimeout = 5000,
                           WriteTimeout = 5000
                       };

            port.Open();

            //improves perf on Windows?
            port.BaseStream.ReadTimeout = 0;
            return port;
        }

        internal async Task<bool> AttemptToReconnectToMeadow(
            CancellationToken cancellationToken = default)
        {
            int delayCount = 20; // 10 seconds
            while (true)
            {
                await Task.Delay(500, cancellationToken)
                          .ConfigureAwait(false);

                bool portOpened = await Initialize(cancellationToken)
                                      .ConfigureAwait(false);

                if (portOpened)
                {
                    Logger.LogDebug("Device successfully reconnected");
                    await Task.Delay(2000, cancellationToken)
                              .ConfigureAwait(false);

                    return true;
                }

                if (delayCount-- == 0)
                    throw new NotConnectedException();
            }
        }
    }
}