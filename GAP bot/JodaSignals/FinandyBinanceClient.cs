namespace BinanceAlert
{
    internal class FinandyBinanceClient : IDisposable
    {
        private readonly HttpClient _client;
        private bool disposedValue;

        public FinandyBinanceClient()
        {
            _client = new()
            {
                Timeout = TimeSpan.FromSeconds(15)
            };
        }

        public async ValueTask Signal(Signal signal)
        {
            await _client.PostFinandyAsync(signal, JodaSignals.Settings.Default.BinanceSignal);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    _client.Dispose();
                }
                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}