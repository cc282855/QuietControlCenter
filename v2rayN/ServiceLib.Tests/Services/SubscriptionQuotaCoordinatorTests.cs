using System.Globalization;
using Xunit;

namespace ServiceLib.Tests.Services;

public sealed class SubscriptionQuotaCoordinatorTests
{
    private static readonly DateTimeOffset RetrievedAt = new(2026, 8, 18, 0, 0, 0, TimeSpan.Zero);
    private const string SubId = "subscription-a";

    [Fact]
    public async Task Repository_QueriesBaseTableBySubIdWithoutRegionFilter()
    {
        var path = Path.Combine(Path.GetTempPath(), $"qcc-quota-{Guid.NewGuid():N}.db");
        var connection = new SQLiteAsyncConnection(path);
        try
        {
            await connection.ExecuteAsync(
                "create table ProfileItem (IndexId text primary key, Subid text, Remarks text)");
            await connection.ExecuteAsync(
                "insert into ProfileItem(IndexId, Subid, Remarks) values (?, ?, ?)",
                "sg", SubId, "新加坡节点");
            await connection.ExecuteAsync(
                "insert into ProfileItem(IndexId, Subid, Remarks) values (?, ?, ?)",
                "us", SubId, "美国节点");
            await connection.ExecuteAsync(
                "insert into ProfileItem(IndexId, Subid, Remarks) values (?, ?, ?)",
                "quota", SubId, "🛠️剩余流量：208.41 GB");
            await connection.ExecuteAsync(
                "insert into ProfileItem(IndexId, Subid, Remarks) values (?, ?, ?)",
                "foreign", "subscription-b", "剩余流量：999 GB");

            var rows = await SubscriptionQuotaRemarkRepository.QueryAsync(
                connection, SubId, SubscriptionQuotaParser.MaxImportedRemarkCount + 1);
            var result = SubscriptionQuotaParser.ParseImportedRemarkRows(SubId, rows, RetrievedAt);

            Assert.Equal(3, rows.Count);
            Assert.All(rows, row => Assert.Equal(SubId, row.Subid));
            Assert.True(result.IsSuccess);
            Assert.Equal((ulong)(208.41m * 1024 * 1024 * 1024), result.Quota!.Snapshot!.RemainingBytes);
        }
        finally
        {
            await connection.CloseAsync();
            File.Delete(path);
        }
    }

    [Fact]
    public void ImportedRows_DistinguishNoRowsNoMarkerConflictAndSubIdMismatch()
    {
        Assert.Equal(SubscriptionQuotaCacheStatus.NoRows,
            Parse([]).Status);
        Assert.Equal(SubscriptionQuotaCacheStatus.NoMarker,
            Parse([Row("新加坡节点")]).Status);
        Assert.Equal(SubscriptionQuotaCacheStatus.Conflict,
            Parse([Row("剩余流量：1 GB"), Row("剩余流量：2 GB")]).Status);
        Assert.Equal(SubscriptionQuotaCacheStatus.SubIdMismatch,
            Parse([new() { Subid = "subscription-b", Remarks = "剩余流量：2 GB" }]).Status);
    }

    [Theory]
    [InlineData("剩余流量：208．41 GB")]
    [InlineData("剩余流量：208​.41 GB")]
    [InlineData("剩余流量：208.4100 GB")]
    public void ImportedRows_ClassifyMarkerShapedInvalidValuesAsMalformed(string remark)
        => Assert.Equal(SubscriptionQuotaCacheStatus.Malformed, Parse([Row(remark)]).Status);

    [Fact]
    public void ImportedRows_Enforce4096Boundary()
    {
        var accepted = Enumerable.Range(0, SubscriptionQuotaParser.MaxImportedRemarkCount)
            .Select(_ => Row("ordinary")).ToArray();
        var rejected = accepted.Append(Row("ordinary")).ToArray();

        Assert.Equal(SubscriptionQuotaCacheStatus.NoMarker, Parse(accepted).Status);
        Assert.Equal(SubscriptionQuotaCacheStatus.Malformed, Parse(rejected).Status);
    }

    [Theory]
    [InlineData("ar-SA", "🛠剩余流量：208.41 GB")]
    [InlineData("de-DE", "🛠️剩余流量：208.41 GB")]
    public void ImportedRows_AreCultureInvariantWithOrWithoutVs16(string culture, string remark)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var result = Parse([Row(remark)]);
            Assert.True(result.IsSuccess);
            Assert.Equal((ulong)(208.41m * 1024 * 1024 * 1024), result.Quota!.Snapshot!.RemainingBytes);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData(SubscriptionQuotaStatusCode.Unsupported)]
    [InlineData(SubscriptionQuotaStatusCode.LoginRequired)]
    [InlineData(SubscriptionQuotaStatusCode.NetworkError)]
    [InlineData(SubscriptionQuotaStatusCode.ProxyUnavailable)]
    [InlineData(SubscriptionQuotaStatusCode.HttpError)]
    [InlineData(SubscriptionQuotaStatusCode.InvalidRequest)]
    public async Task CacheHitAlwaysReturnsBeforeAccount(SubscriptionQuotaStatusCode liveStatus)
    {
        var accountCalls = 0;
        var coordinator = new SubscriptionQuotaCoordinator();

        var resolution = await coordinator.ResolveAsync(
            new(liveStatus),
            SubId,
            _ => Task.FromResult<IReadOnlyList<ProfileQuotaRemarkRow>>([Row("剩余流量：208.41 GB")]),
            _ => Task.FromResult(true),
            _ =>
            {
                accountCalls++;
                throw new InvalidOperationException("Account must be unreachable on cache hit.");
            },
            CancellationToken.None);

        Assert.True(resolution.DisplayResult.IsSuccess);
        Assert.Equal(SubscriptionQuotaSource.ImportedNodeCache, resolution.DisplayResult.Snapshot!.Source);
        Assert.Equal(SubscriptionQuotaCacheStatus.Success, resolution.CacheStatus);
        Assert.Equal(0, accountCalls);
    }

    [Theory]
    [InlineData(SubscriptionQuotaStatusCode.Unsupported, SubscriptionQuotaCacheStatus.NoRows, 1)]
    [InlineData(SubscriptionQuotaStatusCode.LoginRequired, SubscriptionQuotaCacheStatus.NoMarker, 1)]
    [InlineData(SubscriptionQuotaStatusCode.NetworkError, SubscriptionQuotaCacheStatus.NoMarker, 0)]
    [InlineData(SubscriptionQuotaStatusCode.ProxyUnavailable, SubscriptionQuotaCacheStatus.NoRows, 0)]
    public async Task AccountFallbackRequiresAuthenticationLiveStateAndBenignCacheMiss(
        SubscriptionQuotaStatusCode liveStatus,
        SubscriptionQuotaCacheStatus expectedCache,
        int expectedAccountCalls)
    {
        var accountCalls = 0;
        IReadOnlyList<ProfileQuotaRemarkRow> rows = expectedCache == SubscriptionQuotaCacheStatus.NoRows
            ? []
            : [Row("ordinary")];
        var coordinator = new SubscriptionQuotaCoordinator();

        var resolution = await coordinator.ResolveAsync(
            new(liveStatus), SubId,
            _ => Task.FromResult(rows),
            _ => Task.FromResult(true),
            _ =>
            {
                accountCalls++;
                return Task.FromResult(new SubscriptionQuotaResult(
                    SubscriptionQuotaStatusCode.AuthHostStartFailed,
                    null,
                    SubscriptionQuotaDiagnosticCode.ProcessCreatePrivilege));
            },
            CancellationToken.None);

        Assert.Equal(expectedCache, resolution.CacheStatus);
        Assert.Equal(expectedAccountCalls, accountCalls);
        Assert.Equal(liveStatus, resolution.LiveResult.Status);
        if (expectedAccountCalls == 1)
            Assert.Equal(SubscriptionQuotaStatusCode.AuthHostStartFailed, resolution.AccountResult!.Status);
    }

    [Theory]
    [InlineData("剩余流量：1 GB", "剩余流量：2 GB", SubscriptionQuotaCacheStatus.Conflict)]
    [InlineData("剩余流量：208．41 GB", "ordinary", SubscriptionQuotaCacheStatus.Malformed)]
    public async Task IntegrityCacheFailuresNeverInvokeAccount(
        string first,
        string second,
        SubscriptionQuotaCacheStatus expected)
    {
        var accountCalls = 0;
        var resolution = await new SubscriptionQuotaCoordinator().ResolveAsync(
            new(SubscriptionQuotaStatusCode.Unsupported), SubId,
            _ => Task.FromResult<IReadOnlyList<ProfileQuotaRemarkRow>>([Row(first), Row(second)]),
            _ => Task.FromResult(true),
            _ =>
            {
                accountCalls++;
                return Task.FromResult(new SubscriptionQuotaResult(SubscriptionQuotaStatusCode.LoginRequired));
            },
            CancellationToken.None);

        Assert.Equal(expected, resolution.CacheStatus);
        Assert.Equal(0, accountCalls);
    }

    [Fact]
    public async Task BindingChangeDuringCacheAwaitDiscardsResultAndSkipsAccount()
    {
        var bindingChecks = 0;
        var accountCalls = 0;

        var resolution = await new SubscriptionQuotaCoordinator().ResolveAsync(
            new(SubscriptionQuotaStatusCode.Unsupported), SubId,
            _ => Task.FromResult<IReadOnlyList<ProfileQuotaRemarkRow>>([Row("剩余流量：208.41 GB")]),
            _ => Task.FromResult(++bindingChecks == 1),
            _ =>
            {
                accountCalls++;
                return Task.FromResult(new SubscriptionQuotaResult(SubscriptionQuotaStatusCode.LoginRequired));
            },
            CancellationToken.None);

        Assert.Equal(SubscriptionQuotaCacheStatus.SubIdMismatch, resolution.CacheStatus);
        Assert.Equal(0, accountCalls);
    }

    private static ProfileQuotaRemarkRow Row(string remark) => new() { Subid = SubId, Remarks = remark };

    private static SubscriptionQuotaCacheResult Parse(IReadOnlyCollection<ProfileQuotaRemarkRow> rows)
        => SubscriptionQuotaParser.ParseImportedRemarkRows(SubId, rows, RetrievedAt);
}
