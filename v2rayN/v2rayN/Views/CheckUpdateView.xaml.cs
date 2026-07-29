namespace v2rayN.Views;

public partial class CheckUpdateView
{
    public CheckUpdateView()
    {
        InitializeComponent();

        this.WhenActivated(disposables =>
        {
            // Quiet Control Center owns its UI distribution channel.  The upstream
            // v2rayN self-replacement item must never be reachable from this view:
            // deselecting prevents command execution, removal prevents reselection.
            RemoveOfficialGuiUpdate(ViewModel);
            this.OneWayBind(ViewModel, vm => vm.CheckUpdateModels, v => v.lstCheckUpdates.ItemsSource).DisposeWith(disposables);

            this.Bind(ViewModel, vm => vm.EnableCheckPreReleaseUpdate, v => v.togEnableCheckPreReleaseUpdate.IsChecked).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.CheckOnlyCmd, v => v.btnCheckOnly).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.CheckUpdateCmd, v => v.btnCheckUpdate).DisposeWith(disposables);
        });
    }

    public static void RemoveOfficialGuiUpdate(CheckUpdateViewModel? viewModel)
    {
        if (viewModel is null)
        {
            return;
        }

        foreach (var item in viewModel.CheckUpdateModels
                     .Where(item => item.CoreType == ECoreType.v2rayN)
                     .ToList())
        {
            item.IsSelected = false;
            viewModel.CheckUpdateModels.Remove(item);
        }
    }
}
