namespace ServiceLib.Models.Entities;

[Serializable]
public class ServerStatItem
{
    [PrimaryKey]
    public string IndexId { get; set; }

    public long TotalUp { get; set; }

    public long TotalDown { get; set; }

    public long TodayUp { get; set; }

    public long TodayDown { get; set; }

    public long DateNow { get; set; }

    public long MonthUp { get; set; }

    public long MonthDown { get; set; }

    public long MonthNow { get; set; }
}
