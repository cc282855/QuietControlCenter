namespace ServiceLib.Models.Dto;

public enum SubscriptionQuotaStatusCode
{
    Success,
    Unsupported,
    Malformed,
    BodyTooLarge,
    InvalidRequest,
    ProxyUnavailable,
    NetworkError,
    HttpError,
    MissingOfficialUrl,
    OfficialUrlConfirmationRequired,
    LoginRequired,
    AuthenticatedUnsupported,
    WebView2RuntimeMissing,
    AuthHostHelperMissing,
    AuthHostStartFailed,
    AuthHostCommunicationFailed,
    AuthHostUnavailable,
    SessionCleared,
    SessionClearFailed,
    Cancelled
}

public enum SubscriptionQuotaSource
{
    Header,
    ResponseBody,
    OfficialWebsite
}

public sealed record SubscriptionQuotaSnapshot(
    ulong UploadBytes,
    ulong DownloadBytes,
    ulong? TotalBytes,
    ulong RemainingBytes,
    DateTimeOffset? ExpiresAtUtc,
    DateTimeOffset RetrievedAtUtc,
    SubscriptionQuotaSource Source);

public sealed record SubscriptionQuotaResult(
    SubscriptionQuotaStatusCode Status,
    SubscriptionQuotaSnapshot? Snapshot = null)
{
    public bool IsSuccess => Status == SubscriptionQuotaStatusCode.Success && Snapshot is not null;
}
