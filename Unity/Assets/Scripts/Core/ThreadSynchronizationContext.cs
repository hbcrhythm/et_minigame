using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Collections.Generic;

namespace ET
{

#if UNITY_WEBGL    
    public class ThreadSynchronizationContext
    {
        private readonly Queue<Action> queue = new();
#else
    public class ThreadSynchronizationContext : SynchronizationContext
    {
        // 线程同步队列,发送接收socket回调都放到该队列,由poll线程统一执行
        private readonly ConcurrentQueue<Action> queue = new();
#endif
        private Action a;

        public void Update()
        {
            while (true)
            {
                if (!this.queue.TryDequeue(out a))
                {
                    return;
                }

                try
                {
                    Log.Info($"隊列存在函數 {a}");
                    a();
                }
                catch (Exception e)
                {
                    Log.Error(e);
                }
            }
        }
    
#if UNITY_WEBGL
        public void Post(SendOrPostCallback callback, object state)
        {
#else
        public override void Post(SendOrPostCallback callback, object state)
        {
#endif    
            this.Post(() => callback(state));
        }
		
        public void Post(Action action)
        {
            Log.Info($"放進隊列 {action}");
            this.queue.Enqueue(action);
        }
    }
}