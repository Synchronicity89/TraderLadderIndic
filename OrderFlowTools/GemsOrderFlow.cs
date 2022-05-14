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
using NinjaTrader.Gui.SuperDom;

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
        public BidAsk()
        {
            this.Size = 0;
            this.Time = new DateTime();
        }
        public BidAsk(double size, DateTime time)
        {
            this.Size = size;
            this.Time = time;
        }

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
        BUY_SELL,
        LEVEL2
    }

    class GemsOrderFlow
    {

        private bool DEBUG = true;

        protected ITradeClassifier tradeClassifier;

        private ConcurrentDictionary<double, Trade> SlidingWindowBuys;
        private ConcurrentDictionary<double, Trade> SlidingWindowSells;
        private ConcurrentDictionary<double, long> TotalBuys;
        private ConcurrentDictionary<double, long> TotalSells;

        private ConcurrentDictionary<double, long> LastBuy;
        private ConcurrentDictionary<double, long> LastSell;
        private ConcurrentDictionary<double, long> LastBuyPrint;
        private ConcurrentDictionary<double, long> LastSellPrint;
        private ConcurrentDictionary<double, long> LastBuyPrintMax;
        private ConcurrentDictionary<double, long> LastSellPrintMax;


        private ConcurrentDictionary<double, BidAsk> CurrBid;
        private ConcurrentDictionary<double, BidAsk> CurrAsk;
        private ConcurrentDictionary<double, BidAsk> PrevBid;
        private ConcurrentDictionary<double, BidAsk> PrevAsk;
        private ConcurrentDictionary<double, long> BidChange;
        private ConcurrentDictionary<double, long> AskChange;
        private ConcurrentDictionary<double, BidAsk> CumulativeBids;
        private ConcurrentDictionary<double, BidAsk> CumulativeAsks;
        private ConcurrentDictionary<double, BidAskPerc> BidsPerc;
        private ConcurrentDictionary<double, BidAskPerc> AsksPerc;

        private double imbalanceFactor;
        private long imbalanceInvalidateDistance;
        private const int minSlidingWindowTrades = 1;

        // To support Print
        private Indicator ind;

        public GemsOrderFlow(ITradeClassifier tradeClassifier, double imbalanceFactor)
        {
            ind = new Indicator();

            this.tradeClassifier = tradeClassifier;

            this.imbalanceFactor = imbalanceFactor;
            this.imbalanceInvalidateDistance = 10;

            SlidingWindowBuys = new ConcurrentDictionary<double, Trade>();
            SlidingWindowSells = new ConcurrentDictionary<double, Trade>();
            TotalBuys = new ConcurrentDictionary<double, long>();
            TotalSells = new ConcurrentDictionary<double, long>();

            LastBuy = new ConcurrentDictionary<double, long>();
            LastSell = new ConcurrentDictionary<double, long>();
            LastBuyPrint = new ConcurrentDictionary<double, long>();
            LastSellPrint = new ConcurrentDictionary<double, long>();
            LastBuyPrintMax = new ConcurrentDictionary<double, long>();
            LastSellPrintMax = new ConcurrentDictionary<double, long>();

            CurrAsk = new ConcurrentDictionary<double, BidAsk>();
            CurrBid = new ConcurrentDictionary<double, BidAsk>();
            PrevAsk = new ConcurrentDictionary<double, BidAsk>();
            PrevBid = new ConcurrentDictionary<double, BidAsk>();
            AskChange = new ConcurrentDictionary<double, long>();
            BidChange = new ConcurrentDictionary<double, long>();
            CumulativeBids = new ConcurrentDictionary<double, BidAsk>();
            CumulativeAsks = new ConcurrentDictionary<double, BidAsk>();
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
            CurrBid.Clear();
            CurrAsk.Clear();
            BidChange.Clear();
            AskChange.Clear();
            BidsPerc.Clear();
            AsksPerc.Clear();
        }

        internal void ClearSlidingWindow()
        {
            SlidingWindowBuys.Clear();
            SlidingWindowSells.Clear();

            CumulativeBids.Clear();
            CumulativeAsks.Clear();

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

        internal void SetBidLadder(List<LadderRow> newBidLadder)
        {
            try
            {
                PrevBid = new ConcurrentDictionary<double, BidAsk>(CurrBid);
                CurrBid.Clear();
                if (newBidLadder != null)
                {
                    foreach (LadderRow row in newBidLadder)
                    {
                        BidAsk entry = new BidAsk(row.Volume, row.Time);
                        if (CurrBid != null) CurrBid.TryAdd(row.Price, entry);
                        else Print("SetBidLadder: WTH?? This is not supposed to happen.");
                    }
                }
            }
            catch (Exception ex)
            {
                Print(ex.ToString());
            }
        }

        internal void SetAskLadder(List<LadderRow> newAskLadder)
        {
            try
            {
                PrevAsk = new ConcurrentDictionary<double, BidAsk>(CurrAsk);
                CurrAsk.Clear();
                if (newAskLadder != null)
                {
                    foreach (LadderRow row in newAskLadder)
                    {
                        BidAsk entry = new BidAsk(row.Volume, row.Time);
                        if (CurrAsk != null) CurrAsk.TryAdd(row.Price, entry);
                        else Print("SetAskLadder: WTH?? This is not supposed to happen.");
                    }
                }
            }
            catch (Exception ex)
            {
                Print(ex.ToString());
            }
        }

        internal BidAsk AddOrUpdateBid(double price, long size, DateTime time)
        {
            // Copy current value into prev
            BidAsk currBidAsk = null;
            if (CurrBid.TryGetValue(price, out currBidAsk))
            {
                PrevBid.AddOrUpdate(price, currBidAsk, (key, existing) => currBidAsk);
            }

            // Change in bid size
            long change = Convert.ToInt64(size - (currBidAsk == null ? 0 : currBidAsk.Size));
            BidChange.AddOrUpdate(price, change, (key, value) => change);

            // Calculate cumulative change
            BidAsk currCumBid;
            if (CumulativeBids.TryGetValue(price, out currCumBid))
            {
                double prevSize = currCumBid.Size;
                CumulativeBids.TryUpdate(price, new BidAsk(prevSize + change, time), currCumBid);
            }

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

            // Calculate cumulative change
            BidAsk currCumAsk;
            if (CumulativeBids.TryGetValue(price, out currCumAsk))
            {
                double prevSize = currCumAsk.Size;
                CumulativeBids.TryUpdate(price, new BidAsk(prevSize + change, time), currCumAsk);
            }

            // Add or replace current entry
            BidAsk newBidAsk = new BidAsk(size, time);
            return CurrAsk.AddOrUpdate(price, newBidAsk, (key, existing) => newBidAsk);
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
                    if (volume > lastMax)
                    {
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
                    if (volume > lastMax)
                    {
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
        * Gets total buy (large) volume in the sliding window
        */
        internal long GetTotalLargeBuysInSlidingWindow()
        {
            long total = 0;
            foreach (long size in LastBuyPrintMax.Values)
            {
                total += size;
            }
            return total;
        }

        /*
        * Gets total sell (large) volume in the sliding window
        */
        internal long GetTotalLargeSellsInSlidingWindow()
        {
            long total = 0;
            foreach (long size in LastSellPrintMax.Values)
            {
                total += size;
            }
            return total;
        }

        /*
        * Gets sum of current buy prints in the sliding window
        */
        internal long GetTotalBuyPrintsInSlidingWindow()
        {
            long total = 0;
            foreach (long size in LastBuyPrint.Values)
            {
                total += size;
            }
            return total;
        }

        /*
        * Gets sum of current sell prints in the sliding window
        */
        internal long GetTotalSellPrintsInSlidingWindow()
        {
            long total = 0;
            foreach (long size in LastSellPrint.Values)
            {
                total += size;
            }
            return total;
        }

        internal double GetHighestBuyPriceInSlidingWindow()
        {
            IOrderedEnumerable<double> prices = SlidingWindowBuys.Keys.OrderByDescending(i => ((float)i));
            return prices.FirstOrDefault();
        }

        internal double GetLowestSellPriceInSlidingWindow()
        {
            IOrderedEnumerable<double> prices = SlidingWindowSells.Keys.OrderByDescending(i => ((float)i));
            return prices.LastOrDefault();

        }

        internal double GetSessionHigh()
        {
            IOrderedEnumerable<double> prices = TotalBuys.Keys.OrderByDescending(i => ((float)i));
            return prices.FirstOrDefault();
        }

        internal double GetSessionLow()
        {
            IOrderedEnumerable<double> prices = TotalSells.Keys.OrderByDescending(i => ((float)i));
            return prices.LastOrDefault();

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
         * Clear out Cumulative Bid/Asks if the cumulative entries 
         * fall outside of a sliding time window (seconds), 
         * thus preserving only the latest cumulative bid/ask based on the time window.
         */
        internal void ClearCumulativeBidAsksOutsideSlidingWindow(DateTime time, int TradeSlidingWindowSeconds, int maxBidAskRows, double currentBidPrice, double currentAskPrice, double upperCutOff, double lowerCutOff)
        {
            try
            {
                // Remove cumulative bids 
                foreach (double price in CumulativeBids.Keys)
                {
                    BidAsk bidAsk;
                    bool gotItem = CumulativeBids.TryGetValue(price, out bidAsk);
                    if (gotItem)
                    {
                        TimeSpan diff = time - bidAsk.Time;
                        if (diff.TotalSeconds > TradeSlidingWindowSeconds || price > currentBidPrice || price < lowerCutOff)
                        {
                            CumulativeBids.TryRemove(price, out bidAsk);
                        }
                    }
                }

                // Remove cumulative asks 
                foreach (double price in CumulativeAsks.Keys)
                {
                    BidAsk bidAsk;
                    bool gotItem = CumulativeAsks.TryGetValue(price, out bidAsk);
                    if (gotItem)
                    {
                        TimeSpan diff = time - bidAsk.Time;
                        if (diff.TotalSeconds > TradeSlidingWindowSeconds || price < currentAskPrice || price > upperCutOff)
                        {
                            BidAsk removeThis;
                            CumulativeAsks.TryRemove(price, out removeThis);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Print("An error occurred. Please report this bug. Thanks for your help." + e.ToString());
            }
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

        internal BidAskPerc GetBidPerc(double price)
        {
            BidAskPerc bidAskPerc = null;
            this.BidsPerc.TryGetValue(price, out bidAskPerc);
            return bidAskPerc;
        }

        internal BidAskPerc GetAskPerc(double price)
        {
            BidAskPerc bidAskPerc = null;
            this.AsksPerc.TryGetValue(price, out bidAskPerc);
            return bidAskPerc;
        }

        internal OFStrength CalculateOrderFlowStrength(OFSCalculationMode mode, double price, double tickSize)
        {
            OFStrength orderFlowStrength = new OFStrength();

            // Short-circuit if there's not enough data
            if ((SlidingWindowBuys.Count + SlidingWindowSells.Count) < minSlidingWindowTrades)
            {
                return orderFlowStrength;
            }

            long buyImbalance = 0, sellImbalance = 0, totalImbalance = 0, buysInSlidingWindow = 0, sellsInSlidingWindow = 0, totalVolume = 0;

            if (mode == OFSCalculationMode.COMBINED || mode == OFSCalculationMode.IMBALANCE || mode == OFSCalculationMode.LEVEL2)
            {
                // Imbalance data
                buyImbalance = GetImbalancedBuys(price, tickSize);
                sellImbalance = GetImbalancedSells(price, tickSize);

                if (buyImbalance + sellImbalance == 0)
                {
                    buyImbalance = sellImbalance = 1;
                }

                totalImbalance = buyImbalance + sellImbalance;
            }

            if (mode == OFSCalculationMode.COMBINED || mode == OFSCalculationMode.BUY_SELL)
            {
                // Buy/Sell data in sliding window
                buysInSlidingWindow = GetBuysInSlidingWindow();
                sellsInSlidingWindow = GetSellsInSlidingWindow();

                if (buysInSlidingWindow + sellsInSlidingWindow == 0)
                {
                    buysInSlidingWindow = sellsInSlidingWindow = 1;
                }

                totalVolume = sellsInSlidingWindow + buysInSlidingWindow;
            }

            if (mode == OFSCalculationMode.LEVEL2)
            {
                double cumulativeBidChange = GetTotalCumulativeBidChanges();
                double cumulativeAskChange = GetTotalCumulativeBidChanges();
                double totalChanges = cumulativeAskChange + cumulativeBidChange;

                bool allPositiveBidChanges = isAllBidChangesPositive();
                bool allPositiveAskChanges = isAllAskChangesPositive();

                bool allNegativeBidChanges = isAllBidChangesNegative();
                bool allNegativeAskChanges = isAllAskChangesNegative();

                orderFlowStrength.buyStrength = 0;
                orderFlowStrength.sellStrength = 0;

                if ((allPositiveAskChanges || allNegativeBidChanges) && sellImbalance > buyImbalance)
                {
                    Print("L2 Sell");
                    orderFlowStrength.sellStrength = 100;
                }
                else if ((allPositiveBidChanges || allNegativeAskChanges) && buyImbalance > sellImbalance)
                {
                    Print("L2 Buy");
                    orderFlowStrength.buyStrength = 100;
                }
            }
            else
            {
                orderFlowStrength.buyStrength = (Convert.ToDouble(buysInSlidingWindow + buyImbalance) / Convert.ToDouble(totalVolume + totalImbalance)) * 100.00;
                orderFlowStrength.sellStrength = (Convert.ToDouble(sellsInSlidingWindow + sellImbalance) / Convert.ToDouble(totalVolume + totalImbalance)) * 100.0;
            }

            return orderFlowStrength;
        }

        internal long GetBuyVolumeAtPrice(double price)
        {
            long volume = 0;
            TotalBuys.TryGetValue(price, out volume);
            return volume;
        }

        internal void CalculateCumulativeBidAsk()
        {
            try
            {
                AsksPerc.Clear();
                BidsPerc.Clear();

                // Calculate Total Bid/Ask Sizes (for histogram drawing)
                double bidAskTotalVolume = CalculateBidAskTotalVolume();

                foreach (double price in CurrAsk.Keys)
                {
                    BidAsk currentAsk;
                    if (!CurrAsk.TryGetValue(price, out currentAsk)) continue;

                    // Calculate cumulative ask
                    long change = GetAskChange(price);
                    if (change != 0)
                    {
                        // Get previous cumulative ask, if it exists
                        BidAsk existing;
                        CumulativeAsks.TryGetValue(price, out existing);

                        // Replace with new cumulative value
                        BidAsk newBidAsk = new BidAsk(change + (existing == null ? 0 : existing.Size), currentAsk.Time);
                        CumulativeAsks.AddOrUpdate(price, newBidAsk, (key, oldBidAsk) => newBidAsk);
                    }

                    // Calculate percentage of current ask volume relative to total bid/ask volume
                    BidAskPerc perc = new BidAskPerc();
                    perc.Size = currentAsk.Size;
                    perc.Perc = currentAsk.Size / bidAskTotalVolume;
                    AsksPerc.AddOrUpdate(price, perc, (key, existing) => existing = perc);
                }

                foreach (double price in CurrBid.Keys)
                {
                    BidAsk currentBid;
                    if (!CurrBid.TryGetValue(price, out currentBid)) continue;

                    // Calculate cumulative bid
                    long change = GetBidChange(price);
                    if (change != 0)
                    {
                        // Get previous cumulative bid, if it exists
                        BidAsk existing;
                        CumulativeBids.TryGetValue(price, out existing);

                        // Replace with new cumulative value
                        BidAsk newBidAsk = new BidAsk(change + (existing == null ? 0 : existing.Size), currentBid.Time);
                        CumulativeBids.AddOrUpdate(price, newBidAsk, (key, oldBidAsk) => newBidAsk);
                    }

                    // Calculate percentage of current bid volume relative to total bid/ask volume
                    BidAskPerc perc = new BidAskPerc();
                    perc.Size = currentBid.Size;
                    perc.Perc = currentBid.Size / bidAskTotalVolume;
                    BidsPerc.AddOrUpdate(price, perc, (key, existing) => existing = perc);
                }
            }
            catch (Exception e)
            {
                Print("An error occurred. Please report this bug. Thanks for your help." + e.ToString());
            }
        }

        private double CalculateBidAskTotalVolume()
        {
            double maxBidAsk = 0;
            foreach (BidAsk bidAsk in CurrAsk.Values)
            {
                maxBidAsk = bidAsk.Size > maxBidAsk ? bidAsk.Size : maxBidAsk;
            }
            foreach (BidAsk bidAsk in CurrBid.Values)
            {
                maxBidAsk = bidAsk.Size > maxBidAsk ? bidAsk.Size : maxBidAsk;
            }

            return maxBidAsk;
        }

        internal long GetSellVolumeAtPrice(double price)
        {
            long volume = 0;
            TotalSells.TryGetValue(price, out volume);
            return volume;
        }

        internal long GetAskChange(double price)
        {

            long currentSize = GetAsk(price);
            long prevSize = GetPrevAsk(price);
            return (prevSize > 0 && currentSize > 0) ? (currentSize - prevSize) : 0;
        }

        internal long GetBidChange(double price)
        {
            long currentSize = GetBid(price);
            long prevSize = GetPrevBid(price);
            return (prevSize > 0 && currentSize > 0) ? (currentSize - prevSize) : 0;
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

        internal BidAsk GetCumulativeBid(double price)
        {
            BidAsk item = null;
            CumulativeBids.TryGetValue(price, out item);
            return item;
        }

        internal BidAsk GetCumulativeAsk(double price)
        {
            BidAsk item = null;
            CumulativeAsks.TryGetValue(price, out item);
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


        /*
        * Gets sum of cumulative bid changes in the sliding window
        */
        internal long GetTotalCumulativeBidChanges()
        {
            double total = 0;
            foreach (BidAsk bidAsk in CumulativeBids.Values)
            {
                total += bidAsk.Size;
            }
            return Convert.ToInt64(total);
        }

        /*
        * Gets sum of cumulative ask changes in the sliding window
        */
        internal long GetTotalCumulativeAskChanges()
        {
            double total = 0;
            foreach (BidAsk bidAsk in CumulativeAsks.Values)
            {
                total += bidAsk.Size;
            }
            return Convert.ToInt64(total);
        }

        /*
         * Checks if all cumulative bid changes in the sliding window are positive
         */
        internal bool isAllBidChangesPositive()
        {
            bool isPositive = true;
            foreach (BidAsk bidAsk in CumulativeBids.Values)
            {
                isPositive &= bidAsk.Size >= 0;
            }
            return isPositive;
        }

        /*
         * Checks if all cumulative ask changes in the sliding window are positive
         */
        internal bool isAllAskChangesPositive()
        {
            bool isPositive = true;
            foreach (BidAsk bidAsk in CumulativeAsks.Values)
            {
                isPositive &= bidAsk.Size >= 0;
            }
            return isPositive;
        }

        /*
         * Checks if all cumulative bid changes in the sliding window are negative
         */
        internal bool isAllBidChangesNegative()
        {
            bool isNegative = true;
            foreach (BidAsk bidAsk in CumulativeBids.Values)
            {
                isNegative &= bidAsk.Size <= 0;
            }
            return isNegative;
        }

        /*
         * Checks if all cumulative ask changes in the sliding window are negative
         */
        internal bool isAllAskChangesNegative()
        {
            bool isNegative = true;
            foreach (BidAsk bidAsk in CumulativeAsks.Values)
            {
                isNegative &= bidAsk.Size <= 0;
            }
            return isNegative;
        }

        internal long GetBid(double price)
        {
            try
            {
                BidAsk entry;
                if (CurrBid.TryGetValue(price, out entry))
                {
                    return Convert.ToInt64(entry.Size);
                }
            }
            catch (Exception e)
            {
                Print("An error occurred. Please report this bug. Thanks for your help." + e.ToString());
            }
            return 0;
        }

        internal long GetAsk(double price)
        {
            try
            {
                BidAsk entry;
                if (CurrAsk.TryGetValue(price, out entry))
                {
                    return Convert.ToInt64(entry.Size);
                }
            }
            catch (Exception e)
            {
                Print("An error occurred. Please report this bug. Thanks for your help." + e.ToString());
            }
            return 0;
        }

        internal long GetPrevBid(double price)
        {
            try
            {
                BidAsk entry;
                if (PrevBid.TryGetValue(price, out entry))
                {
                    return Convert.ToInt64(entry.Size);
                }
            }
            catch (Exception e)
            {
                Print("An error occurred. Please report this bug. Thanks for your help." + e.ToString());
            }
            return 0;
        }

        internal long GetPrevAsk(double price)
        {
            try
            {
                BidAsk entry;
                if (PrevAsk.TryGetValue(price, out entry))
                {
                    return Convert.ToInt64(entry.Size);
                }
            }
            catch (Exception e)
            {
                Print("An error occurred. Please report this bug. Thanks for your help." + e.ToString());
            }
            return 0;
        }
    }
}
