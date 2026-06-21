using OpenNas.Core.Data;
using OpenNas.Services;
using OpenNas.ViewModels;
using Microsoft.Maui.ApplicationModel.DataTransfer;

namespace OpenNas.Views;

public partial class LogPage : ContentPage
{
    private readonly LogPageViewModel _vm;

    public LogPage(LogPageViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = _vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // 让出 UI 线程先完成页面渲染，再开始加载数据
        await Task.Yield();
        await _vm.LoadInitialAsync();
    }

    private async void OnEntrySelected(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is CollectionView cv) cv.SelectedItem = null;
        if (e.CurrentSelection.FirstOrDefault() is not LogEntry entry) return;

        if (entry.IsError)
        {
            await _vm.LoadEntryDetailAsync(entry);
            var detail = entry.ExceptionType != null
                ? $"{entry.Message}\n\n{entry.ExceptionType}\n{entry.StackTrace ?? ""}"
                : entry.Message;
            var copy = await DisplayAlertAsync("异常详情", detail, "复制", "关闭");
            if (copy)
                await Clipboard.Default.SetTextAsync(detail);
        }
        else
        {
            var copy = await DisplayAlertAsync("操作详情", entry.Message, "复制", "关闭");
            if (copy)
                await Clipboard.Default.SetTextAsync(entry.Message);
        }
    }

    private async void OnBackClicked(object? sender, EventArgs e) =>
        await Navigation.PopAsync();

    private async void OnClearClicked(object? sender, EventArgs e)
    {
        var ok = await DisplayAlert("清理日志", "确定清除所有日志记录？", "确定", "取消");
        if (!ok) return;
        await LogRepository.Instance.ClearAllAsync();
        _vm.Entries.Clear();
        await DisplayAlertAsync("清理日志", "日志已清除。", "确定");
    }

}
