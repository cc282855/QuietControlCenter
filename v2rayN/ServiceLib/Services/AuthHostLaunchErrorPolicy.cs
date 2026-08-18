namespace ServiceLib.Services;

public static class AuthHostLaunchErrorPolicy
{
    private static readonly int[] GlobalErrors = [1314, 1260, 2, 3, 193, 216, 740, 87, 206];

    public static bool IsGlobalProcessCreateError(int errorCode)
        => Array.IndexOf(GlobalErrors, errorCode) >= 0;

    public static SubscriptionQuotaDiagnosticCode MapProcessCreateError(int errorCode)
        => errorCode switch
        {
            1314 => SubscriptionQuotaDiagnosticCode.ProcessCreatePrivilege,
            5 or 1260 => SubscriptionQuotaDiagnosticCode.ProcessCreateAccessOrPolicy,
            2 or 3 or 193 or 216 or 740 => SubscriptionQuotaDiagnosticCode.ProcessCreateImageOrElevation,
            87 or 206 => SubscriptionQuotaDiagnosticCode.ProcessCreateParameter,
            1326 or 1385 => SubscriptionQuotaDiagnosticCode.ProcessCreateProfileOrLogon,
            8 or 14 or 1450 => SubscriptionQuotaDiagnosticCode.ProcessCreateResource,
            _ => SubscriptionQuotaDiagnosticCode.ProcessCreateOther
        };

    public static (SubscriptionQuotaDiagnosticCode Diagnostic, int NativeErrorCode) SelectDeterministic(
        IReadOnlyCollection<int> errorCodes)
    {
        if (errorCodes.Count == 0)
            return (SubscriptionQuotaDiagnosticCode.ShellTokenUnavailable, 0);

        var groups = errorCodes
            .GroupBy(code => (Diagnostic: MapProcessCreateError(code), Code: code))
            .Select(group => new { group.Key.Diagnostic, group.Key.Code, Count = group.Count() })
            .OrderByDescending(group => group.Count)
            .ThenBy(group => Priority(group.Diagnostic))
            .ThenBy(group => group.Code)
            .ToArray();

        if (groups.Length > 1 && groups[0].Count == groups[1].Count)
            return (SubscriptionQuotaDiagnosticCode.ProcessCreateOther, 0);

        return (groups[0].Diagnostic, groups[0].Code);
    }

    private static int Priority(SubscriptionQuotaDiagnosticCode diagnostic)
        => diagnostic switch
        {
            SubscriptionQuotaDiagnosticCode.ProcessCreatePrivilege => 0,
            SubscriptionQuotaDiagnosticCode.ProcessCreateAccessOrPolicy => 1,
            SubscriptionQuotaDiagnosticCode.ProcessCreateImageOrElevation => 2,
            SubscriptionQuotaDiagnosticCode.ProcessCreateParameter => 3,
            SubscriptionQuotaDiagnosticCode.ProcessCreateProfileOrLogon => 4,
            SubscriptionQuotaDiagnosticCode.ProcessCreateResource => 5,
            _ => 6
        };
}
