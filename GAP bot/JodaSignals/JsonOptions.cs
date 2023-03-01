using System.Text.Json.Serialization;
using System.Text.Json;

namespace BinanceAlert
{
    internal static class JsonOptions
    {
        public static JsonSerializerOptions Default = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }
}