#if UNITY_WEBGL && !UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.IO;
using UnityWebSocket;

namespace ET
{
    public class WChannelJS : AChannel
    {

        private readonly WServiceJS Service;

        private readonly WebSocket webSocket;

        private readonly Queue<MemoryBuffer> queue = new();

        private bool isSending;

        private bool isConnected;

        private readonly MemoryStream recvStream;

        private byte[] cache = new byte[ushort.MaxValue];

        private ETTask tcs;
        public WChannelJS(long id, WebSocket webSocket, WServiceJS service)
        {
            this.Id = id;
            this.Service = service;
            this.ChannelType = ChannelType.Connect;
            this.webSocket = webSocket;
            this.recvStream = new MemoryStream(ushort.MaxValue);

            isConnected = false;
            tcs = ETTask.Create();
Log.Info("WChannelJS ");
            this.Service.ThreadSynchronizationContext.Post(() => this.ConnectAsync().Coroutine());
        }

        public override void Dispose()
        {
            if (this.IsDisposed)
            {
                return;
            }


            this.webSocket?.CloseAsync();
        }

        public async ETTask ConnectAsync()
        {
            try
            {
                Log.Info("ConnectAsync === ");
                this.webSocket.OnOpen += SocketOnOpen;
                this.webSocket.OnMessage += SocketOnMessage;
                this.webSocket.OnClose += SocketOnClose;
                this.webSocket.OnError += SocketOnError;
                this.webSocket.ConnectAsync();

                await tcs;

                StartSend();
            }
            catch (Exception e)
            {
                Log.Error(e);
                this.OnError(ErrorCore.ERR_WebsocketConnectError);
            }
        }

        private void SocketOnError(object sender, UnityWebSocket.ErrorEventArgs e)
        {
            Log.Error($"Error: {e.Message}");
        }

        private void SocketOnClose(object sender, UnityWebSocket.CloseEventArgs e)
        {
            this.isConnected = false;
            Log.Info($"Closed: StatusCode: {e.StatusCode}, Reason: {e.Reason}");
        }

        private void SocketOnMessage(object sender, MessageEventArgs e)
        {
            if (this.IsDisposed)
            {
                return;
            }

            try
            {

                if (this.webSocket.ReadyState == WebSocketState.Closed)
                {
                    this.OnError(ErrorCore.ERR_WebsocketPeerReset);
                    return;
                }

                if (e.RawData.Length > ushort.MaxValue)
                {
                    this.webSocket.CloseAsync();
                    this.OnError(ErrorCore.ERR_WebsocketMessageTooBig);
                    return;
                }
                
                MemoryBuffer memoryBuffer = this.Service.Fetch(e.RawData.Length);
                memoryBuffer.SetLength(e.RawData.Length);
                memoryBuffer.Seek(0, SeekOrigin.Begin);
                Array.Copy(e.RawData, 0, memoryBuffer.GetBuffer(), 0, e.RawData.Length);

                // this.recvStream.SetLength(e.RawData.Length);wser
                // this.recvStream.Seek(2, SeekOrigin.Begin);
                // Array.Copy(e.RawData, 0, this.recvStream.GetBuffer(), 0, e.RawData.Length);
Log.Info("SOcket 接受到数据");
                this.OnRead(memoryBuffer);

            }
            catch (Exception ex)
            {
                Log.Error(ex);
                this.OnError(ErrorCore.ERR_WebsocketRecvError);
            }
        }


        private void SocketOnOpen(object sender, OpenEventArgs e)
        {
Log.Info("链接成功 ");
            this.isConnected = true;
            tcs.SetResult();
        }

        public void Send(MemoryBuffer memoryBuffer)
        {
            this.queue.Enqueue(memoryBuffer);

            if (this.isConnected)
            {
                this.StartSend();
            }
        }

        public void StartSend()
        {
            if (this.IsDisposed)
            {
                return;
            }

            try
            {
                if (this.isSending)
                {
                    return;
                }

                this.isSending = true;

                while (true)
                {
                    if (this.queue.Count == 0)
                    {
                        this.isSending = false;
                        return;
                    }

                    MemoryBuffer stream  = this.queue.Dequeue();
                    try
                    {
Log.Info("最终发送出去");
                        this.webSocket.SendAsync(stream.GetMemory());
                        this.Service.Recycle(stream);
                        
                        if (this.IsDisposed)
                        {
                            return;
                        }
                    }
                    catch (Exception e)
                    {
                        Log.Error(e);
                        this.OnError(ErrorCore.ERR_WebsocketSendError);
                        return;
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error(e);
            }
        }


        private void OnRead(MemoryBuffer memoryStream)
        {
            try
            {
                this.Service.ReadCallback(this.Id, memoryStream);
            }
            catch (Exception e)
            {
                Log.Error(e);
                this.OnError(ErrorCore.ERR_PacketParserError);
            }
        }

        private void OnError(int error)
        {
            Log.Debug($"WChannel error: {error} {this.RemoteAddress}");

            long channelId = this.Id;

            this.Service.Remove(channelId);

            this.Service.ErrorCallback(channelId, error);
        }
    }
}
#endif