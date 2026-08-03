namespace ServiceLib.Services.Statistics;

public sealed class TrafficByteAccumulator
{
    private long _upRemainderBytes;
    private long _downRemainderBytes;

    public (long UpKilobytes, long DownKilobytes) Add(long upBytes, long downBytes)
    {
        var upKilobytes = AddOne(Math.Max(0, upBytes), ref _upRemainderBytes);
        var downKilobytes = AddOne(Math.Max(0, downBytes), ref _downRemainderBytes);
        return (upKilobytes, downKilobytes);
    }

    private static long AddOne(long bytes, ref long remainderBytes)
    {
        var kilobytes = bytes / 1024;
        remainderBytes += bytes % 1024;
        if (remainderBytes >= 1024)
        {
            kilobytes += remainderBytes / 1024;
            remainderBytes %= 1024;
        }
        return kilobytes;
    }
}
