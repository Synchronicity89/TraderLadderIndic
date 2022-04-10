// 
// Copyright (C) 2021, Gem Immanuel (gemify@gmail.com)
//

using NinjaTrader.Cbi;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NinjaTrader.NinjaScript.Indicators;

namespace Gemify.OrderFlow
{
    class OFStrength
    {
        internal double buyStrength = 0.0;
        internal double sellStrength = 0.0;
    }

    class Trade
    {
        internal long Size { get; set; }
        internal double Ask { get; set; }
        internal double Bid { get; set; }
        internal DateTime Time { get; set; }
    }

    class BidAsk
    {
        internal double Size { get; set; }
        internal DateTime Time { get; set; }
    }

    class BidAskPerc : BidAsk
    {
        internal double Perc { get; set; }
    }
	
	enum TradeAggressor
    {
        BUYER,
        SELLER
    }

    public enum OFSCalculationMode
    {
        COMBINED,
        IMBALANCE,
        BUY_SELL
    }

    class GemsOrderFlow
    {

        protected ITradeClassifier tradeClassifier;

        private ConcurrentDictionary<double, Trade> SlidingWindowBuys;
        private ConcurrentDictionary<double, Trade> SlidingWindowSells;
        private ConcurrentDictionary<double, BidAsk> SlidingWindowBids;
        private ConcurrentDictionary<double, BidAsk> SlidingWindowAsks;
        private ConcurrentDictionary<double, long> LastBuy;
        private ConcurrentDictionary<double, long> LastSell;
        private ConcurrentDictionary<double, BidAskPerc> BidsPerc;
        private ConcurrentDictionary<double, BidAskPerc> AsksPerc;        
        private ConcurrentDictionary<double, long> LastBuyPrint;
        private ConcurrentDictionary<double, long> LastSellPrint;
        private ConcurrentDictionary<double, long> LastBuyPrintMax;
        private ConcurrentDictionary<double, long> LastSellPrintMax;        

        private ConcurrentDictionary<double, long> TotalBuys;
        private ConcurrentDictionary<double, long> TotalSells;
        private ConcurrentDictionary<double, long> PrevBid;
        private ConcurrentDictionary<double, long> PrevAsk;
        
        private double imbalanceFactor;
        private long imbalanceInvalidateDistance;
        private const int minSlidingWindowTrades = 1;

        // To support Print
        private Indicator ind;

        public GemsOrderFlow (ITradeClassifier tradeClassifier, double imbalanceFactor)
        {
            ind = new Indicator();

            this.tradeClassifier = tradeClassifier;

            this.imbalanceFactor = imbalanceFactor;
            this.imbalanceInvalidateDistance = 10;

            SlidingWindowBuys = new ConcurrentDictionary<double, Trade>();
            SlidingWindowSells = new ConcurrentDictionary<double, Trade>();
            SlidingWindowBids = new ConcurrentDictionary<double, BidAsk>();
            SlidingWindowAsks = new ConcurrentDictionary<double, BidAsk>();            
            TotalBuys = new ConcurrentDictionary<double, long>();
            TotalSells = new ConcurrentDictionary<double, long>();

            LastBuy = new ConcurrentDictionary<double, long>();
            LastSell = new ConcurrentDictionary<double, long>();
            LastBuyPrint = new ConcurrentDictionary<double, long>();
            LastSellPrint = new ConcurrentDictionary<double, long>();
            LastBuyPrintMax = new ConcurrentDictionary<double, long>();
            LastSellPrintMax = new ConcurrentDictionary<double, long>();
            PrevAsk = new ConcurrentDictionary<double, long>();
            PrevBid = new ConcurrentDictionary<double, long>();
            BidsPerc = new ConcurrentDictionary<double, BidAskPerc>();
            AsksPerc = new ConcurrentDictionary<double, BidAskPerc>();
        }

        internal void ClearAll()
        {

            ClearSlidingWindow();

            TotalBuys.Clear();
            TotalSells.Clear();

            PrevBid.Clear();
            PrevAsk.Clear();

            BidsPerc.Clear();
            AsksPerc.Clear();
        }

        internal void ClearSlidingWindow()
        {
            SlidingWindowBuys.Clear();
            SlidingWindowSells.Clear();
            SlidingWindowBids.Clear();
            SlidingWindowAsks.Clear();            
            LastBuy.Clear();
            LastSell.Clear();
            LastBuyPrint.Clear();
            LastSellPrint.Clear();
            LastBuyPrintMax.Clear();
            LastSellPrintMax.Clear();
        }

        private void Print(string s)
        {
            ind.Print(s);
        }

        internal void CalculateBidAskPerc(List<NinjaTrader.Gui.SuperDom.LadderRow> askLadder, List<NinjaTrader.Gui.SuperDom.LadderRow> bidLadder) {
            AsksPerc.Clear();
            BidsPerc.Clear();
            double maxBidAsk = 0;
            foreach (NinjaTrader.Gui.SuperDom.LadderRow row in askLadder)
            {
                maxBidAsk = row.Volume > maxBidAsk ? row.Volume : maxBidAsk;
            }
            foreach (NinjaTrader.Gui.SuperDom.LadderRow row in bidLadder)
            {
                maxBidAsk = row.Volume > maxBidAsk ? row.Volume : maxBidAsk;
            }

            foreach (NinjaTrader.Gui.SuperDom.LadderRow row in askLadder)
            {
                BidAskPerc perc = new BidAskPerc();
                perc.Size = row.Volume;
                perc.Perc = row.Volume/maxBidAsk;
                AsksPerc.AddOrUpdate(row.Price, perc, (price, existing) => existing = perc);
            }

            foreach (NinjaTrader.Gui.SuperDom.LadderRow row in bidLadder)
            {
                BidAskPerc perc = new BidAskPerc();
                perc.Size = row.Volume;
                perc.Perc = row.Volume/maxBidAsk;
                BidsPerc.AddOrUpdate(row.Price, perc, (price, existing) => existing = perc);
            }
        }


        /*
         * Classifies given trade as either buyer or seller initiated based on configured classifier.
         */
        internal void ClassifyTrade(bool updateSlidingWindow, double ask, double bid, double close, long volume, DateTime time)
        {
            TradeAggressor aggressor = tradeClassifier.ClassifyTrade(ask, bid, close, volume, time);

            // Classification - buyers vs. sellers
            if (aggressor == TradeAggressor.BUYER)
            {
                Trade oldTrade;
                bool gotOldTrade = SlidingWindowBuys.TryGetValue(close, out oldTrade);

                Trade trade = new Trade();
                trade.Ask = ask;
                trade.Time = time;

                if (gotOldTrade)
                {
                    trade.Size = oldTrade.Size + volume;
                }
                else
                {
                    trade.Size = volume;
                }

                if (updateSlidingWindow)
                {
                    SlidingWindowBuys.AddOrUpdate(close, trade, (price, existingTrade) => existingTrade = trade);
                    // Update last buy
                    LastBuy.AddOrUpdate(close, volume, (price, oldVolume) => volume);
                    LastBuyPrint.AddOrUpdate(close, volume, (price, oldVolume) => volume);
                    long lastMax = 0;
                    LastBuyPrintMax.TryGetValue(close, out lastMax);
                    if (volume > lastMax) {
                        LastBuyPrintMax.AddOrUpdate(close, volume, (price, oldVolume) => volume);
                    }
                }
                TotalBuys.AddOrUpdate(close, volume, (price, oldVolume) => oldVolume + volume);
            }
            else if (aggressor == TradeAggressor.SELLER)
            {
                Trade oldTrade;
                bool gotOldTrade = SlidingWindowSells.TryGetValue(close, out oldTrade);

                Trade trade = new Trade();
                trade.Bid = bid;
                trade.Time = time;

                if (gotOldTrade)
                {
                    trade.Size = oldTrade.Size + volume;
                }
                else
                {
                    trade.Size = volume;
                }

                if (updateSlidingWindow)
                {
                    SlidingWindowSells.AddOrUpdate(close, trade, (price, existingTrade) => existingTrade = trade);
                    // Update last sell
                    LastSell.AddOrUpdate(close, volume, (price, oldVolume) => volume);
                    LastSellPrint.AddOrUpdate(close, volume, (price, oldVolume) => volume);
                    long lastMax = 0;
                    LastSellPrintMax.TryGetValue(close, out lastMax);
                    if (volume > lastMax) {
                        LastSellPrintMax.AddOrUpdate(close, volume, (price, oldVolume) => volume);
                    }
                }
                TotalSells.AddOrUpdate(close, volume, (price, oldVolume) => oldVolume + volume);
            }
        }

        /*
        * Gets total buy volume in the sliding window
        */
        internal long GetBuysInSlidingWindow()
        {
            long total = 0;
            foreach (Trade trade in SlidingWindowBuys.Values)
            {
                total += trade.Size;
            }
            return total;
        }        

        /*
        * Gets total sell volume in the sliding window
        */
        internal long GetSellsInSlidingWindow()
        {
            long total = 0;
            foreach (Trade trade in SlidingWindowSells.Values)
            {
                total += trade.Size;
            }
            return total;
        }      

        /*
         * Gets total volume transacted (buyers + sellers) at given price.
         */
        internal long GetVolumeAtPrice(double price)
        {
            long buyVolume = 0, sellVolume = 0;
            TotalBuys.TryGetValue(price, out buyVolume);
            TotalSells.TryGetValue(price, out sellVolume);
            long totalVolume = buyVolume + sellVolume;
            return totalVolume;
        }

        /* 
         * Clear out trades from the buys and sells collection if the trade entries 
         * fall outside (older trades) of a sliding time window (seconds), 
         * thus preserving only the latest trades based on the time window.
         */
        internal void ClearTradesOutsideSlidingWindow(DateTime time, int TradeSlidingWindowSeconds)
        {
            foreach (double price in SlidingWindowBuys.Keys)
            {
                Trade trade;
                bool gotTrade = SlidingWindowBuys.TryGetValue(price, out trade);
                if (gotTrade)
                {
                    TimeSpan diff = time - trade.Time;
                    if (diff.TotalSeconds > TradeSlidingWindowSeconds)
                    {
                        SlidingWindowBuys.TryRemove(price, out trade);
                        long oldVolume;
                        LastBuy.TryRemove(price, out oldVolume);
                        LastBuyPrint.TryRemove(price, out oldVolume);
                        LastBuyPrintMax.TryRemove(price, out oldVolume);
                    }
                }
            }

            foreach (double price in SlidingWindowSells.Keys)
            {
                Trade trade;
                bool gotTrade = SlidingWindowSells.TryGetValue(price, out trade);
                if (gotTrade)
                {
                    TimeSpan diff = time - trade.Time;
                    if (diff.TotalSeconds > TradeSlidingWindowSeconds)
                    {
                        SlidingWindowSells.TryRemove(price, out trade);
                        long oldVolume;
                        LastSell.TryRemove(price, out oldVolume);
                        LastSellPrint.TryRemove(price, out oldVolume);
                        LastSellPrintMax.TryRemove(price, out oldVolume);
                    }
                }
            }

            foreach (double price in SlidingWindowBids.Keys)
            {
                BidAsk bidAsk;
                bool gotItem = SlidingWindowBids.TryGetValue(price, out bidAsk);
                if (gotItem)
                {
                    TimeSpan diff = time - bidAsk.Time;
                    if (diff.TotalSeconds > TradeSlidingWindowSeconds)
                    {
                        SlidingWindowBids.TryRemove(price, out bidAsk);
                    }
                }
            } 

            foreach (double price in SlidingWindowAsks.Keys)
            {
                BidAsk bidAsk;
                bool gotItem = SlidingWindowAsks.TryGetValue(price, out bidAsk);
                if (gotItem)
                {
                    TimeSpan diff = time - bidAsk.Time;
                    if (diff.TotalSeconds > TradeSlidingWindowSeconds)
                    {
                        SlidingWindowAsks.TryRemove(price, out bidAsk);
                    }
                }
            }                        
        }

        internal long GetImbalancedBuys(double currentPrice, double tickSize)
        {
            long buyImbalance = 0;

            foreach (double buyPrice in SlidingWindowBuys.Keys)
            {
                // If we've blown past "imbalance", the imbalance did not hold up. It does not indicate strength.
                if (currentPrice < buyPrice - (imbalanceInvalidateDistance * tickSize))
                {
                    continue;
                }

                Trade buyTrade;
                bool gotBuy = SlidingWindowBuys.TryGetValue(buyPrice, out buyTrade);
                long buySize = gotBuy ? buyTrade.Size : 0;

                Trade sellTrade;
                bool gotSell = SlidingWindowSells.TryGetValue(buyPrice - tickSize, out sellTrade);
                long sellSize = gotSell ? sellTrade.Size : 0;

                if (gotSell && buySize >= sellSize * imbalanceFactor)
                    buyImbalance += buySize;
            }

            return buyImbalance;
        }

        internal long GetImbalancedSells(double currentPrice, double tickSize)
        {
            long sellImbalance = 0;

            foreach (double sellPrice in SlidingWindowSells.Keys)
            {
                // If we've blown past "imbalance", the imbalance did not hold up. It does not indicate strength.
                if (currentPrice > sellPrice + (imbalanceInvalidateDistance * tickSize))
                {
                    continue;
                }

                Trade sellTrade;
                bool gotSell = SlidingWindowSells.TryGetValue(sellPrice, out sellTrade);
                long sellSize = gotSell ? sellTrade.Size : 0;

                Trade buyTrade;
                bool gotBuy = SlidingWindowBuys.TryGetValue(sellPrice + tickSize, out buyTrade);
                long buySize = gotBuy ? buyTrade.Size : 0;

                if (gotBuy && sellSize >= buySize * imbalanceFactor)
                    sellImbalance += sellSize;
            }

            return sellImbalance;
        }

        internal BidAskPerc GetBidPerc (double price) {
            BidAskPerc bidAskPerc = null;
            this.BidsPerc.TryGetValue(price, out bidAskPerc);
            return bidAskPerc;
        }

        internal BidAskPerc GetAskPerc (double price) {
            BidAskPerc bidAskPerc = null;
            this.AsksPerc.TryGetValue(price, out bidAskPerc);
            return bidAskPerc;
        }

        internal OFStrength CalculateOrderFlowStrength(OFSCalculationMode mode, double price, double tickSize)
        {
            OFStrength orderFlowStrength = new OFStrength();

            // Short-circuit if there's not enough data
            if ((SlidingWindowBuys.Count + SlidingWindowSells.Count) < minSlidingWindowTrades) {
                return orderFlowStrength;
            }

            long buyImbalance = 0, sellImbalance = 0, totalImbalance = 0, buysInSlidingWindow = 0, sellsInSlidingWindow = 0, totalVolume = 0;

            if (mode == OFSCalculationMode.COMBINED || mode == OFSCalculationMode.IMBALANCE) {
                // Imbalance data
                buyImbalance = GetImbalancedBuys(price, tickSize);
                sellImbalance = GetImbalancedSells(price, tickSize);

                if (buyImbalance + sellImbalance == 0)
                {
                    buyImbalance = sellImbalance = 1;
                }

                totalImbalance = buyImbalance + sellImbalance;
            }

            if (mode == OFSCalculationMode.COMBINED || mode == OFSCalculationMode.BUY_SELL) {
                // Buy/Sell data in sliding window
                buysInSlidingWindow = GetBuysInSlidingWindow();
                sellsInSlidingWindow = GetSellsInSlidingWindow();

                if (buysInSlidingWindow + sellsInSlidingWindow == 0)
                {
                    buysInSlidingWindow = sellsInSlidingWindow = 1;
                }

                totalVolume = sellsInSlidingWindow + buysInSlidingWindow;
            }

            orderFlowStrength.buyStrength = (Convert.ToDouble(buysInSlidingWindow + buyImbalance) / Convert.ToDouble(totalVolume + totalImbalance)) * 100.00;
            orderFlowStrength.sellStrength = (Convert.ToDouble(sellsInSlidingWindow + sellImbalance) / Convert.ToDouble(totalVolume + totalImbalance)) * 100.8;

            return orderFlowStrength;
        }

        internal long GetBuyVolumeAtPrice(double price)
        {
            long volume = 0;
            TotalBuys.TryGetValue(price, out volume);
            return volume;
        }

        internal long GetSellVolumeAtPrice(double price)
        {
            long volume = 0;
            TotalSells.TryGetValue(price, out volume);
            return volume;
        }

        internal long GetAskChange(double price, long currentSize)
        {
            long change = 0;
            long prevSize = 0;
            bool gotPrevAsk = PrevAsk.TryGetValue(price, out prevSize);

            // Replace with current size
            if (currentSize > 0)
            {
                // Clear out old size
                long old;
                PrevAsk.TryRemove(price, out old);

                PrevAsk.TryAdd(price, currentSize);
            }

            if (gotPrevAsk)
            {
                change = currentSize - prevSize;

            }

            return change;
        }

        internal long GetBidChange(double price, long currentSize)
        {
            long change = 0;
            long prevSize = 0;
            bool gotPrevBid = PrevBid.TryGetValue(price, out prevSize);

            // Replace with current size
            if (currentSize > 0)
            {
                // Clear out old size
                long old;
                PrevBid.TryRemove(price, out old);

                PrevBid.TryAdd(price, currentSize);
            }

            if (gotPrevBid)
            {
                change = currentSize - prevSize;

            }

            return change;
        }

        internal Trade GetBuysInSlidingWindow(double price)
        {
            Trade trade = null;
            SlidingWindowBuys.TryGetValue(price, out trade);
            return trade;
        }

        internal Trade GetSellsInSlidingWindow(double price)
        {
            Trade trade = null;
            SlidingWindowSells.TryGetValue(price, out trade);
            return trade;
        }

        internal BidAsk GetBidsInSlidingWindow(double price)
        {
            BidAsk item = null;
            SlidingWindowBids.TryGetValue(price, out item);
            return item;
        }

        internal BidAsk GetAsksInSlidingWindow(double price)
        {
            BidAsk item = null;
            SlidingWindowAsks.TryGetValue(price, out item);
            return item;
        }       

        internal long GetLastBuySize(double price)
        {
            long lastSize = 0;
            LastBuy.TryGetValue(price, out lastSize);
            return lastSize;
        }

        internal long GetLastSellSize(double price)
        {
            long lastSize = 0;
            LastSell.TryGetValue(price, out lastSize);
            return lastSize;
        }

        internal long GetLastBuyPrint(double price)
        {
            long lastSize = 0;
            LastBuyPrint.TryGetValue(price, out lastSize);
            return lastSize;
        }

        internal long GetLastSellPrint(double price)
        {
            long lastSize = 0;
            LastSellPrint.TryGetValue(price, out lastSize);
            return lastSize;
        }  

        internal long GetLastBuyPrintMax(double price)
        {
            long lastSize = 0;
            LastBuyPrintMax.TryGetValue(price, out lastSize);
            return lastSize;
        }

        internal long GetLastSellPrintMax(double price)
        {
            long lastSize = 0;
            LastSellPrintMax.TryGetValue(price, out lastSize);
            return lastSize;
        }               

        internal void RemoveLastBuy(double price)
        {
            long lastSize;
            LastBuy.TryRemove(price, out lastSize);
        }

        internal void RemoveLastSell(double price)
        {
            long lastSize;
            LastSell.TryRemove(price, out lastSize); 
        }
    }
}
