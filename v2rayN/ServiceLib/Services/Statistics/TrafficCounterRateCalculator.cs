namespace ServiceLib.Services.Statistics;

public sealed class TrafficCounterRateCalculator
{
    private ServerSpeedItem? _previous;

    public bool TryCalculate(ServerSpeedItem counters, double elapsedSeconds, out ServerSpeedItem rate)
    {
        rate = new();
        if (_previous is null || elapsedSeconds <= 0 || HasCounterRegression(counters, _previous))
        {
            _previous = counters;
            return false;
        }

        rate.ProxyUpBytes = Math.Max(0, counters.ProxyUp - _previous.ProxyUp);
        rate.ProxyDownBytes = Math.Max(0, counters.ProxyDown - _previous.ProxyDown);
        rate.DirectUpBytes = Math.Max(0, counters.DirectUp - _previous.DirectUp);
        rate.DirectDownBytes = Math.Max(0, counters.DirectDown - _previous.DirectDown);
        rate.ProxyUp = ToKilobytesPerSecond(rate.ProxyUpBytes, elapsedSeconds);
        rate.ProxyDown = ToKilobytesPerSecond(rate.ProxyDownBytes, elapsedSeconds);
        rate.DirectUp = ToKilobytesPerSecond(rate.DirectUpBytes, elapsedSeconds);
        rate.DirectDown = ToKilobytesPerSecond(rate.DirectDownBytes, elapsedSeconds);
        _previous = counters;
        return true;
    }

    public void Reset() => _previous = null;

    private static bool HasCounterRegression(ServerSpeedItem current, ServerSpeedItem previous) =>
        current.ProxyUp < previous.ProxyUp
        || current.ProxyDown < previous.ProxyDown
        || current.DirectUp < previous.DirectUp
        || current.DirectDown < previous.DirectDown;

    private static long ToKilobytesPerSecond(long bytes, double elapsedSeconds) =>
        (long)Math.Round(Math.Max(0, bytes) / elapsedSeconds / 1024d, MidpointRounding.AwayFromZero);
}
