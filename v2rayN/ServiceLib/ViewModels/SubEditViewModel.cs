namespace ServiceLib.ViewModels;

public class SubEditViewModel : MyReactiveObject, ICloseable
{
    private readonly bool _wasNew;
    private readonly Func<string, Task<SubscriptionUpdateResult>>? _firstUpdateAsync;
    private int _firstUpdateConsumed;

    public event EventHandler? RequestClose;

    [Reactive]
    public SubItem SelectedSource { get; set; }

    public ReactiveCommand<Unit, Unit> SelectPrevProfileCmd { get; }
    public ReactiveCommand<Unit, Unit> SelectNextProfileCmd { get; }
    public ReactiveCommand<Unit, Unit> SaveCmd { get; }

    public SubEditViewModel(
        SubItem subItem,
        Func<string, Task<SubscriptionUpdateResult>>? firstUpdateAsync = null)
    {
        _config = AppManager.Instance.Config;
        _wasNew = subItem.Id.IsNullOrEmpty();
        _firstUpdateAsync = firstUpdateAsync;

        SelectPrevProfileCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            var profileItem = await SelectProfileAsync();
            if (profileItem != null)
            {
                SelectedSource?.PrevProfile = profileItem.Remarks;
                SelectedSource = JsonUtils.DeepCopy(SelectedSource);
            }
        });
        SelectNextProfileCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            var profileItem = await SelectProfileAsync();
            if (profileItem != null)
            {
                SelectedSource?.NextProfile = profileItem.Remarks;
                SelectedSource = JsonUtils.DeepCopy(SelectedSource);
            }
        });
        SaveCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await SaveSubAsync();
        });

        SelectedSource = subItem.Id.IsNullOrEmpty() ? subItem : JsonUtils.DeepCopy(subItem);
    }

    private async Task SaveSubAsync()
    {
        var remarks = SelectedSource.Remarks;
        if (remarks.IsNullOrEmpty())
        {
            NoticeManager.Instance.Enqueue(ResUI.PleaseFillRemarks);
            return;
        }

        var url = SelectedSource.Url;
        if (url.IsNotEmpty())
        {
            var uri = Utils.TryUri(url);
            if (uri == null)
            {
                NoticeManager.Instance.Enqueue(ResUI.InvalidUrlTip);
                return;
            }
            //Do not allow http protocol
            if (url.StartsWith(Global.HttpProtocol) && !Utils.IsPrivateNetwork(uri.IdnHost))
            {
                NoticeManager.Instance.Enqueue(ResUI.InsecureUrlProtocol);
                //return;
            }
        }

        if (await ConfigHandler.AddSubItem(_config, SelectedSource) == 0)
        {
            NoticeManager.Instance.Enqueue(ResUI.OperationSuccess);
            await RunFirstUpdateAsync();
        }
        else
        {
            NoticeManager.Instance.Enqueue(ResUI.OperationFailed);
        }
    }

    private async Task RunFirstUpdateAsync()
    {
        if (!_wasNew)
        {
            RequestClose?.Invoke(this, EventArgs.Empty);
            return;
        }

        var persistedId = SelectedSource.Id?.Trim() ?? string.Empty;
        SubItem? persistedItem = null;
        if (persistedId.Length > 0)
        {
            try
            {
                persistedItem = await AppManager.Instance.GetSubItem(persistedId);
            }
            catch
            {
                persistedItem = null;
            }
        }

        var shouldUpdate = persistedItem is not null
            && _firstUpdateAsync is not null
            && FirstSubscriptionUpdatePolicy.ShouldUpdate(
                _wasNew,
                Volatile.Read(ref _firstUpdateConsumed) != 0,
                persistedItem.Id,
                persistedItem.Enabled,
                persistedItem.Url)
            && Interlocked.CompareExchange(ref _firstUpdateConsumed, 1, 0) == 0;

        RequestClose?.Invoke(this, EventArgs.Empty);

        if (!shouldUpdate)
        {
            NoticeManager.Instance.Enqueue(FirstSubscriptionUpdatePolicy.SkippedFeedback);
            return;
        }

        SubscriptionUpdateResult result;
        try
        {
            result = await _firstUpdateAsync!(persistedId);
        }
        catch
        {
            result = SubscriptionUpdateResult.Failed;
        }

        NoticeManager.Instance.Enqueue(result.Success
            ? FirstSubscriptionUpdatePolicy.SuccessFeedback
            : FirstSubscriptionUpdatePolicy.FailedFeedback);
    }

    private async Task<ProfileItem?> SelectProfileAsync()
    {
        var profileSelectViewModel = new ProfilesSelectViewModel();
        profileSelectViewModel.SetConfigTypeFilter([EConfigType.Custom], exclude: true);
        var result = await AppManager.Instance.WindowDialog.ShowDialogAsync(profileSelectViewModel);
        if (result != true)
        {
            return null;
        }
        var profileItem = await profileSelectViewModel.GetProfileItem();
        return profileItem;
    }
}
