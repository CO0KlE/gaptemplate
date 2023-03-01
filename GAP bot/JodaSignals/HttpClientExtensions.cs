using System.Text;
using System.Text.Json;

namespace BinanceAlert
{
    internal static class HttpClientExtensions
    {
        public static async Task<HttpResponseMessage> PostJsonAsync<T>(this HttpClient client, string requestUri, T value)
        {
            var data = JsonSerializer.Serialize(value, JsonOptions.Default);
            var content = new StringContent(data, Encoding.UTF8, "application/json");

            return await client.PostAsync(requestUri, content)
                               .ConfigureAwait(false);
        }

        public static async ValueTask PostFinandyAsync(this HttpClient client, Signal signal, string settings)
        {
            var parsed = settings.Split(';');

            var payload = new SignalPayload(
                parsed[0].Trim(),
                parsed[1].Trim(),
                signal.IsShort ? "sell" : "buy",
                signal.Symbol,
                new Open(signal.Leverage.ToString())
            );

            using var response = await client.PostJsonAsync($"https://hook.finandy.com/{parsed[2].Trim()}", payload);

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    await response.Content.ReadAsStringAsync(),
                    inner: null,
                    response.StatusCode);
            }
        }
    }
}