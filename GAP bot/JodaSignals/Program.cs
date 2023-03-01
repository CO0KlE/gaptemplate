using System.Globalization;
using Binance.Net.Clients;
using Binance.Net.Objects;
using CryptoExchange.Net.Authentication;
using JodaSignals;
using Telegram.Bot;

namespace BinanceAlert
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("cs-CZ");

            var creds = new ApiCredentials(Settings.Default.BinanceKey, Settings.Default.BinanceSecret);
            BinanceClient.SetDefaultOptions(new BinanceClientOptions
            {
                ApiCredentials = creds
            });
            BinanceSocketClient.SetDefaultOptions(new BinanceSocketClientOptions
            {
                ApiCredentials = creds
            });

            var telegram = new TelegramBotClient(Settings.Default.BotToken);

            var processor = new Processor(
                new BinanceClient(),
                telegram,
                new FinandyClient(),
                new FinandyBinanceClient()
                );

            await processor.Run();
        }        
    }
}