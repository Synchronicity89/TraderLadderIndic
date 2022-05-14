using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gemify.OrderFlow.SlidingWindow
{
    internal class SlidingWindowItem<T>
    {
        private readonly T Item;
        private DateTime Time;

        public SlidingWindowItem(T Item, DateTime Time)
        {
            this.Item = Item;
            this.Time = Time;
        }

        protected internal T GetItem()
        {
            return this.Item;
        }

        protected internal DateTime GetTime()
        {
            return Time;
        }
    }
}
