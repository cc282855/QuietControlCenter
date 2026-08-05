namespace ServiceLib.Handler;

public static class SubscriptionHandler
{
    public static async Task<SubscriptionUpdateResult> UpdateProcess(
        Config config,
        string subId,
        bool blProxy,
        Func<bool, string, Task> updateFunc,
        bool allowDirectFallback = true)
    {
        await updateFunc?.Invoke(false, ResUI.MsgUpdateSubscriptionStart);
        var subItem = await AppManager.Instance.SubItems();

        if (subItem is not { Count: > 0 })
        {
            await updateFunc?.Invoke(false, ResUI.MsgNoValidSubscription);
            return SubscriptionUpdateResult.Failed;
        }

        var attemptedCount = 0;
        var successCount = 0;
        foreach (var item in subItem)
        {
            try
            {
                if (!IsValidSubscription(item, subId))
                {
                    continue;
                }

                const string hashCode = "订阅->";
                if (item.Enabled == false)
                {
                    await updateFunc?.Invoke(false, $"{hashCode}{ResUI.MsgSkipSubscriptionUpdate}");
                    continue;
                }

                attemptedCount++;
                var downloadHandle = new DownloadService(redactSensitiveErrors: true);
                await updateFunc?.Invoke(false, $"{hashCode}{ResUI.MsgStartGettingSubscriptions}");

                var result = await DownloadAllSubscriptions(config, item, blProxy, allowDirectFallback, downloadHandle);

                if (await ProcessDownloadResult(config, item.Id, result, hashCode, updateFunc))
                {
                    successCount++;
                }

                await updateFunc?.Invoke(false, "-------------------------------------------------------");
            }
            catch
            {
                Logging.SaveLog("Subscription update failed.");
                await updateFunc?.Invoke(false, $"订阅->{ResUI.MsgFailedImportSubscription}");
                await updateFunc?.Invoke(false, "-------------------------------------------------------");
            }
        }

        await updateFunc?.Invoke(successCount > 0, $"{ResUI.MsgUpdateSubscriptionEnd}");
        return new SubscriptionUpdateResult(successCount > 0, attemptedCount, successCount);
    }

    private static bool IsValidSubscription(SubItem item, string subId)
    {
        var id = item.Id.TrimEx();
        var url = item.Url.TrimEx();

        if (id.IsNullOrEmpty() || url.IsNullOrEmpty())
        {
            return false;
        }

        if (subId.IsNotEmpty() && item.Id != subId)
        {
            return false;
        }

        if (!url.StartsWith(Global.HttpsProtocol) && !url.StartsWith(Global.HttpProtocol))
        {
            return false;
        }

        return true;
    }

    private static async Task<string> DownloadSubscriptionContent(
        DownloadService downloadHandle,
        string url,
        bool blProxy,
        bool allowDirectFallback,
        string userAgent)
    {
        var result = await downloadHandle.TryDownloadString(
            url,
            blProxy,
            userAgent,
            requireProxy: blProxy && !allowDirectFallback);

        if (allowDirectFallback && blProxy && result.IsNullOrEmpty())
        {
            result = await downloadHandle.TryDownloadString(url, false, userAgent);
        }

        return result ?? string.Empty;
    }

    private static async Task<string> DownloadAllSubscriptions(
        Config config,
        SubItem item,
        bool blProxy,
        bool allowDirectFallback,
        DownloadService downloadHandle)
    {
        var result = await DownloadMainSubscription(config, item, blProxy, allowDirectFallback, downloadHandle);

        if (item.ConvertTarget.IsNullOrEmpty() && item.MoreUrl.TrimEx().IsNotEmpty())
        {
            result = await DownloadAdditionalSubscriptions(item, result, blProxy, allowDirectFallback, downloadHandle);
        }

        return result;
    }

    private static async Task<string> DownloadMainSubscription(
        Config config,
        SubItem item,
        bool blProxy,
        bool allowDirectFallback,
        DownloadService downloadHandle)
    {
        // Prepare subscription URL and download directly
        var url = Utils.GetPunycode(item.Url.TrimEx());

        // If conversion is needed
        if (item.ConvertTarget.IsNotEmpty())
        {
            var subConvertUrl = config.ConstItem.SubConvertUrl.IsNullOrEmpty()
                ? Global.SubConvertUrls.FirstOrDefault()
                : config.ConstItem.SubConvertUrl;

            url = string.Format(subConvertUrl!, Utils.UrlEncode(url));

            if (!url.Contains("target="))
            {
                url += $"&target={item.ConvertTarget}";
            }

            if (!url.Contains("config="))
            {
                url += $"&config={Global.SubConvertConfig.FirstOrDefault()}";
            }
        }

        // Download and return result directly
        return await DownloadSubscriptionContent(downloadHandle, url, blProxy, allowDirectFallback, item.UserAgent);
    }

    private static async Task<string> DownloadAdditionalSubscriptions(
        SubItem item,
        string mainResult,
        bool blProxy,
        bool allowDirectFallback,
        DownloadService downloadHandle)
    {
        var result = mainResult;

        // If main subscription result is Base64 encoded, decode it first
        if (result.IsNotEmpty() && Utils.IsBase64String(result))
        {
            result = Utils.Base64Decode(result);
        }

        // Process additional URL list
        var lstUrl = item.MoreUrl.TrimEx().Split(",") ?? [];
        foreach (var it in lstUrl)
        {
            var url2 = Utils.GetPunycode(it);
            if (url2.IsNullOrEmpty())
            {
                continue;
            }

            var additionalResult = await DownloadSubscriptionContent(
                downloadHandle,
                url2,
                blProxy,
                allowDirectFallback,
                item.UserAgent);

            if (additionalResult.IsNotEmpty())
            {
                // Process additional subscription results, add to main result
                if (Utils.IsBase64String(additionalResult))
                {
                    result += Environment.NewLine + Utils.Base64Decode(additionalResult);
                }
                else
                {
                    result += Environment.NewLine + additionalResult;
                }
            }
        }

        return result;
    }

    private static async Task<bool> ProcessDownloadResult(Config config, string id, string result, string hashCode, Func<bool, string, Task> updateFunc)
    {
        if (result.IsNullOrEmpty())
        {
            await updateFunc?.Invoke(false, $"{hashCode}{ResUI.MsgSubscriptionDecodingFailed}");
            return false;
        }

        await updateFunc?.Invoke(false, $"{hashCode}{ResUI.MsgGetSubscriptionSuccessfully}");

        await updateFunc?.Invoke(false, $"{hashCode}{ResUI.MsgStartParsingSubscription}");

        var originalProfiles = await AppManager.Instance.ProfileItems(id) ?? [];
        var originalIndexId = config.IndexId;
        int ret;
        try
        {
            ret = await ConfigHandler.AddBatchServers(config, result, id, true);
        }
        catch
        {
            await RestoreProfilesAsync(config, id, originalProfiles, originalIndexId);
            Logging.SaveLog("Subscription parsing failed.");
            await updateFunc?.Invoke(false, $"{hashCode}{ResUI.MsgFailedImportSubscription}");
            return false;
        }

        if (ret <= 0)
        {
            await RestoreProfilesAsync(config, id, originalProfiles, originalIndexId);
            Logging.SaveLog("Subscription import failed.");
        }

        await updateFunc?.Invoke(false, ret > 0
                ? $"{hashCode}{ResUI.MsgUpdateSubscriptionEnd}"
                : $"{hashCode}{ResUI.MsgFailedImportSubscription}");

        return ret > 0;
    }

    private static async Task RestoreProfilesAsync(
        Config config,
        string subscriptionId,
        IReadOnlyCollection<ProfileItem> originalProfiles,
        string originalIndexId)
    {
        await ConfigHandler.RemoveServersViaSubid(config, subscriptionId, true);
        foreach (var profile in originalProfiles)
        {
            await SQLiteHelper.Instance.ReplaceAsync(profile);
        }

        config.IndexId = originalIndexId;
        await ConfigHandler.SaveConfig(config);
    }
}
