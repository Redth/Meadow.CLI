﻿using System;

namespace Meadow.CLI.Core.Internals.MeadowComms.RecvClasses
{
    // Factory class
    public class RecvSimpleMsgFactory : RecvMessageFactory
    {
        public override IReceivedMessage Create(byte[] recvdMsg, int recvdMsgLength) => new RecvSimpleMsg(recvdMsg, recvdMsgLength);
    }

    // Concrete class
    internal class RecvSimpleMsg : RecvHeader
    {
        public RecvSimpleMsg(byte[] recvdMsg, int recvdMsgLength) : base(recvdMsg, recvdMsgLength)
        {
        }

        public override bool Execute(byte[] recvdMsg, int recvdMsgLen)
        {
            try
            {
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception:{ex.Message}");
                return false;
            }
        }
    }
}
