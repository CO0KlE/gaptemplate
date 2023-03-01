namespace BinanceAlert
{
    internal class FinandyClient : IDisposable
    {
        private readonly HttpClient _client;
        private bool disposedValue;

        public FinandyClient()
        {
            _client = new()
            {
                Timeout = TimeSpan.FromSeconds(15)
            };
        }

        public async ValueTask Signal(Signal signal, bool isValid)
        {
            if (isValid)
            {
                await _client.PostFinandyAsync(signal, JodaSignals.Settings.Default.FinandySignal);
            }
            else
            {
                await _client.PostFinandyAsync(signal, JodaSignals.Settings.Default.FinandyInfo);
            }
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