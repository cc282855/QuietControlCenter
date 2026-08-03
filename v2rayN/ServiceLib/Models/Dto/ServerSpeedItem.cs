namespace ServiceLib.Models.Dto;

[Serializable]
public class ServerSpeedItem : ServerStatItem
{
    public long ProxyUp { get; set; }

    public long ProxyDown { get; set; }

    public long DirectUp { get; set; }

    public long DirectDown { get; set; }

    // The live fields above are KB/s. These fields preserve the exact byte
    // delta for the sampling window so history is independent of tick timing.
    public long ProxyUpBytes { get; set; }

    public long ProxyDownBytes { get; set; }

    public long DirectUpBytes { get; set; }

    public long DirectDownBytes { get; set; }

    public long CoreGeneration { get; set; }
}

[Serializable]
public class TrafficItem
{
    public ulong Up { get; set; }

    public ulong Down { get; set; }
}
