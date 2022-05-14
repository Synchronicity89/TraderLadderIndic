using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gemify.OrderFlow.OrderBook
{
    internal class OrderBook
    {
        private ConcurrentDictionary<double, BidAsk> CurrBid;
        private ConcurrentDictionary<double, BidAsk> CurrAsk;
        private ConcurrentDictionary<double, BidAsk> PrevBid;
        private ConcurrentDictionary<double, BidAsk> PrevAsk;
        private ConcurrentDictionary<double, long> BidChange;
        private ConcurrentDictionary<double, long> AskChange;

        private long nPosBidChanges = 0;
        private long nPosAskChanges = 0;
        private long nNegBidChanges = 0;
        private long nNegAskChanges = 0;

        public OrderBook()
        {
            PrevBid = new ConcurrentDictionary<double, BidAsk>();
            PrevAsk = new ConcurrentDictionary<double, BidAsk>();
            CurrBid = new ConcurrentDictionary<double, BidAsk>();
            CurrAsk = new ConcurrentDictionary<double, BidAsk>();

            BidChange = new ConcurrentDictionary<double, long>();
            AskChange = new ConcurrentDictionary<double, long>();
        }

        internal BidAsk AddOrUpdateBid(double price, long size, DateTime time)
        {
            // Copy current value into prev
            BidAsk currBidAsk = null;
            if (CurrBid.TryGetValue(price, out currBidAsk)) {
                PrevBid.AddOrUpdate(price, currBidAsk, (key, existing) => currBidAsk);
            }

            // Change in bid size
            long change = Convert.ToInt64(size - (currBidAsk == null ? 0 : currBidAsk.Size));
            BidChange.AddOrUpdate(price, change, (key, value) => change);

            // Keep track of positive and negative changes
            if (change > 0) nPosBidChanges++;
            else if (change < 0) nNegBidChanges++;

            // Add or replace current entry
            BidAsk newBidAsk = new BidAsk(size, time);
            return CurrBid.AddOrUpdate(price, newBidAsk, (key, existing) => newBidAsk);
        }

        internal BidAsk AddOrUpdateAsk(double price, long size, DateTime time)
        {
            // Copy current value into prev
            BidAsk currBidAsk = null;
            if (CurrAsk.TryGetValue(price, out currBidAsk))
            {
                PrevAsk.AddOrUpdate(price, currBidAsk, (key, existing) => currBidAsk);
            }

            // Change in ask size
            long change = Convert.ToInt64(size - (currBidAsk == null ? 0 : currBidAsk.Size));
            AskChange.AddOrUpdate(price, change, (key, value) => change);

            // Keep track of positive and negative changes
            if (change > 0) nPosAskChanges++;
            else if (change < 0) nNegAskChanges++;

            // Add or replace current entry
            BidAsk newBidAsk = new BidAsk(size, time);
            return CurrAsk.AddOrUpdate(price, newBidAsk, (key, existing) => newBidAsk);
        }

        internal BidAsk GetPreviousBid(double price)
        {
            BidAsk bidAsk = null;
            PrevBid.TryGetValue(price, out bidAsk);
            return bidAsk;            
        }

        internal BidAsk GetPreviousAsk(double price)
        {
            BidAsk bidAsk = null;
            PrevAsk.TryGetValue(price, out bidAsk);
            return bidAsk;
        }

        internal BidAsk GetCurrentBid(double price)
        {
            BidAsk bidAsk = null;
            CurrBid.TryGetValue(price, out bidAsk);
            return bidAsk;
        }

        internal BidAsk GetCurrentAsk(double price)
        {
            BidAsk bidAsk = null;
            CurrAsk.TryGetValue(price, out bidAsk);
            return bidAsk;
        }

        internal long GetBidChange(double price)
        {
            long change = 0;
            BidChange.TryGetValue(price, out change);
            return change;
        }

        internal long GetAskChange(double price)
        {
            long change = 0;
            AskChange.TryGetValue(price, out change);
            return change;
        }

        internal long GetPositiveBidChanges()
        {
            return nPosBidChanges;
        }
        internal long GetNegativeBidChanges()
        {
            return nNegBidChanges;
        }
        internal long GetPositiveAskChanges()
        {
            return nPosAskChanges;
        }
        internal long GetNegativeAskChanges()
        {
            return nNegAskChanges;
        }

        internal long GetOrderBookSizeBid()
        {
            return CurrBid.Count;
        }
        internal long GetOrderBookSizeAsk()
        {
            return CurrAsk.Count;
        }
    }
}
