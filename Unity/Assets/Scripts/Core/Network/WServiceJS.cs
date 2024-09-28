#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using UnityWebSocket;

namespace ET
{
    public class WServiceJS: AService
    {
        private long idGenerater = 200000000;
        
        private WebSocket webSocket;
        
        private readonly Dictionary<long, WChannelJS> channels = new Dictionary<long, WChannelJS>();
        public ThreadSynchronizationContext ThreadSynchronizationContext;        

        public WServiceJS()
        {
            this.ThreadSynchronizationContext = new ThreadSynchronizationContext();
            this.ServiceType = ServiceType.Outer;
        }
        
        private long GetId
        {
            get
            {
                return ++this.idGenerater;
            }
        }
        
        public override void Create(long id, IPEndPoint ipEndpoint)
        {
            Log.Info($"ws://{ipEndpoint}");
			this.webSocket = new WebSocket($"ws://{ipEndpoint}");
            WChannelJS channel = new WChannelJS(id, webSocket, this);
            this.channels[channel.Id] = channel;
        }

        public override void Remove(long id, int error = 0)
        {
            WChannelJS channel;
            if (!this.channels.TryGetValue(id, out channel))
            {
                return;
            }

            channel.Error = error;

            this.channels.Remove(id);
            channel.Dispose();
        }

        public override bool IsDisposed()
        {
            return this.ThreadSynchronizationContext == null;
        }

        protected void Get(long id, IPEndPoint ipEndPoint)
        {
            if (!this.channels.TryGetValue(id, out _))
            {
                 this.Create(id, ipEndPoint);
            }
        }
        public WChannelJS Get(long id)
        {
            WChannelJS channel = null;
            this.channels.TryGetValue(id, out channel);
            return channel;
        }

        public override void Dispose()
        {
            this.ThreadSynchronizationContext = null;
            this.webSocket?.CloseAsync();
            this.webSocket = null;
        }       
        
        public override void Send(long channelId, MemoryBuffer memoryBuffer)
        {
            Log.Info("WServiceJS Send");
            this.channels.TryGetValue(channelId, out WChannelJS channel);
            if (channel == null)
            {
                return;
            }
            channel.Send(memoryBuffer);
        }

        public override void Update()
        {
            this.ThreadSynchronizationContext.Update();
        }

    }
}
#endif