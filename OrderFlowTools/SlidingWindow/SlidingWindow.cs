using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

namespace Gemify.OrderFlow.SlidingWindow
{
    internal class SlidingWindow<T>
    {
        protected ConcurrentDictionary<double, SlidingWindowItem<T>> window;
        private Timer timer;
        private int initialWaitTimeSeconds;
        private int windowCleanInterval;
        private int ttlSeconds;
        private const int _DefaultCleanInterval = 5;

        public SlidingWindow(int TTLSeconds) : this(TTLSeconds, _DefaultCleanInterval)
        {
            // Defaults to _DefaultCleanInterval
        }

        public SlidingWindow(int TTLSeconds, int WindowCleanInterval)
        {
            // Wait a second before starting cleanup
            initialWaitTimeSeconds = 1;

            // TTL to use
            this.ttlSeconds = TTLSeconds;

            // Window cleaning interval
            this.windowCleanInterval = WindowCleanInterval;

            // Initialize the dictionary
            this.window = new ConcurrentDictionary<double, SlidingWindowItem<T>>();

            // Ensure minimum 1 second
            TTLSeconds = Math.Max(TTLSeconds, 1);
            this.timer = new Timer(new TimerCallback(WindowCleaner), null, initialWaitTimeSeconds * 1000, WindowCleanInterval * 1000);
        }

        private void WindowCleaner(object state)
        {
            lock (window)
            {
                foreach (double key in window.Keys)
                {
                    SlidingWindowItem<T> item = window[key];
                    if (item != null)
                    {
                        TimeSpan diff = DateTime.Now - item.GetTime();
                        if (diff.TotalSeconds > ttlSeconds)
                        {
                            Remove(key);
                        }
                    }
                }
            }
        }

        protected internal ICollection<double> Keys()
        {
            return window.Keys;
        }

        protected internal T Get(double key)
        {
            SlidingWindowItem<T> swi;
            window.TryGetValue(key, out swi);
            return swi == null ? default(T) : swi.GetItem();
        }

        protected internal T AddOrUpdate(double key, T item)
        {
            return AddOrUpdate(key, item, DateTime.Now);
        }

        protected internal T AddOrUpdate(double key, T item, DateTime time)
        {
            SlidingWindowItem<T> swi = new SlidingWindowItem<T>(item, time);
            lock (window)
            {
                this.window.AddOrUpdate(key, swi, (k, v) => swi);
            }
            return swi.GetItem();
        }

        protected internal bool Remove(double key)
        {
            lock (window)
            {
                SlidingWindowItem<T> item;
                return window.TryRemove(key, out item);
            }
        }

        protected internal void Clear()
        {
            lock (window)
            {
                window.Clear();
            }
        }
    }
}
