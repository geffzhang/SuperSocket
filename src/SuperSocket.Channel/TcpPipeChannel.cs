﻿using System;
using System.Threading.Tasks;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Threading;
using System.Buffers;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SuperSocket.ProtoBase;

namespace SuperSocket.Channel
{
    public class TcpPipeChannel<TPackageInfo> : PipeChannel<TPackageInfo>
        where TPackageInfo : class
    {

        private Socket _socket;

        private List<ArraySegment<byte>> _segmentsForSend;
        
        public TcpPipeChannel(Socket socket, IPipelineFilter<TPackageInfo> pipelineFilter, ChannelOptions options, ILogger logger)
            : base(pipelineFilter, options, logger)
        {
            _socket = socket;
        }

        protected override void OnClosed()
        {
            _socket = null;
            base.OnClosed();
        }

        private async Task FillPipeAsync(Socket socket, PipeWriter writer)
        {
            var options = Options;

            while (true)
            {
                try
                {
                    var bufferSize = options.ReceiveBufferSize;
                    var maxPackageLength = options.MaxPackageLength;

                    if (maxPackageLength > 0)
                        bufferSize = Math.Min(bufferSize, maxPackageLength);

                    var memory = writer.GetMemory(bufferSize);

                    var bytesRead = await ReceiveAsync(socket, memory, SocketFlags.None);         

                    if (bytesRead == 0)
                    {
                        break;
                    }

                    // Tell the PipeWriter how much was read
                    writer.Advance(bytesRead);
                }
                catch (Exception e)
                {
                    Logger.LogError(e, "Exception happened in ReceiveAsync");
                    break;
                }

                // Make the data available to the PipeReader
                var result = await writer.FlushAsync();

                if (result.IsCompleted)
                {
                    break;
                }
            }

            // Signal to the reader that we're done writing
            writer.Complete();
            Output.Writer.Complete();
        }

        private async Task<int> ReceiveAsync(Socket socket, Memory<byte> memory, SocketFlags socketFlags)
        {
            return await socket.ReceiveAsync(GetArrayByMemory((ReadOnlyMemory<byte>)memory), socketFlags);
        }

        protected override async Task ProcessReads()
        {
            var pipe = new Pipe();

            Task writing = FillPipeAsync(_socket, pipe.Writer);
            Task reading = ReadPipeAsync(pipe.Reader);

            await Task.WhenAll(reading, writing);
        }

        protected override async ValueTask<int> SendAsync(ReadOnlySequence<byte> buffer)
        {
            if (buffer.IsSingleSegment)
            {
                return await _socket.SendAsync(GetArrayByMemory(buffer.First), SocketFlags.None);
            }
            
            if (_segmentsForSend == null)
            {
                _segmentsForSend = new List<ArraySegment<byte>>();
            }
            else
            {
                _segmentsForSend.Clear();
            }

            var segments = _segmentsForSend;

            foreach (var piece in buffer)
            {
                _segmentsForSend.Add(GetArrayByMemory(piece));
            }
            
            return await _socket.SendAsync(_segmentsForSend, SocketFlags.None);
        }

        public override void Close()
        {
            var socket = _socket;

            if (socket == null)
                return;

            if (Interlocked.CompareExchange(ref _socket, null, socket) == socket)
            {
                try
                {
                    socket.Shutdown(SocketShutdown.Both);
                }
                finally
                {
                    socket.Close();
                }
            }
        }
    }
}
