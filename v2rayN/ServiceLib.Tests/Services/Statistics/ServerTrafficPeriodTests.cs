using ServiceLib.Models.Entities;
using ServiceLib.Services.Statistics;
using Xunit;

namespace ServiceLib.Tests.Services.Statistics;

public class ServerTrafficPeriodTests
{
    [Fact]
    public void Add_AccumulatesTodayMonthAndLifetimeTogether()
    {
        var now = new DateTime(2026, 8, 5, 12, 0, 0);
        var item = CreateItem(now, todayUp: 10, todayDown: 20, monthUp: 30, monthDown: 40, totalUp: 50, totalDown: 60);

        ServerTrafficPeriod.Add(item, 3, 7, now);

        Assert.Equal(13, item.TodayUp);
        Assert.Equal(27, item.TodayDown);
        Assert.Equal(33, item.MonthUp);
        Assert.Equal(47, item.MonthDown);
        Assert.Equal(53, item.TotalUp);
        Assert.Equal(67, item.TotalDown);
    }

    [Fact]
    public void Add_OnNextDayResetsTodayButKeepsMonthAndLifetime()
    {
        var previous = new DateTime(2026, 8, 5, 23, 59, 0);
        var now = previous.AddDays(1);
        var item = CreateItem(previous, todayUp: 10, todayDown: 20, monthUp: 30, monthDown: 40, totalUp: 50, totalDown: 60);

        ServerTrafficPeriod.Add(item, 3, 7, now);

        Assert.Equal(3, item.TodayUp);
        Assert.Equal(7, item.TodayDown);
        Assert.Equal(33, item.MonthUp);
        Assert.Equal(47, item.MonthDown);
        Assert.Equal(53, item.TotalUp);
        Assert.Equal(67, item.TotalDown);
    }

    [Fact]
    public void Add_OnNextMonthResetsTodayAndMonthButKeepsLifetime()
    {
        var previous = new DateTime(2026, 8, 31, 23, 59, 0);
        var now = previous.AddDays(1);
        var item = CreateItem(previous, todayUp: 10, todayDown: 20, monthUp: 30, monthDown: 40, totalUp: 50, totalDown: 60);

        ServerTrafficPeriod.Add(item, 3, 7, now);

        Assert.Equal(3, item.TodayUp);
        Assert.Equal(7, item.TodayDown);
        Assert.Equal(3, item.MonthUp);
        Assert.Equal(7, item.MonthDown);
        Assert.Equal(53, item.TotalUp);
        Assert.Equal(67, item.TotalDown);
    }

    [Fact]
    public void Normalize_LegacyRecordStartsCurrentMonthAtZeroAndPreservesLifetime()
    {
        var now = new DateTime(2026, 8, 5, 12, 0, 0);
        var item = new ServerStatItem
        {
            DateNow = ServerTrafficPeriod.GetDayKey(now),
            TodayUp = 10,
            TodayDown = 20,
            MonthNow = 0,
            MonthUp = 0,
            MonthDown = 0,
            TotalUp = 50,
            TotalDown = 60
        };

        Assert.True(ServerTrafficPeriod.Normalize(item, now));
        Assert.Equal(ServerTrafficPeriod.GetMonthKey(now), item.MonthNow);
        Assert.Equal(0, item.MonthUp);
        Assert.Equal(0, item.MonthDown);
        Assert.Equal(50, item.TotalUp);
        Assert.Equal(60, item.TotalDown);
    }

    [Fact]
    public void GetTodayValues_StaleRecordReturnsZeroWithoutChangingLifetime()
    {
        var previous = new DateTime(2026, 8, 5, 23, 59, 0);
        var item = CreateItem(previous, todayUp: 10, todayDown: 20, monthUp: 30, monthDown: 40, totalUp: 50, totalDown: 60);

        var today = ServerTrafficPeriod.GetTodayValues(item, previous.AddDays(1));

        Assert.Equal((0L, 0L), today);
        Assert.Equal(50, item.TotalUp);
        Assert.Equal(60, item.TotalDown);
    }

    [Fact]
    public void GetTodayValues_CurrentRecordReturnsStoredValues()
    {
        var now = new DateTime(2026, 8, 5, 12, 0, 0);
        var item = CreateItem(now, todayUp: 10, todayDown: 20, monthUp: 30, monthDown: 40, totalUp: 50, totalDown: 60);

        Assert.Equal((10L, 20L), ServerTrafficPeriod.GetTodayValues(item, now));
    }

    private static ServerStatItem CreateItem(
        DateTime now,
        long todayUp,
        long todayDown,
        long monthUp,
        long monthDown,
        long totalUp,
        long totalDown) => new()
        {
            DateNow = ServerTrafficPeriod.GetDayKey(now),
            MonthNow = ServerTrafficPeriod.GetMonthKey(now),
            TodayUp = todayUp,
            TodayDown = todayDown,
            MonthUp = monthUp,
            MonthDown = monthDown,
            TotalUp = totalUp,
            TotalDown = totalDown
        };
}
