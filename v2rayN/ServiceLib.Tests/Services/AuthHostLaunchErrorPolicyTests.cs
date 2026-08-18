using Xunit;

namespace ServiceLib.Tests.Services;

public sealed class AuthHostLaunchErrorPolicyTests
{
    [Theory]
    [InlineData(1314, SubscriptionQuotaDiagnosticCode.ProcessCreatePrivilege, true)]
    [InlineData(5, SubscriptionQuotaDiagnosticCode.ProcessCreateAccessOrPolicy, false)]
    [InlineData(1260, SubscriptionQuotaDiagnosticCode.ProcessCreateAccessOrPolicy, true)]
    [InlineData(2, SubscriptionQuotaDiagnosticCode.ProcessCreateImageOrElevation, true)]
    [InlineData(193, SubscriptionQuotaDiagnosticCode.ProcessCreateImageOrElevation, true)]
    [InlineData(740, SubscriptionQuotaDiagnosticCode.ProcessCreateImageOrElevation, true)]
    [InlineData(87, SubscriptionQuotaDiagnosticCode.ProcessCreateParameter, true)]
    [InlineData(206, SubscriptionQuotaDiagnosticCode.ProcessCreateParameter, true)]
    [InlineData(1450, SubscriptionQuotaDiagnosticCode.ProcessCreateResource, false)]
    [InlineData(9999, SubscriptionQuotaDiagnosticCode.ProcessCreateOther, false)]
    public void MapsSafeCategoriesAndGlobalScope(
        int error,
        SubscriptionQuotaDiagnosticCode expected,
        bool global)
    {
        Assert.Equal(expected, AuthHostLaunchErrorPolicy.MapProcessCreateError(error));
        Assert.Equal(global, AuthHostLaunchErrorPolicy.IsGlobalProcessCreateError(error));
    }

    [Fact]
    public void DeterministicSelectionIsInvariantToEnumerationOrder()
    {
        int[] errors = [5, 5, 1450, 9999];
        var expected = AuthHostLaunchErrorPolicy.SelectDeterministic(errors);
        var random = new Random(72442);

        for (var iteration = 0; iteration < 100; iteration++)
        {
            var shuffled = errors.OrderBy(_ => random.Next()).ToArray();
            Assert.Equal(expected, AuthHostLaunchErrorPolicy.SelectDeterministic(shuffled));
        }

        Assert.Equal(SubscriptionQuotaDiagnosticCode.ProcessCreateAccessOrPolicy, expected.Diagnostic);
        Assert.Equal(5, expected.NativeErrorCode);
    }

    [Fact]
    public void FullyMixedErrorsDoNotPretendToHaveOneRootCause()
    {
        var selected = AuthHostLaunchErrorPolicy.SelectDeterministic([5, 1450, 9999]);

        Assert.Equal(SubscriptionQuotaDiagnosticCode.ProcessCreateOther, selected.Diagnostic);
        Assert.Equal(0, selected.NativeErrorCode);
    }
}
