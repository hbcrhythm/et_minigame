﻿using System;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Net.WebSockets;
using System.Buffers;
using System.Collections.Concurrent;

namespace ET
{
    public interface IKcpTransport: IDisposable
    {
        void Send(byte[] bytes, int index, int length, EndPoint endPoint);
        int Recv(byte[] buffer, ref EndPoint endPoint);
        int Available();
        void Update();
        void OnError(long id, int error);
    }

    public class UdpTransport: IKcpTransport
    {
        private readonly Socket socket;

        public UdpTransport(AddressFamily addressFamily)
        {
            this.socket = new Socket(addressFamily, SocketType.Dgram, ProtocolType.Udp);
            NetworkHelper.SetSioUdpConnReset(this.socket);
        }
        
        public UdpTransport(IPEndPoint ipEndPoint)
        {
            this.socket = new Socket(ipEndPoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                this.socket.SendBufferSize = Kcp.OneM * 64;
                this.socket.ReceiveBufferSize = Kcp.OneM * 64;
            }
            
            try
            {
                this.socket.Bind(ipEndPoint);
            }
            catch (Exception e)
            {
                throw new Exception($"bind error: {ipEndPoint}", e);
            }

            NetworkHelper.SetSioUdpConnReset(this.socket);
        }
        
        public void Send(byte[] bytes, int index, int length, EndPoint endPoint)
        {
            this.socket.SendTo(bytes, index, length, SocketFlags.None, endPoint);
        }
        
        public int Recv(byte[] buffer, ref EndPoint endPoint)
        {
            return this.socket.ReceiveFrom(buffer, ref endPoint);
        }

        public int Available()
        {
            return this.socket.Available;
        }

        public void Update()
        {
        }

        public void OnError(long id, int error)
        {
        }

        public void Dispose()
        {
            this.socket?.Dispose();
        }
    }

    public class TcpTransport: IKcpTransport
    {
        private readonly TService tService;

        private readonly DoubleMap<long, EndPoint> idEndpoints = new();

        private readonly Queue<(EndPoint, MemoryBuffer)> channelRecvDatas = new();

        private readonly Dictionary<long, long> readWriteTime = new();

        private readonly Queue<long> channelIds = new();
        
        public TcpTransport(AddressFamily addressFamily)
        {
            this.tService = new TService(addressFamily, ServiceType.Outer);
            this.tService.ErrorCallback = this.OnError;
            this.tService.ReadCallback = this.OnRead;
        }
        
        public TcpTransport(IPEndPoint ipEndPoint)
        {
            this.tService = new TService(ipEndPoint, ServiceType.Outer);
            this.tService.AcceptCallback = this.OnAccept;
            this.tService.ErrorCallback = this.OnError;
            this.tService.ReadCallback = this.OnRead;
        }

        private void OnAccept(long id, IPEndPoint ipEndPoint)
        {
            TChannel channel = this.tService.Get(id);
            long timeNow = TimeInfo.Instance.ClientFrameTime();
            this.readWriteTime[id] = timeNow;
            this.channelIds.Enqueue(id);
            this.idEndpoints.Add(id, channel.RemoteAddress);
        }

        public void OnError(long id, int error)
        {
            Log.Warning($"IKcpTransport tcp error: {id} {error}");
            this.tService.Remove(id, error);
            this.idEndpoints.RemoveByKey(id);
            this.readWriteTime.Remove(id);
        }
        
        private void OnRead(long id, MemoryBuffer memoryBuffer)
        {
            long timeNow = TimeInfo.Instance.ClientFrameTime();
            this.readWriteTime[id] = timeNow;
            TChannel channel = this.tService.Get(id);
            channelRecvDatas.Enqueue((channel.RemoteAddress, memoryBuffer));
        }
        
        public void Send(byte[] bytes, int index, int length, EndPoint endPoint)
        {
            long id = this.idEndpoints.GetKeyByValue(endPoint);
            if (id == 0)
            {
                id = IdGenerater.Instance.GenerateInstanceId();
                this.tService.Create(id, (IPEndPoint)endPoint);
                this.idEndpoints.Add(id, endPoint);
                this.channelIds.Enqueue(id);
            }
            MemoryBuffer memoryBuffer = this.tService.Fetch();
            memoryBuffer.Write(bytes, index, length);
            memoryBuffer.Seek(0, SeekOrigin.Begin);
            this.tService.Send(id, memoryBuffer);
            
            long timeNow = TimeInfo.Instance.ClientFrameTime();
            this.readWriteTime[id] = timeNow;
        }

        public int Recv(byte[] buffer, ref EndPoint endPoint)
        {
            return RecvNonAlloc(buffer, ref endPoint);
        }

        public int RecvNonAlloc(byte[] buffer, ref EndPoint endPoint)
        {
            (EndPoint e, MemoryBuffer memoryBuffer) = this.channelRecvDatas.Dequeue();
            endPoint = e;
            int count = memoryBuffer.Read(buffer);
            this.tService.Recycle(memoryBuffer);
            return count;
        }

        public int Available()
        {
            return this.channelRecvDatas.Count;
        }

        public void Update()
        {
            // 检查长时间不读写的TChannel, 超时断开, 一次update检查10个
            long timeNow = TimeInfo.Instance.ClientFrameTime();
            const int MaxCheckNum = 10;
            int n = this.channelIds.Count < MaxCheckNum? this.channelIds.Count : MaxCheckNum;
            for (int i = 0; i < n; ++i)
            {
                long id = this.channelIds.Dequeue();
                if (!this.readWriteTime.TryGetValue(id, out long rwTime))
                {
                    continue;
                }
                if (timeNow - rwTime > 30 * 1000)
                {
                    this.OnError(id, ErrorCore.ERR_KcpReadWriteTimeout);
                    continue;
                }
                this.channelIds.Enqueue(id);
            }
            
            this.tService.Update();
        }

        public void Dispose()
        {
            this.tService?.Dispose();
        }
    }
    public class WebSocketTransport : IKcpTransport
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        private readonly WServiceJS wService;
#else
        private readonly WService wService;
#endif
        
        private readonly DoubleMap<long, EndPoint> idEndpoints = new();

        private readonly Queue<(EndPoint, MemoryBuffer)> channelRecvDatas = new();

        private readonly Dictionary<long, long> readWriteTime = new();

        private readonly Queue<long> channelIds = new();
        
#if UNITY_WEBGL && !UNITY_EDITOR
        public WebSocketTransport(IPEndPoint ipEndPoint)
        {}
#else
        public WebSocketTransport(IPEndPoint ipEndPoint)
        {
            string prefix = $"http://{ipEndPoint.Address}:{ipEndPoint.Port}/";
            var prefixs = new List<string> { prefix };
            this.wService = new WService(prefixs);
            this.wService.AcceptCallback = this.OnAccept;
            this.wService.ErrorCallback = this.OnError;
            this.wService.ReadCallback = this.OnRead;
        }
#endif     
        public WebSocketTransport(AddressFamily addressFamily)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            this.wService = new WServiceJS();
#else
            this.wService = new WService();
#endif
            this.wService.AcceptCallback = this.OnAccept;
            this.wService.ErrorCallback = this.OnError;
            this.wService.ReadCallback = this.OnRead;
        }


        private void OnAccept(long id, IPEndPoint ipEndPoint)
        {
            Log.Info("Onaccept 接收数据回调");
            var channel = this.wService.Get(id);
            long timeNow = TimeInfo.Instance.ClientFrameTime();
            this.readWriteTime[id] = timeNow;
            this.channelIds.Enqueue(id);
            this.idEndpoints.Add(id, channel.RemoteAddress);
        }

        public void OnError(long id, int error)
        {
            Log.Warning($"IKcpTransport WebSocket error: {id} {error}");
            this.wService.Remove(id, error);
            this.idEndpoints.RemoveByKey(id);
            this.readWriteTime.Remove(id);
        }

        private void OnRead(long id, MemoryBuffer memoryBuffer)
        {
            long timeNow = TimeInfo.Instance.ClientFrameTime();
            this.readWriteTime[id] = timeNow;
            var channel = this.wService.Get(id);
            channelRecvDatas.Enqueue((channel.RemoteAddress, memoryBuffer));
        }

        public void Send(byte[] bytes, int index, int length, EndPoint endPoint)
        {
            long id = this.idEndpoints.GetKeyByValue(endPoint);
            if (id == 0)
            {
                id = IdGenerater.Instance.GenerateInstanceId();
                this.wService.Create(id, (IPEndPoint)endPoint);
                this.idEndpoints.Add(id, endPoint);
                this.channelIds.Enqueue(id);
            }
            MemoryBuffer memoryBuffer = this.wService.Fetch();
            memoryBuffer.Write(bytes, index, length);
            memoryBuffer.Seek(0, SeekOrigin.Begin);
            this.wService.Send(id, memoryBuffer);

            long timeNow = TimeInfo.Instance.ClientFrameTime();
            this.readWriteTime[id] = timeNow;
        }

        public int Recv(byte[] buffer, ref EndPoint endPoint)
        {
            return RecvNonAlloc(buffer, ref endPoint);
        }

        public int RecvNonAlloc(byte[] buffer, ref EndPoint endPoint)
        {
            (EndPoint e, MemoryBuffer memoryBuffer) = this.channelRecvDatas.Dequeue();
            endPoint = e;
            int count = memoryBuffer.Read(buffer);
            this.wService.Recycle(memoryBuffer);
            return count;
        }

        public int Available()
        {
            return this.channelRecvDatas.Count;
        }

        public void Update()
        {
            // 检查长时间不读写的WChannel, 超时断开, 一次update检查10个
            long timeNow = TimeInfo.Instance.ClientFrameTime();
            const int MaxCheckNum = 10;
            int n = this.channelIds.Count < MaxCheckNum ? this.channelIds.Count : MaxCheckNum;
            for (int i = 0; i < n; ++i)
            {
                long id = this.channelIds.Dequeue();
                if (!this.readWriteTime.TryGetValue(id, out long rwTime))
                {
                    continue;
                }
                if (timeNow - rwTime > 30 * 1000)
                {
                    this.OnError(id, ErrorCore.ERR_KcpReadWriteTimeout);
                    continue;
                }
                this.channelIds.Enqueue(id);
            }

            this.wService.Update();
        }

        public void Dispose()
        {
            this.wService?.Dispose();
        }
    }
}