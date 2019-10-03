﻿using System;
using System.Collections.Generic;
using System.Linq;
using Binance.Net;
using Binance.Net.Objects;
using BinanceTrader.Core.DataAccess;
using BinanceTrader.Core.Models;
using CryptoExchange.Net.Authentication;
using Serilog;

namespace BinanceTrader.Core.AutoTrader
{
    public class BinanceAutoTrader : IAutoTrader
    {
        private readonly object _lockObject = new object();
        private readonly CoreConfiguration _config;
        private readonly SymbolPair _symbolPair;
        private readonly IRepository _repo;
        private readonly ILogger _logger;

        private DateTime _lastTradeDate;
        private DateTime _lastLockTime;
        private long _lockedDueToOrderId;
        private bool _isTradingLocked;

        public BinanceAutoTrader(CoreConfiguration config, IRepository repo, ILogger logger)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _repo = repo ?? throw new ArgumentNullException(nameof(repo));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _symbolPair = SymbolPair.Create(_config.FirstSymbol, _config.SecondSymbol);
            CurrentWallet = GetCurrentWallet();
        }

        public EventHandler<ProfitableUserTradedEventArgs> ProfitableUserTradedHandler => HandleEvent;
        public List<SymbolAmountPair> WalletHistory { get; private set; } = new List<SymbolAmountPair>();
        public List<string> AttachedUsersHistory { get; private set; } = new List<string>();
        public BinanceUser AttachedUser { get; private set; }
        public UserProfitReport AttachedUserProfit { get; private set; }
        public SymbolAmountPair CurrentWallet { get; private set; }

        public void DetachAttachedUser()
        {
            AttachedUser = null;
        }

        private SymbolAmountPair GetCurrentWallet()
        {
            using (var client = CreateBinanceClient())
            {
                var price = client.GetPrice(_symbolPair.ToString());
                var accountInfo = client.GetAccountInfo();
                var s1b = accountInfo.Data.Balances
                    .Where(b => b.Asset == _symbolPair.Symbol1)
                    .First();

                var s2b = accountInfo.Data.Balances
                    .Where(b => b.Asset == _symbolPair.Symbol2)
                    .First();

                var s1pcb = s1b.Total * price.Data.Price;
                if (s1pcb > s2b.Total)
                {
                    return SymbolAmountPair.Create(s1b.Asset, s1b.Total);
                }
                else
                {
                    return SymbolAmountPair.Create(s2b.Asset, s2b.Total);
                }
            }
        }

        private void UpdateLockedState()
        {
            if (!_isTradingLocked)
            {
                return;
            }

            using (var client = CreateBinanceClient())
            {
                var order = client.GetOrder(_symbolPair.ToString(), orderId: _lockedDueToOrderId);
                var orderStatus = order.Data.Status;

                if (orderStatus == OrderStatus.Filled ||
                    orderStatus == OrderStatus.Expired ||
                    orderStatus == OrderStatus.Canceled ||
                    orderStatus == OrderStatus.Rejected)
                {
                    _logger.Warning($"Order with Id {_lockedDueToOrderId} had status of order {orderStatus}. The last lock time was {_lastLockTime}. Unlocking the trading.");
                    _isTradingLocked = false;
                }
                else if (DateTime.UtcNow - _lastLockTime > TimeSpan.FromSeconds(_config.MaxTraderLockSeconds))
                {
                    client.CancelOrder(_symbolPair.ToString(), orderId: _lockedDueToOrderId);
                    _isTradingLocked = false;
                }
            }
        }

        private BinanceClient CreateBinanceClient()
        {
            var creds = new ApiCredentials(_config.BinanceApiKey, _config.BinanceApiSecret);
            var options = new BinanceClientOptions() { ApiCredentials = creds };

            return new BinanceClient(options);
        }

        private void HandleEvent(object sender, ProfitableUserTradedEventArgs e)
        {
            lock (_lockObject)
            {
                if (_isTradingLocked)
                {
                    UpdateLockedState();

                    if (_isTradingLocked)
                    {
                        _logger.Information($"Trading is locked. Skipping profitable user with Id: {e.UserId}");
                        return;
                    }
                    else
                    {
                        CurrentWallet = GetCurrentWallet();
                    }
                }

                if (e.Report.CurrencySymbol != _config.TargetCurrencySymbol)
                {
                    _logger.Information("Report was not targeting our currency symbol.");
                    return;
                }

                if (AttachedUser == null || AttachedUser.Identifier == e.UserId)
                {
                    if (AttachedUser == null)
                    {
                        _logger.Information($"Attaching to user with Id: {e.UserId}");
                        AttachedUsersHistory.Add(e.UserId);
                        AttachedUserProfit = e.Report;
                        AttachedUser = _repo.GetUserById(e.UserId);
                    }

                    _logger.Information("Attached user traded. Repeating actions.");

                    var trade = _repo.GetTradeById(e.TradeId);
                    _lastTradeDate = trade.TradeTime;
                    if (AttachedUser.CurrentWallet.Symbol == CurrentWallet.Symbol)
                    {
                        _logger.Information("Currently the trader holds the currency that we already have.");
                        return;
                    }

                    using (var client = CreateBinanceClient())
                    {
                        var orderSide = CurrentWallet.Symbol == _symbolPair.Symbol1 ? OrderSide.Sell : OrderSide.Buy;
                        var feePercentage = GetCurrentFeePercentage(client, orderSide);

                        if (!IsFeeProfitable(e.Report, feePercentage))
                        {
                            _logger.Information($"Attached user average profit per hour: {e.Report.AverageProfitPerHour}");
                            _logger.Information($"Attached user trades per hour: {e.Report.AverageTradesPerHour}");
                            _logger.Warning($"User with Id {e.UserId} was not fee profitable. Detaching them.");
                            DetachAttachedUser();
                            return;
                        }

                        var priceResult = client.GetPrice(_symbolPair.ToString());
                        var price = priceResult.Data.Price;
                        var amountToTrade = CurrentWallet.Symbol == _symbolPair.Symbol1 ? CurrentWallet.Amount : CurrentWallet.Amount / price;

                        _logger.Warning($"Wallet balance is {CurrentWallet.Amount}{CurrentWallet.Symbol}");
                        _logger.Warning($"Selling {CurrentWallet.Amount}{CurrentWallet.Symbol} and buying {AttachedUser.CurrentWallet.Symbol} with price of {price}.");

                        var placeOrderResult = client.PlaceOrder(_symbolPair.ToString(), orderSide, OrderType.Limit, amountToTrade, price: price, timeInForce: TimeInForce.GoodTillCancel);
                        _repo.BlacklistOrderId(placeOrderResult.Data.OrderId);

                        _isTradingLocked = true;
                        _lastLockTime = DateTime.UtcNow;
                        _lockedDueToOrderId = placeOrderResult.Data.OrderId;
                    }
                    _logger.Warning($"Locked account until order with Id {_lockedDueToOrderId} become filled/expired/rejected.");
                }
                else
                {
                    var maxTimeToWaitForAttachedUser = TimeSpan.FromTicks(AttachedUserProfit.AverageTradeThreshold.Ticks * 2);
                    if (DateTime.UtcNow - _lastTradeDate > maxTimeToWaitForAttachedUser ||
                        e.Report.AverageProfitPerHour > AttachedUserProfit.AverageProfitPerHour)
                    {
                        _logger.Information("Detaching current user.");
                        AttachedUser = null;
                        HandleEvent(this, e);
                    }
                }
            }
        }

        private decimal GetCurrentFeePercentage(BinanceClient client, OrderSide orderSide)
        {
            var accountInfo = client.GetAccountInfo();
            var currentFeePercentage = orderSide == OrderSide.Buy ? accountInfo.Data.BuyerCommission : accountInfo.Data.SellerCommission;
            currentFeePercentage += accountInfo.Data.MakerCommission;
            currentFeePercentage /= 10000;

            return currentFeePercentage;
        }

        private bool IsFeeProfitable(UserProfitReport upr, decimal feePercentage)
        {
            return upr.AverageTradesPerHour * (double)feePercentage > upr.AverageProfitPerHour;
        }
    }
}