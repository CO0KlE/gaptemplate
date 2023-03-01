using System.Text;
using Binance.Net.Enums;
using JodaSignals;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace BinanceAlert
{
    internal class SymbolProcessor
    {
        private readonly SemaphoreSlim semaphore = new(1);
        private DateTime mutedInfo = DateTime.MinValue;
        private DateTime mutedSignal = DateTime.MinValue;
        private HistoryPair history = null;
        private SymbolStatus spotInfo = SymbolStatus.Close;
        private SymbolStatus futuresInfo = SymbolStatus.Break;
        private int leverageInfo = Settings.Default.Leverage;
        private PricePair price = new (null, null);
        private DateTime spotUpdate = DateTime.MinValue;
        private DateTime futuresUpdate = DateTime.MinValue;

        private readonly string symbol;

        private readonly TelegramBotClient telegramClient;

        private readonly FinandyClient client;
        private readonly FinandyBinanceClient clientB;

        public SymbolProcessor(string symbol, TelegramBotClient telegramClient, FinandyClient client, FinandyBinanceClient clientB)
        {
            this.symbol = symbol;
            this.telegramClient = telegramClient;
            this.client = client;
            this.clientB = clientB;            
        }

        public bool Trading => spotInfo == SymbolStatus.Trading && futuresInfo == SymbolStatus.Trading;

        public bool UpdateSpotInfo(SymbolStatus status) //získává hodnoty ze spot párů - změna statusu
        {
            var ret = status != spotInfo;
            spotInfo = status;
            return ret;
        }

        public bool UpdateFuturesInfo(SymbolStatus status) //získává hodnoty z futures párů - změna statusu
        {
            var ret = status != futuresInfo;
            futuresInfo = status;
            return ret;
        }

        public int Leverage
        {
            set
            {
                leverageInfo = Math.Min(value, Settings.Default.Leverage);
            }
        } //získává lvg informace

        private async Task Locked(Func<Task> taskFactory, CancellationToken ct = default)
        {
            await semaphore.WaitAsync(ct);
            try
            {
                await taskFactory();
            }
            finally
            {
                semaphore.Release();
            }
        }

        public async Task UpdateHistory(CancellationToken ct = default) //updatuje historii čistě kvůli spot pump contribution calculation - možná bude potřeba upravit
        {
            await Locked(() =>
            {
                if (!price.Spot.HasValue || !price.Futures.HasValue)
                    return Task.CompletedTask;

                history = new HistoryPair(
                    new HistoryRecord(history?.Spot?.Newer, price.Spot.Value),
                    new HistoryRecord(history?.Futures?.Newer, price.Futures.Value)
                );

                return Task.CompletedTask;
            }, ct);
        }

        public async Task UpdateSpotPrice(decimal newPrice, CancellationToken ct = default) //updatování spotu MAIN
        {
            await Locked(async () =>
            {
                price = price with { Spot = newPrice };
                spotUpdate = DateTime.Now;
                await Process(ct);
            }, ct);
        }

        public async Task UpdateFuturesPrice(decimal newPrice, CancellationToken ct = default) //updatování futures MAIN
        {
            await Locked(async () =>
            {
                price = price with { Futures = newPrice };
                futuresUpdate = DateTime.Now;
                await Process(ct);
            }, ct);
        }

        private async Task Process(CancellationToken ct = default)  //tady jsem skoncil
        {
            if (!Trading) //pokud nejde trejdit tak OUT ASI
                return;

            if (!price.Spot.HasValue || !price.Futures.HasValue) //pokud něco z toho neukazuje hodnotu, tak OUT (pump contribution)
                return;

            if (Math.Abs((spotUpdate - futuresUpdate).TotalMilliseconds) > 500) //pokud rozdíl mezi záznamy MAIN je větší než 500ms, tak OUT
                return;

            var spotPrice = price.Spot.Value; //poslední hodnoty z pu.co ASI
            var futuresPrice = price.Futures.Value; //poslední hodnoty z pu.co ASI

            var diff = DiffAbs(futuresPrice, spotPrice); //abs rozdíl abs(a - b) / b * 100;
#if DEBUG //čistě pro debug potřebný rozdíl, aby to něco psalo do konzole
            if (diff > 1.2M)
            {
                Console.WriteLine($"{symbol}: {diff:F2}%"); //tohle jediný chápu úplně nahned :D 
            }
#endif

            if (diff > Settings.Default.PriceDiff) //podmínka rozdílu
            {
                var infoMuted = mutedInfo > DateTime.Now; //var ohledně čekingu, jestli je info ještě muted, buď ze stanovené hodnoty nahoře nebo z minulé
                var signalMuted = mutedSignal > DateTime.Now; //var ohledně čekingu, jestli je signal ještě muted, buď ze stanovené hodnoty nahoře nebo z minulé
                if (infoMuted && signalMuted) return; //pokud je oboje muted, tak OUT

                var higher = spotPrice > futuresPrice; //parametr, co je vyšší (t=spot,f=futures)

                var signal = new Signal(symbol, !higher, leverageInfo); //PŘEDVYTVOŘENÍ SIGNÁLU????
                var valid = true; //nějaký parametr související s aktivací signálu

                var details = new StringBuilder(); //vytvoření detailů, kam se budou zapisovat věci do té zprávy o signálu

                if (history != null && history.Spot.Older.HasValue && history.Futures.Older.HasValue) //pu.co checkování, jestli historie existuje
                {
                    var ds = Diff(spotPrice, history.Spot.Older.Value); //udělá to tohle (a(new) - b(old)) / b * 100 při a 1 b 3,5 vyjde -71,42 (%) příklad
                    var df = Diff(futuresPrice, history.Futures.Older.Value); //udělá to tohle (a(new) - b(old)) / b * 100 při a 1 b 3,5 vyjde -71,42 (%) příklad
                    var cf = Settings.Default.ContributionFilter; //hodnota spot contribution
                    if (higher) //když je spot výš
                    {
                        //Pump scenario
                        valid &= ds > 0; // Spot is pumping - když je ds výš než 0, tak se valid nezmění, pokud menší nebo rovno 0, tak se změní na f
                        details.Append($"{(ds > 0 ? "✔️" : "❌")} Spot 1m: {ds:F2}%\n"); //připne k details zprávu o tom, že ds je nebo není větší než 0

                        //Spot should do the majority of the pump
                        var spotPortion = Math.Max(0, ds - Math.Max(0, df)) / diff * 100; //větší(0,dif spotu - větší(0, dif fut)) /diff
                        valid &= spotPortion > cf;
                        details.Append($"{(spotPortion > cf ? "✔️" : "❌")} Spot pump contribution \\>{cf}%: {spotPortion:F2}%\n");
                    }
                    else
                    {
                        //Dump scenario
                        valid &= ds < 0; // Spot is dumping
                        details.Append($"{(ds < 0 ? "✔️" : "❌")} Spot 1m: {ds:F2}%\n");

                        //Spot should do the majority of the dump
                        var spotPortion = Math.Min(0, ds - Math.Min(0, df)) / diff * 100;
                        cf = -cf;
                        valid &= spotPortion < cf;
                        details.Append($"{(spotPortion < cf ? "✔️" : "❌")} Spot dump contribution \\<{cf}%: {spotPortion:F2}%\n");
                    }

                    details.Append($"ℹ️ Futures 1m: {df:F2}%\n");
                }
                else //pokud historie neexistuje, přepne to ten parametr valid na f
                {
                    valid = false;
                }

                if (valid)
                {
                    if (signalMuted) return;

                    mutedSignal = DateTime.Now.AddMinutes(5);
                    mutedInfo = DateTime.Now.AddMinutes(30);
                }
                else
                {
                    if (infoMuted) return;

                    mutedInfo = DateTime.Now.AddMinutes(30);
                }

                async Task SignalBinance()
                {
#if RELEASE
                        if (!valid) return;

                        try
                        {
                            await clientB.Signal(signal);
                        }
                        catch { }
#endif
                }

                async Task SignalFinandy()
                {
#if RELEASE
                        try
                        {
                            await client.Signal(signal, valid);
                        }
                        catch { }
#endif
                }

                async Task SendMessage()
                {
                    var message = new StringBuilder();
                    message.Append(higher ? "🚀" : "⚰️");
                    message.Append($" \\[{(valid ? (signal.IsShort ? "SHORT" : "LONG") : "info")}\\] ");
                    message.Append($"Spot `{symbol}` price is {diff:F2}% {(higher ? "higher" : "lower")} than Futures price\\!\n");
                    message.Append($"\\(Links: [TradingView](https://www.tradingview.com/chart/?symbol={symbol}PERP), [Binance](https://www.binance.com/en/futures/{symbol})\\)\n\n");
                    message.Append($"📍 Spot: {spotPrice.Normalize()}\n");
                    message.Append($"🔮 Futures: {futuresPrice.Normalize()}\n");
                    message.Append($"🧲 Leverage: {leverageInfo}x\n\n");
                    message.Append(details);

                    await telegramClient.SendTextMessageAsync(chatId: Settings.Default.ChatId, text: message.ToString().Replace("-", "\\-"), parseMode: ParseMode.MarkdownV2, cancellationToken: ct);
                }

                await Task.WhenAll(
                    SignalBinance(),
                    SignalFinandy(),
                    SendMessage()
                    );
            }
        }

        private static decimal DiffAbs(decimal a, decimal b) => Math.Abs(a - b) / b * 100;
        private static decimal Diff(decimal a, decimal b) => (a - b) / b * 100;

        record HistoryRecord(decimal? Older, decimal Newer);
        record HistoryPair(HistoryRecord Spot, HistoryRecord Futures);
    }
}