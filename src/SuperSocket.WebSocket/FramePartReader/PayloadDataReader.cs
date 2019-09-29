﻿using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SuperSocket.WebSocket.FramePartReader
{
    class PayloadDataReader : DataFramePartReader
    {
        public override bool Process(WebSocketPackage package, ref SequenceReader<byte> reader, out IDataFramePartReader nextPartReader)
        {
            long required = package.PayloadLength;

            if (reader.Length < required)
            {
                nextPartReader = this;
                return false;
            }

            package.Data = reader.Sequence;
            nextPartReader = null;
            return true;
        }
    }
}
