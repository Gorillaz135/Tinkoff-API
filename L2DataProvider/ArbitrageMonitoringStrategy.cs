﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CoreData;
using CoreData.Interfaces;
using CoreData.Models;
using CoreData.Settings;
using CoreNgine.Infra;
using CoreNgine.Interfaces;
using CoreNgine.Models;
using CoreNgine.Shared;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types.Enums;
using Tinkoff.Trading.OpenApi.Legacy.Models;

namespace CoreNgine.Strategies
{
    public struct ArbitrageEventData
    {
        public string Ticker { get; set; }
        public decimal BestBidSpb { get; set; }
        public decimal BestAskSpb { get; set; }
        public DateTimeOffset EventTime { get; set; }
    }

    public class ArbitrageMonitoringStrategy : IHandler<IStockModel>, IHandler<INgineSettings>
    {
        public INgineSettings Settings { get; }
        public IMainModel MainModel { get; }
        public StocksManager StocksManager { get; }
        public ILogger<ArbitrageMonitoringStrategy> Logger { get; }
        public IUSADataManager DataManager { get; }

        private readonly DateTime _strategyLoaded = DateTime.Now;

        private ConcurrentDictionary<string, ArbitrageEventData> _arbitrageEvents = new ConcurrentDictionary<string, ArbitrageEventData>();

        private long _chatIdLong, _chatIdShort;

        public TimeSpan Elapsed => DateTime.Now.Subtract(_strategyLoaded);

        public ArbitrageMonitoringStrategy(ISettingsProvider settingsProvider, IMainModel mainModel, StocksManager stocksManager, 
            IEventAggregator2 eventAggregator, ILogger<ArbitrageMonitoringStrategy> logger, IUSADataManager usaDataManager)
        {
            StocksManager = stocksManager;
            Settings = settingsProvider.Settings;
            SetChatId(Settings);
            MainModel = mainModel;
            DataManager = usaDataManager;
            Logger = logger;
            eventAggregator.SubscribeOnPublishedThread(this);
        }

        private void SetChatId(INgineSettings settings)
        {
            if (long.TryParse(settings.TgArbitrageLongUSAChatId, out var result))
                _chatIdLong = result;
            else
                _chatIdLong = 0;

            if (long.TryParse(settings.TgArbitrageShortUSAChatId, out result))
                _chatIdShort = result;
            else
                _chatIdShort = 0;
        }

        public bool IsSendToTelegramEnabled =>
            Settings.IsTelegramEnabled && Elapsed.TotalSeconds > 10;

        public void PerformArbitrageCheck(IStockModel stock, CancellationToken cancellationToken)
        {
            if (stock.LastUpdateUSA == null || Math.Abs(stock.LastUpdateOrderbook.Subtract(stock.LastUpdateUSA.Value).TotalMinutes) > 5)
                return;

            if (_arbitrageEvents.ContainsKey(stock.Ticker) 
                && _arbitrageEvents[stock.Ticker] is ArbitrageEventData arbitrageEvent 
                && (arbitrageEvent.EventTime.Elapsed().TotalMinutes < 1 
                || (arbitrageEvent.BestAskSpb == stock.BestAskSpb && arbitrageEvent.BestBidSpb == stock.BestBidSpb)))
                return;

            var message = new StringBuilder();
            decimal diff;
            string line;
            var quote = DataManager.QuoteDatas[stock.Ticker];
            OrderbookModel spbOrders = null;
            bool bLong = false, bShort = false;

            if (stock.USBidRUAskDiff > 0.01m)
            {
                bLong = true;
                spbOrders = spbOrders ?? StocksManager.OrderbookInfoSpb[stock.Ticker];
                diff = stock.BidUSA - stock.BestAskSpb;

                message.AppendLine($"*{stock.Ticker}* - {stock.Name}");
                message.AppendLine($"✅ *{stock.USBidRUAskDiff.FormatPercent()}* ({diff.FormatPrice(stock.Currency)})");
                message.AppendLine($"SPB *B {spbOrders.Bids[0][0]}* ({spbOrders.Bids[0][1]}) *A {spbOrders.Asks[0][0]}* ({spbOrders.Asks[0][1]})");
                message.AppendLine($"USA *B {quote.Bid}* ({quote.BidSize}) *A {quote.Ask}* ({quote.AskSize})");

                line =
                    $"SPB ASK *{stock.BestAskSpb.FormatPrice(stock.Currency)}* USA BID *{stock.BidUSA.FormatPrice(stock.Currency)}* Diff {diff.FormatPrice(stock.Currency)} ({stock.USBidRUAskDiff.FormatPercent()})";
                MainModel.AddMessage(MessageKind.ArbitrageLong, stock.Ticker, DateTime.Now, diff, quote.BidSize, line);
            }

            if (stock.RUBidUSAskDiff > 0.01m)
            {
                bShort = true;
                spbOrders = spbOrders ?? StocksManager.OrderbookInfoSpb[stock.Ticker];
                diff = stock.BestBidSpb - stock.AskUSA;

                message.AppendLine($"*{stock.Ticker}* - {stock.Name}");
                message.AppendLine($"⛔️ *{stock.RUBidUSAskDiff.FormatPercent()}* ({diff.FormatPrice(stock.Currency)})");
                message.AppendLine($"SPB *B {spbOrders.Bids[0][0]}* ({spbOrders.Bids[0][1]}) *A {spbOrders.Asks[0][0]}* ({spbOrders.Asks[0][1]})");
                message.AppendLine($"USA *B {quote.Bid}* ({quote.BidSize}) *A {quote.Ask}* ({quote.AskSize})");

                line =
                    $"USA ASK *{stock.AskUSA.FormatPrice(stock.Currency)}* SPB BID *{stock.BestBidSpb.FormatPrice(stock.Currency)}* Diff {diff.FormatPrice(stock.Currency)} ({stock.RUBidUSAskDiff.FormatPercent()})";
                if (stock.CanBeShorted)
                    MainModel.AddMessage(MessageKind.ArbitrageShort, stock.Ticker, DateTime.Now, diff, quote.AskSize, line);
            }

            if (message.Length > 0)
            {
                _arbitrageEvents[stock.Ticker] = new ArbitrageEventData() {
                    EventTime = DateTime.Now,
                    Ticker = stock.Ticker,
                    BestBidSpb = stock.BestBidSpb,
                    BestAskSpb = stock.BestAskSpb
                };

                message.AppendLine("`");
                int i = 0;
                foreach (var spbOrdersBid in spbOrders.Bids)
                {
                    message.Append($"\tSPB BID {spbOrdersBid[0].ToString().PadLeft(2).PadRight(6)} Size {spbOrdersBid[1].ToString().PadLeft(2).PadRight(5)}");
                    if (i < spbOrders.Asks.Count)
                    {
                        var ask = spbOrders.Asks[i++];
                        message.Append($" Ask {ask[0].ToString().PadRight(6)} Size {ask[1]}");
                    }
                    message.AppendLine();
                }
                message.Append("`");

                var text = message.ToString();
                if (_chatIdLong != 0 && bLong)
                    StocksManager.Telegram.PostMessage(new TelegramMessage(stock.Ticker, text, _chatIdLong)
                    {
                        AddTickerImage = false,
                        MessageMode = ParseMode.MarkdownV2
                    });

                if (_chatIdShort != 0 && bShort && stock.CanBeShorted)
                    StocksManager.Telegram.PostMessage(new TelegramMessage(stock.Ticker, text, _chatIdShort)
                    {
                        AddTickerImage = false,
                        MessageMode = ParseMode.MarkdownV2
                    });
            }
        }
            
        public Task HandleAsync(IStockModel message, CancellationToken cancellationToken)
        {
            if (Settings.USAQuotesEnabled)
                PerformArbitrageCheck(message, cancellationToken);

            return Task.CompletedTask;
        }

        public Task HandleAsync(INgineSettings message, CancellationToken cancellationToken)
        {
            SetChatId(message);
            return Task.CompletedTask;
        }
    }
}
