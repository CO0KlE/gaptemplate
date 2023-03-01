using System.Net.Http.Headers;
using Binance.Net.Clients;
using Binance.Net.Enums;
using CryptoExchange.Net.Objects;
using CryptoExchange.Net.Sockets;
using JodaSignals;
using Telegram.Bot;

namespace BinanceAlert
{
    internal class Processor
    {
        private readonly BinanceClient binanceClient;
        private readonly BinanceSocketClient socketClient;

        private readonly TelegramBotClient telegramClient;

        private readonly ProcessorFactory factory;

        public Processor(BinanceClient binanceClient, TelegramBotClient telegramClient, FinandyClient client, FinandyBinanceClient clientB)
        {
            this.binanceClient = binanceClient;
            this.telegramClient = telegramClient;

            socketClient = new BinanceSocketClient();
            factory = new ProcessorFactory(telegramClient, client, clientB);
        }

        public async Task Run()
        {
#if RELEASE
            await Task.Delay(10000);
#endif
            await Welcome();

            await Task.WhenAll(CollectMarkets(), CollectHistory());
        }

        private async Task Welcome()
        {
            try
            {
                using var c = new HttpClient();
                using var r = new HttpRequestMessage(HttpMethod.Get, "https://icanhazdadjoke.com/");
                r.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
                using var jj = await c.SendAsync(r);
                var joke = await jj.Content.ReadAsStringAsync();

                await telegramClient.SendTextMessageAsync(
                    chatId: Settings.Default.ChatId,
                    text: $"😏 Bot started.\n<code>{joke}</code>",
                    parseMode: Telegram.Bot.Types.Enums.ParseMode.Html);
            }
            catch { }
        }

        public async Task CollectMarkets(CancellationToken ct = default)
        {
            var error = false;
            var updated = false;
            var spotSubs = new List<CallResult<UpdateSubscription>>();
            var futuresSubs = new List<CallResult<UpdateSubscription>>();

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var spotInfo = (await binanceClient.SpotApi.ExchangeData.GetExchangeInfoAsync())
                        .Data
                        .Symbols
                        .Where(x => x.QuoteAsset == "USDT");

                    foreach (var spot in spotInfo)
                    {
                        updated |= factory.GetProcessor(spot.Name).UpdateSpotInfo(spot.Status);       // získávání hodnot ze spot párů
                    }

                    var futuresInfo = (await binanceClient.UsdFuturesApi.ExchangeData.GetExchangeInfoAsync()).Data.Symbols
                        .Where(x => x.ContractType == ContractType.Perpetual && x.QuoteAsset == "USDT");

                    foreach (var futures in futuresInfo)
                    {
                        updated |= factory.GetProcessor(futures.Pair).UpdateFuturesInfo(futures.Status);     // získávání hodnot z futures párů
                    }

                    for (int i = 0; i < 3; i++) // for je tu proto aby pokud se to posere, tak aby se to zeptalo vícekrát
                    {
                        try
                        {
                            var leverageInfo = (await binanceClient.UsdFuturesApi.Account.GetBracketsAsync()).Data;
                            foreach(var leverage in leverageInfo)
                            {
                                factory.GetProcessor(leverage.Symbol).Leverage = leverage.Brackets.Max(b => b.InitialLeverage);
                            }
                            break;
                        }
                        catch
                        {
                            if (i == 2)
                            {
                                throw;
                            }
                            await Task.Delay(500, ct);
                        }
                    }

                    if (updated) //HM
                    {
                        foreach (var sub in spotSubs)
                        {
                            await sub.Data.CloseAsync();
                        }

                        spotSubs.Clear();

                        foreach (var sub in futuresSubs)
                        {
                            await sub.Data.CloseAsync();
                        }

                        futuresSubs.Clear();

                        var batches = factory
                            .All()
                            .Where(f => f.Value.Trading)
                            .Select(f => f.Key)
                            .Batch(200)
                            .ToList();

                        foreach (var batch in batches)
                        {
                            futuresSubs.Add(await socketClient.UsdFuturesStreams.SubscribeToKlineUpdatesAsync(batch, KlineInterval.FiveMinutes, async e =>
                            {
                                await factory.GetProcessor(e.Data.Symbol).UpdateFuturesPrice(e.Data.Data.ClosePrice);
                            }));

                            spotSubs.Add(await socketClient.SpotStreams.SubscribeToKlineUpdatesAsync(batch, KlineInterval.FiveMinutes, async e =>
                            {
                                await factory.GetProcessor(e.Data.Symbol).UpdateSpotPrice(e.Data.Data.ClosePrice);
                            }));
                        }

                        if (spotSubs.All(x => x.Success) && futuresSubs.All(x => x.Success)) // otherwise resubscribe
                        {
                            updated = false;
                        }
                    }

                    if (error)
                    {
                        error = false;
                        try
                        {
                            await telegramClient.SendTextMessageAsync(chatId: Settings.Default.ChatId, text: $"🎊 Error state was resolved.", cancellationToken: ct);
                        }
                        catch { }
                    }

                    await Task.Delay(TimeSpan.FromMinutes(10), ct);

                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                    try
                    {
                        if (!error)
                        {
                            await telegramClient.SendTextMessageAsync(chatId: Settings.Default.ChatId, text: $"🥲 Market update error: {ex.Message}", cancellationToken: ct);
                            error = true;
                        }
                    }
                    catch { }

                    await Task.Delay(TimeSpan.FromSeconds(1), ct);
                }
            }
        }

        public async Task CollectHistory(CancellationToken ct = default)
        {
            var error = false;
            while(!ct.IsCancellationRequested)
            {
                try
                {
                    foreach (var x in factory.All())
                    {
                        await x.Value.UpdateHistory(ct);
                    }

                    if (error)
                    {
                        error = false;
                        try
                        {
                            await telegramClient.SendTextMessageAsync(chatId: Settings.Default.ChatId, text: $"🎊 Error state was resolved.", cancellationToken: ct);
                        }
                        catch { }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                    try
                    {
                        if (!error)
                        {
                            await telegramClient.SendTextMessageAsync(chatId: Settings.Default.ChatId, text: $"🥲 History collection error: {ex.Message}", cancellationToken: ct);
                            error = true;
                        }
                    }
                    catch { }
                }

                await Task.Delay(TimeSpan.FromMinutes(1), ct);
            }
        }
    }
}