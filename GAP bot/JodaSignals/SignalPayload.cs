namespace BinanceAlert
{
    internal record SignalPayload(
            string Name,
            string Secret,
            string Side,
            string Symbol,
            Open Open
            );
}