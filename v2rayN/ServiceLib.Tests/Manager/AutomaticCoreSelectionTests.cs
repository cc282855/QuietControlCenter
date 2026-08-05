using Xunit;

namespace ServiceLib.Tests.Manager;

public sealed class AutomaticCoreSelectionTests
{
    [Fact]
    public void EnableAutoCoreSelection_DefaultsToTrue_ForNewAndLegacyJson()
    {
        Assert.True(new CoreBasicItem().EnableAutoCoreSelection);

        var legacy = JsonUtils.Deserialize<CoreBasicItem>("{\"LogEnabled\":false}");

        Assert.NotNull(legacy);
        Assert.True(legacy.EnableAutoCoreSelection);
    }

    [Fact]
    public void SelectCoreType_WhenDisabled_ReturnsManualPreference()
    {
        var profile = CreateProfile(EConfigType.TUIC);

        var actual = AppManager.SelectCoreType(profile, profile.ConfigType, ECoreType.Xray, false);

        Assert.Equal(ECoreType.Xray, actual);
    }

    [Fact]
    public void SelectCoreType_ExplicitProfileOverrideAlwaysWins()
    {
        var profile = CreateProfile(EConfigType.TUIC);
        profile.CoreType = ECoreType.Xray;

        var actual = AppManager.SelectCoreType(profile, profile.ConfigType, ECoreType.sing_box, true);

        Assert.Equal(ECoreType.Xray, actual);
    }

    [Theory]
    [InlineData(EConfigType.VMess, ECoreType.Xray)]
    [InlineData(EConfigType.VMess, ECoreType.sing_box)]
    [InlineData(EConfigType.Hysteria2, ECoreType.Xray)]
    [InlineData(EConfigType.Hysteria2, ECoreType.sing_box)]
    [InlineData(EConfigType.WireGuard, ECoreType.Xray)]
    [InlineData(EConfigType.WireGuard, ECoreType.sing_box)]
    public void SelectCoreType_CompatiblePreferenceIsPreserved(EConfigType configType, ECoreType preferred)
    {
        var profile = CreateProfile(configType);

        var actual = AppManager.SelectCoreType(profile, configType, preferred, true);

        Assert.Equal(preferred, actual);
    }

    [Theory]
    [InlineData(nameof(ETransport.kcp))]
    [InlineData(nameof(ETransport.xhttp))]
    public void SelectCoreType_SingboxUnsupportedTransportFallsBackToXray(string transport)
    {
        var profile = CreateProfile(EConfigType.VMess, transport);

        var actual = AppManager.SelectCoreType(profile, profile.ConfigType, ECoreType.sing_box, true);

        Assert.Equal(ECoreType.Xray, actual);
    }

    [Theory]
    [InlineData(EConfigType.TUIC)]
    [InlineData(EConfigType.Anytls)]
    [InlineData(EConfigType.Naive)]
    public void SelectCoreType_SingboxOnlyProtocolFallsBackToSingbox(EConfigType configType)
    {
        var profile = CreateProfile(configType);

        var actual = AppManager.SelectCoreType(profile, configType, ECoreType.Xray, true);

        Assert.Equal(ECoreType.sing_box, actual);
    }

    [Theory]
    [InlineData("aes-192-gcm", ECoreType.Xray, ECoreType.sing_box)]
    [InlineData("plain", ECoreType.sing_box, ECoreType.Xray)]
    [InlineData("aes-256-gcm", ECoreType.Xray, ECoreType.Xray)]
    [InlineData("aes-256-gcm", ECoreType.sing_box, ECoreType.sing_box)]
    [InlineData("unknown-cipher", ECoreType.Xray, ECoreType.Xray)]
    [InlineData("unknown-cipher", ECoreType.sing_box, ECoreType.sing_box)]
    public void SelectCoreType_ShadowSocksCipherSelectsOnlyCompatibleAlternative(
        string cipher,
        ECoreType preferred,
        ECoreType expected)
    {
        var profile = CreateShadowsocksProfile(cipher);

        var actual = AppManager.SelectCoreType(profile, profile.ConfigType, preferred, true);

        Assert.Equal(expected, actual);
        if (cipher == "unknown-cipher")
        {
            Assert.False(NodeValidator.Validate(profile, preferred).Success);
        }
    }

    [Fact]
    public void SelectCoreType_OrdinaryFieldErrorsDoNotTriggerCoreSwitch()
    {
        var profile = CreateProfile(EConfigType.VMess);
        profile.Address = string.Empty;
        profile.Port = 0;
        profile.Password = string.Empty;

        Assert.True(NodeValidator.IsCoreCompatible(profile, ECoreType.Xray));
        Assert.False(NodeValidator.Validate(profile, ECoreType.Xray).Success);

        var actual = AppManager.SelectCoreType(profile, profile.ConfigType, ECoreType.Xray, true);

        Assert.Equal(ECoreType.Xray, actual);
    }

    [Theory]
    [InlineData(null, EConfigType.VMess, ECoreType.sing_box)]
    [InlineData(EConfigType.Custom, EConfigType.Custom, ECoreType.Xray)]
    [InlineData(EConfigType.PolicyGroup, EConfigType.PolicyGroup, ECoreType.sing_box)]
    public void SelectCoreType_NonSelectableCasesKeepPreference(
        EConfigType? profileType,
        EConfigType requestedType,
        ECoreType preferred)
    {
        var profile = profileType.HasValue ? CreateProfile(profileType.Value) : null;

        var actual = AppManager.SelectCoreType(profile, requestedType, preferred, true);

        Assert.Equal(preferred, actual);
    }

    [Fact]
    public void SelectCoreType_NonSelectableCoreKeepsPreference()
    {
        var profile = CreateProfile(EConfigType.VMess);

        var actual = AppManager.SelectCoreType(profile, profile.ConfigType, ECoreType.v2fly, true);

        Assert.Equal(ECoreType.v2fly, actual);
    }

    [Theory]
    [InlineData("plain", ECoreType.Xray, true)]
    [InlineData("plain", ECoreType.sing_box, false)]
    [InlineData("aes-192-gcm", ECoreType.Xray, false)]
    [InlineData("aes-192-gcm", ECoreType.sing_box, true)]
    [InlineData("plain", ECoreType.v2fly, true)]
    [InlineData("aes-192-gcm", ECoreType.v2fly, false)]
    public void ValidateShadowsocks_UsesTheActualCoreCipherList(
        string cipher,
        ECoreType coreType,
        bool expectedSuccess)
    {
        var result = NodeValidator.Validate(CreateShadowsocksProfile(cipher), coreType);

        Assert.Equal(expectedSuccess, result.Success);
    }

    private static ProfileItem CreateProfile(
        EConfigType configType,
        string transport = nameof(ETransport.raw))
    {
        return new ProfileItem
        {
            ConfigType = configType,
            CoreType = null,
            Address = "example.com",
            Port = 443,
            Password = Guid.NewGuid().ToString(),
            Network = transport,
            Remarks = "test"
        };
    }

    private static ProfileItem CreateShadowsocksProfile(string cipher)
    {
        var profile = CreateProfile(EConfigType.Shadowsocks);
        profile.Password = "password";
        profile.SetProtocolExtra(new ProtocolExtraItem { SsMethod = cipher });
        return profile;
    }
}
