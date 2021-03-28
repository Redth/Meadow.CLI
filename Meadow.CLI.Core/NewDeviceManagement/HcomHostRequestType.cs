﻿using System;
using System.Collections.Generic;
using System.Text;

namespace Meadow.CLI.Core.NewDeviceManagement
{
    /// <summary>
    /// Messages sent from meadow to host
    /// </summary>
    public enum HcomHostRequestType : UInt16
    {
        HCOM_HOST_REQUEST_UNDEFINED_REQUEST = 0x00 | HcomProtocolHeaderTypes.HCOM_PROTOCOL_HEADER_TYPE_UNDEFINED,

        // Simple types
        HCOM_HOST_REQUEST_SIMPLE_MESSAGE = 0x01 | HcomProtocolHeaderTypes.HCOM_PROTOCOL_HEADER_TYPE_SIMPLE,    // Just the header
                                                                                                               // Simple with some text message

        HCOM_HOST_REQUEST_TEXT_REJECTED = 0x01 | HcomProtocolHeaderTypes.HCOM_PROTOCOL_HEADER_TYPE_SIMPLE_TEXT,
        HCOM_HOST_REQUEST_TEXT_ACCEPTED = 0x02 | HcomProtocolHeaderTypes.HCOM_PROTOCOL_HEADER_TYPE_SIMPLE_TEXT,
        HCOM_HOST_REQUEST_TEXT_CONCLUDED = 0x03 | HcomProtocolHeaderTypes.HCOM_PROTOCOL_HEADER_TYPE_SIMPLE_TEXT,
        HCOM_HOST_REQUEST_TEXT_ERROR = 0x04 | HcomProtocolHeaderTypes.HCOM_PROTOCOL_HEADER_TYPE_SIMPLE_TEXT,
        HCOM_HOST_REQUEST_TEXT_INFORMATION = 0x05 | HcomProtocolHeaderTypes.HCOM_PROTOCOL_HEADER_TYPE_SIMPLE_TEXT,
        HCOM_HOST_REQUEST_TEXT_LIST_HEADER = 0x06 | HcomProtocolHeaderTypes.HCOM_PROTOCOL_HEADER_TYPE_SIMPLE_TEXT,
        HCOM_HOST_REQUEST_TEXT_LIST_MEMBER = 0x07 | HcomProtocolHeaderTypes.HCOM_PROTOCOL_HEADER_TYPE_SIMPLE_TEXT,
        HCOM_HOST_REQUEST_TEXT_CRC_MEMBER = 0x08 | HcomProtocolHeaderTypes.HCOM_PROTOCOL_HEADER_TYPE_SIMPLE_TEXT,
        HCOM_HOST_REQUEST_TEXT_MONO_STDOUT = 0x09 | HcomProtocolHeaderTypes.HCOM_PROTOCOL_HEADER_TYPE_SIMPLE_TEXT,
        HCOM_HOST_REQUEST_TEXT_DEVICE_INFO = 0x0A | HcomProtocolHeaderTypes.HCOM_PROTOCOL_HEADER_TYPE_SIMPLE_TEXT,
        HCOM_HOST_REQUEST_TEXT_TRACE_MSG = 0x0B | HcomProtocolHeaderTypes.HCOM_PROTOCOL_HEADER_TYPE_SIMPLE_TEXT,
        HCOM_HOST_REQUEST_TEXT_RECONNECT = 0x0C | HcomProtocolHeaderTypes.HCOM_PROTOCOL_HEADER_TYPE_SIMPLE_TEXT,
        HCOM_HOST_REQUEST_TEXT_MONO_STDERR = 0x0d | HcomProtocolHeaderTypes.HCOM_PROTOCOL_HEADER_TYPE_SIMPLE_TEXT,
        // Simple with debugger message from Meadow
        HCOM_HOST_REQUEST_DEBUGGING_MONO_DATA = 0x01 | HcomProtocolHeaderTypes.HCOM_PROTOCOL_HEADER_TYPE_SIMPLE_BINARY,
    }
}
