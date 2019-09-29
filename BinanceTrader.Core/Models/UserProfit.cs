﻿using System;

namespace BinanceTrader.Core.Models
{
    public class UserProfit
    {
        public string CurrencySymbol { get; set; }

        public double ProfitPercentage { get; set; }

        public int TradesCount { get; set; }

        public int SucceededTradesCount { get; set; }

        public int FailedTradesCount { get; set; }

        public TimeSpan AverageTradeThreshold { get; set; }

        public decimal StartBalance { get; set; }

        public decimal EndBalance { get; set; }
    }
}