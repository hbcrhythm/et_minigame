using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace ET
{
    internal class MainThreadScheduler: IScheduler
    {
        private readonly ConcurrentQueue<int> idQueue = new();
        private readonly ConcurrentQueue<int> addIds = new();
        private readonly FiberManager fiberManager;
        private readonly ThreadSynchronizationContext threadSynchronizationContext = new();

        public MainThreadScheduler(FiberManager fiberManager)
        {
#if !UNITY_WEBGL
            SynchronizationContext.SetSynchronizationContext(this.threadSynchronizationContext);
#endif
            this.fiberManager = fiberManager;
        }

        public void Dispose()
        {
            this.addIds.Clear();
            this.idQueue.Clear();
        }

        public void Update()
        {
#if !UNITY_WEBGL
            SynchronizationContext.SetSynchronizationContext(this.threadSynchronizationContext);
#endif
            this.threadSynchronizationContext.Update();
            
            int count = this.idQueue.Count;
            while (count-- > 0)
            {
                if (!this.idQueue.TryDequeue(out int id))
                {
                    continue;
                }

                Fiber fiber = this.fiberManager.Get(id);
                if (fiber == null)
                {
                    continue;
                }
                
                if (fiber.IsDisposed)
                {
                    continue;
                }
                
                Fiber.Instance = fiber;
#if !UNITY_WEBGL
                SynchronizationContext.SetSynchronizationContext(fiber.ThreadSynchronizationContext);
#endif
                fiber.Update();
                Fiber.Instance = null;
                
                this.idQueue.Enqueue(id);
            }
            
#if !UNITY_WEBGL
            // Fiber调度完成，要还原成默认的上下文，否则unity的回调会找不到正确的上下文
            SynchronizationContext.SetSynchronizationContext(this.threadSynchronizationContext);
#endif
        }

        public void LateUpdate()
        {
            int count = this.idQueue.Count;
            while (count-- > 0)
            {
                if (!this.idQueue.TryDequeue(out int id))
                {
                    continue;
                }

                Fiber fiber = this.fiberManager.Get(id);
                if (fiber == null)
                {
                    continue;
                }

                if (fiber.IsDisposed)
                {
                    continue;
                }

                Fiber.Instance = fiber;
#if !UNITY_WEBGL
                SynchronizationContext.SetSynchronizationContext(fiber.ThreadSynchronizationContext);
#endif
                fiber.LateUpdate();
                Fiber.Instance = null;
                
                this.idQueue.Enqueue(id);
            }

            while (this.addIds.Count > 0)
            {
                this.addIds.TryDequeue(out int result);
                this.idQueue.Enqueue(result);
            }

#if !UNITY_WEBGL
            // Fiber调度完成，要还原成默认的上下文，否则unity的回调会找不到正确的上下文
            SynchronizationContext.SetSynchronizationContext(this.threadSynchronizationContext);
#endif
        }


        public void Add(int fiberId = 0)
        {
            this.addIds.Enqueue(fiberId);
        }
    }
}