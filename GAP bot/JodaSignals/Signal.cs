namespace BinanceAlert
{
    internal record Signal(string Symbol, bool IsShort, int Leverage);
}