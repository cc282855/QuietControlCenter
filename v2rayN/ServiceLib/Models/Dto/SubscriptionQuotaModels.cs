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
    ImportedNodeCache,
    OfficialWebsite
}

public enum SubscriptionQuotaDiagnosticCode
{
    None,
    TicketDirectoryFailed,
    TicketProtectionFailed,
    HelperVerificationFailed,
    TicketWriteFailed,
    PipeCreationFailed,
    InteractiveShellUnavailable,
    ShellTokenUnavailable,
    MediumProcessCreateFailed,
    ChildExitedEarly,
    ChildIdentityValidationFailed,
    UnknownStartFailure,
    ProcessCreatePrivilege,
    ProcessCreateAccessOrPolicy,
    ProcessCreateImageOrElevation,
    ProcessCreateParameter,
    ProcessCreateProfileOrLogon,
    ProcessCreateResource,
    ProcessCreateOther,
    EnvironmentBlockFailed,
    ChildCleanupFailed,
    ChildExitedBeforeConnection,
    PipeConnectionFailed,
    PipePeerValidationFailed,
    ResponseReadFailed,
    ResponseValidationFailed,
    AckWriteFailed,
    CommitReadFailed,
    CommitValidationFailed
}

public enum SubscriptionQuotaCacheStatus
{
    NotAttempted,
    Success,
    NoRows,
    NoMarker,
    Conflict,
    Malformed,
    SubIdMismatch,
    MissingSubscriptionBinding
}

public sealed class ProfileQuotaRemarkRow
{
    public string? Subid { get; set; }
    public string? Remarks { get; set; }
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
    SubscriptionQuotaSnapshot? Snapshot = null,
    SubscriptionQuotaDiagnosticCode Diagnostic = SubscriptionQuotaDiagnosticCode.None,
    int NativeErrorCode = 0)
{
    public bool IsSuccess => Status == SubscriptionQuotaStatusCode.Success && Snapshot is not null;
}

public sealed record SubscriptionQuotaCacheResult(
    SubscriptionQuotaCacheStatus Status,
    SubscriptionQuotaResult? Quota = null)
{
    public bool IsSuccess => Status == SubscriptionQuotaCacheStatus.Success && Quota?.IsSuccess == true;
}

public sealed record SubscriptionQuotaResolution(
    SubscriptionQuotaResult DisplayResult,
    SubscriptionQuotaResult LiveResult,
    SubscriptionQuotaCacheStatus CacheStatus,
    SubscriptionQuotaResult? AccountResult = null);
