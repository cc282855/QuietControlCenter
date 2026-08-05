namespace ServiceLib.Models.Dto;

public sealed record SubscriptionUpdateRequest(
    string SubscriptionId,
    bool UseProxy,
    bool AllowDirectFallback,
    bool IsAutomatic);

public sealed record SubscriptionUpdateResult(
    bool Success,
    int AttemptedCount,
    int SucceededCount)
{
    public static SubscriptionUpdateResult Failed { get; } = new(false, 0, 0);
}

public static class FirstSubscriptionUpdatePolicy
{
    public const string SkippedFeedback = "订阅已保存，但未自动更新；请启用订阅并确认地址为 HTTP(S)，然后手动更新。";
    public const string FailedFeedback = "订阅已保存，但首次自动更新失败，请稍后手动重试。";
    public const string SuccessFeedback = "新订阅已保存并完成首次节点更新。";

    public static bool ShouldUpdate(bool wasNew, bool alreadyConsumed, string? id, bool enabled, string? url)
    {
        if (!wasNew || alreadyConsumed || string.IsNullOrWhiteSpace(id) || !enabled)
        {
            return false;
        }

        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase));
    }
}
