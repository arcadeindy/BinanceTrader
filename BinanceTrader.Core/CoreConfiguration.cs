﻿namespace BinanceTrader.Core
{
    public class CoreConfiguration
    {
        public string BinanceApiKey { get; set; }

        public string BinanceApiSecret { get; set; }

        public string FirstSymbol { get; set; }

        public string SecondSymbol { get; set; }

        public string TargetCurrencySymbol { get; set; }

        public bool EnableAutoTrade { get; set; }

        public double MaximumAllowedTradeSyncSeconds { get; set; }

        public double MaximumTradeFeePercentage { get; set; }

        public int MemoryInSeconds { get; set; }

        public int MinimalTraderActivityThresholdSeconds { get; set; }

        public int MinimalTraderTradesCount { get; set; }

        public double MinimalTraderProfitPercentage { get; set; }
    }
}