namespace ServiceLib.Common;

public enum ConnectionQualitySeverity
{
    None,
    Good,
    Warning,
    Danger
}

public static class ConnectionQualitySeverityCalculator
{
    public static ConnectionQualitySeverity GetDelaySeverity(int? delayMs)
    {
        if (delayMs is null)
        {
            return ConnectionQualitySeverity.None;
        }

        return delayMs <= 100
            ? ConnectionQualitySeverity.Good
            : delayMs <= 200
                ? ConnectionQualitySeverity.Warning
                : ConnectionQualitySeverity.Danger;
    }

    public static ConnectionQualitySeverity GetJitterSeverity(int? jitterMs)
    {
        if (jitterMs is null)
        {
            return ConnectionQualitySeverity.None;
        }

        return jitterMs <= 20
            ? ConnectionQualitySeverity.Good
            : jitterMs <= 50
                ? ConnectionQualitySeverity.Warning
                : ConnectionQualitySeverity.Danger;
    }

    public static ConnectionQualitySeverity GetLossSeverity(int lossPercent)
    {
        return lossPercent < 1
            ? ConnectionQualitySeverity.Good
            : lossPercent <= 5
                ? ConnectionQualitySeverity.Warning
                : ConnectionQualitySeverity.Danger;
    }

    public static ConnectionQualitySeverity GetJitterLossSeverity(int? jitterMs, int lossPercent)
    {
        var jitterSeverity = GetJitterSeverity(jitterMs);
        var lossSeverity = GetLossSeverity(lossPercent);
        return jitterSeverity > lossSeverity ? jitterSeverity : lossSeverity;
    }
}
