using OpenNas.ViewModels;

namespace OpenNas.Views;

public partial class TasksPage : ContentPage
{
    private readonly BackupTaskViewModel _vm;

    public TasksPage(BackupTaskViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = _vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
#if ANDROID
        OpenNas.Platforms.Android.BackupPendingDeleteHelper.TryLaunchDeleteConfirmation();
#endif
        await _vm.RefreshAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _vm.Detach();
    }

    private async void OnAddRuleClicked(object sender, EventArgs e) =>
        await _vm.AddRuleAsync(this);

    private async void OnRuleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not BindableObject bindable
            || bindable.BindingContext is not BackupRuleItemViewModel item)
            return;

        await _vm.EditRuleAsync(this, item);
    }

    private async void OnRuleActionClicked(object? sender, EventArgs e)
    {
        if (sender is not Button button
            || button.BindingContext is not BackupRuleItemViewModel item)
            return;

        await _vm.ToggleRuleActionAsync(this, item);
    }
}
