﻿using System;
using System.Threading;
using System.Threading.Tasks;
using Meadow.CLI.Core.Internals.MeadowCommunication;

namespace Meadow.CLI.Core.DeviceManagement
{
    public class MeadowDataProcessor
    {
        public EventHandler<MeadowMessageEventArgs>? OnReceiveData;
        public Func<byte[]?, CancellationToken, Task>? ForwardDebuggingData;
    }

    public class MeadowMessageEventArgs : EventArgs
    {
        public string Message { get; private set; }
        public MeadowMessageType MessageType { get; private set; }

        public MeadowMessageEventArgs(MeadowMessageType messageType, string message = "")
        {
            Message = message;
            MessageType = messageType;
        }
    }
}
