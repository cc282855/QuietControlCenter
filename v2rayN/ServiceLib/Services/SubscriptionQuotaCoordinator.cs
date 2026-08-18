namespace ServiceLib.Services;

public sealed class SubscriptionQuotaCoordinator
{
    public async Task<SubscriptionQuotaResolution> ResolveAsync(
        SubscriptionQuotaResult liveResult,
        string capturedSubId,
        Func<CancellationToken, Task<IReadOnlyList<ProfileQuotaRemarkRow>>> readImportedRemarks,
        Func<CancellationToken, Task<bool>> bindingIsCurrent,
        Func<CancellationToken, Task<SubscriptionQuotaResult>>? queryAccount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(liveResult);
        ArgumentNullException.ThrowIfNull(readImportedRemarks);
        ArgumentNullException.ThrowIfNull(bindingIsCurrent);

        if (liveResult.Status == SubscriptionQuotaStatusCode.Cancelled)
            return new(liveResult, liveResult, SubscriptionQuotaCacheStatus.NotAttempted);

        if (liveResult.IsSuccess)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await bindingIsCurrent(cancellationToken).ConfigureAwait(false)
                ? new(liveResult, liveResult, SubscriptionQuotaCacheStatus.NotAttempted)
                : Mismatch(liveResult);
        }

        if (string.IsNullOrEmpty(capturedSubId))
            return new(liveResult, liveResult, SubscriptionQuotaCacheStatus.MissingSubscriptionBinding);

        cancellationToken.ThrowIfCancellationRequested();
        if (!await bindingIsCurrent(cancellationToken).ConfigureAwait(false))
            return Mismatch(liveResult);

        var rows = await readImportedRemarks(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!await bindingIsCurrent(cancellationToken).ConfigureAwait(false))
            return Mismatch(liveResult);

        var cache = SubscriptionQuotaParser.ParseImportedRemarkRows(
            capturedSubId, rows, DateTimeOffset.UtcNow);
        if (cache.IsSuccess)
        {
            // Structural early return: account/session code is unreachable on every cache hit.
            return new(cache.Quota!, liveResult, SubscriptionQuotaCacheStatus.Success);
        }

        if (queryAccount is null
            || cache.Status is not (SubscriptionQuotaCacheStatus.NoRows or SubscriptionQuotaCacheStatus.NoMarker)
            || liveResult.Status is not (SubscriptionQuotaStatusCode.Unsupported or SubscriptionQuotaStatusCode.LoginRequired))
        {
            return new(ToConnectionResult(cache.Status, liveResult), liveResult, cache.Status);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!await bindingIsCurrent(cancellationToken).ConfigureAwait(false))
            return Mismatch(liveResult);

        var account = await queryAccount(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!await bindingIsCurrent(cancellationToken).ConfigureAwait(false))
            return Mismatch(liveResult);

        return new(
            account.IsSuccess ? account : ToConnectionResult(cache.Status, liveResult),
            liveResult,
            cache.Status,
            account);
    }

    private static SubscriptionQuotaResolution Mismatch(SubscriptionQuotaResult liveResult)
        => new(new(SubscriptionQuotaStatusCode.InvalidRequest), liveResult,
            SubscriptionQuotaCacheStatus.SubIdMismatch);

    private static SubscriptionQuotaResult ToConnectionResult(
        SubscriptionQuotaCacheStatus cacheStatus,
        SubscriptionQuotaResult liveResult)
        => cacheStatus switch
        {
            SubscriptionQuotaCacheStatus.Conflict or SubscriptionQuotaCacheStatus.Malformed
                => new(SubscriptionQuotaStatusCode.Malformed),
            SubscriptionQuotaCacheStatus.MissingSubscriptionBinding or SubscriptionQuotaCacheStatus.SubIdMismatch
                => new(SubscriptionQuotaStatusCode.InvalidRequest),
            _ => liveResult
        };
}

public static class SubscriptionQuotaRemarkRepository
{
    public static async Task<IReadOnlyList<ProfileQuotaRemarkRow>> QueryAsync(
        SQLiteAsyncConnection connection,
        string subId,
        int maximumRows)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (string.IsNullOrEmpty(subId) || maximumRows is <= 0 or > SubscriptionQuotaParser.MaxImportedRemarkCount + 1)
            return [];

        return await connection.QueryAsync<ProfileQuotaRemarkRow>(
            "select Subid, Remarks from ProfileItem where Subid = ? limit ?", subId, maximumRows)
            .ConfigureAwait(false);
    }
}
