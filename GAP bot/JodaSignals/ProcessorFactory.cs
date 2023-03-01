using System.Collections.Concurrent;
using Binance.Net.Clients;
using Telegram.Bot;

namespace BinanceAlert
{
    internal class ProcessorFactory
    {
        private readonly ConcurrentDictionary<string, SymbolProcessor> processors = new();

        private readonly TelegramBotClient telegramClient;

        private readonly FinandyClient client;
        private readonly FinandyBinanceClient clientB;

        public ProcessorFactory(TelegramBotClient telegramClient, FinandyClient client, FinandyBinanceClient clientB)
        {
            this.telegramClient = telegramClient;
            this.client = client;
            this.clientB = clientB;
        }

        public SymbolProcessor GetProcessor(string symbol)
        {
            return processors.GetOrAdd(symbol, CreateNew);
        }

        private SymbolProcessor CreateNew(string symbol)
        {
            return new SymbolProcessor(symbol, telegramClient, client, clientB);
        }

        public ICollection<KeyValuePair<string, SymbolProcessor>> All()
        {
            return processors.ToList();
        }
    }
}