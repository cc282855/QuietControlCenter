namespace ServiceLib.Services.Statistics;

public static class ServerTrafficPeriod
{
    public static long GetDayKey(DateTime now) => now.Date.Ticks;

    public static long GetMonthKey(DateTime now) => new DateTime(now.Year, now.Month, 1).Ticks;

    public static (long Up, long Down) GetTodayValues(ServerStatItem? item, DateTime now)
    {
        if (item is null || item.DateNow != GetDayKey(now))
        {
            return (0, 0);
        }

        return (Math.Max(0, item.TodayUp), Math.Max(0, item.TodayDown));
    }

    public static bool Normalize(ServerStatItem item, DateTime now)
    {
        var changed = false;
        var dayKey = GetDayKey(now);
        if (item.DateNow != dayKey)
        {
            item.TodayUp = 0;
            item.TodayDown = 0;
            item.DateNow = dayKey;
            changed = true;
        }

        var monthKey = GetMonthKey(now);
        if (item.MonthNow != monthKey)
        {
            item.MonthUp = 0;
            item.MonthDown = 0;
            item.MonthNow = monthKey;
            changed = true;
        }

        return changed;
    }

    public static void Add(ServerStatItem item, long upKilobytes, long downKilobytes, DateTime now)
    {
        Normalize(item, now);
        upKilobytes = Math.Max(0, upKilobytes);
        downKilobytes = Math.Max(0, downKilobytes);

        item.TodayUp += upKilobytes;
        item.TodayDown += downKilobytes;
        item.MonthUp += upKilobytes;
        item.MonthDown += downKilobytes;
        item.TotalUp += upKilobytes;
        item.TotalDown += downKilobytes;
    }
}
