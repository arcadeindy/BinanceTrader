﻿using System;
using System.IO;
using System.Linq;
using System.Threading;
using BinanceTrader.Core.DataAccess;
using BinanceTrader.Core.Services;
using Castle.Windsor;
using Microsoft.Extensions.Configuration;

namespace BinanceTrader.Core
{
    public class Program
    {
        private static IRepository repo;
        private static UserProcessingService ups;

        public static void Main(string[] args)
        {
            var manualResetEvent = new ManualResetEvent(false);
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);

            var configRoot = builder.Build();
            var config = configRoot.Get<CoreConfiguration>();

            using (var container = new WindsorContainer())
            {
                container.Install(new TraderInstaller(config));
                var dataFiller = container.Resolve<HistoricalDataDownloaderService>();
                var registrar = container.Resolve<ITradeRegistrarService>();
                repo = container.Resolve<IRepository>();
                ups = container.Resolve<UserProcessingService>();

                registrar.RecognizedUserTraded += Registrar_RecognizedUserTraded;

                dataFiller.DownloadToRepositoryAsync().Wait();
                manualResetEvent.WaitOne();
            }
        }

        private static void Registrar_RecognizedUserTraded(object sender, RecognizedUserTradesEventArgs e)
        {
            var user = repo.GetUserById(e.UserId);
            if (user.Wallets.Count < 3)
            {
                return;
            }

            var userProfit = ups.GetUserProfit(user);

            if (userProfit.MinimalTradeThreshold < TimeSpan.FromSeconds(10) || userProfit.ProfitPercentage < 1 || userProfit.IsFullReport == false)
            {
                return;
            }

            Console.WriteLine($"==============User detected with positive profit==============");
            Console.WriteLine($"User ID : {user.Identifier}");
            Console.WriteLine($"User profit: {userProfit.ProfitPercentage}%");
            Console.WriteLine($"User wallets {string.Join("->", user.Wallets.Select(w => $"{w.Balance} {w.Symbol}"))}");
            Console.WriteLine($"Average trade threshold seconds: {userProfit.AverageTradeThreshold.TotalSeconds}");
            Console.WriteLine($"Minimal trade threshold seconds: {userProfit.MinimalTradeThreshold.TotalSeconds}");
            Console.WriteLine($"Success count: {userProfit.SucceededTradesCount}");
            Console.WriteLine($"Failed count: {userProfit.FailedTradesCount}");
            Console.WriteLine($"Start balance: {userProfit.StartBalance}{userProfit.CurrencySymbol}");
            Console.WriteLine($"Ending balance: {userProfit.EndBalance}{userProfit.CurrencySymbol}");
            Console.WriteLine($"==============================================================");

            //Console.WriteLine($"=========Continuous Recognized User Traded========");
            //Console.WriteLine($"User Id: {e.UserId}");
            //var prefix = "****";
            //Console.WriteLine(string.Join("->", user.WalletsHistory.Select(w => $"{w.Balance} {w.Symbol}" )));

            //prefix += "****";
            //foreach (var tradeId in user.TradeIds)
            //{
            //    var trade = repo.GetTradeById(tradeId);
            //    Console.WriteLine($"{prefix} Trade Id: {tradeId}");
            //    Console.WriteLine($"{prefix} Trade symbol pair: {trade.SymbolPair}");
            //    Console.WriteLine($"{prefix} Trade quantity: {trade.Quantity}");
            //    Console.WriteLine($"{prefix} Trade price: {trade.Price}");
            //    Console.WriteLine($"{prefix} Trade q*p: {trade.Price * trade.Quantity}");
            //    Console.WriteLine($"{prefix} Trade time: {trade.TradeTime}");
            //    Console.WriteLine();
            //}

            //Console.WriteLine($"=======================================");
        }
    }
}