using OpenNas.Core.Data;
using OpenNas.Services;
using OpenNas.ViewModels;

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
        await _vm.LoadInitialAsync();
    }

    private async void OnEntrySelected(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is CollectionView cv) cv.SelectedItem = null;
        if (e.CurrentSelection.FirstOrDefault() is not LogEntry entry) return;

        if (entry.IsError)
        {
            var detail = entry.ExceptionType != null
                ? $"{entry.Message}\n\n{entry.ExceptionType}\n{entry.StackTrace ?? ""}"
                : entry.Message;
            await DisplayAlertAsync("异常详情", detail, "关闭");
        }
        else
        {
            await DisplayAlertAsync("操作详情", entry.Message, "关闭");
        }
    }

    private async void OnExportClicked(object? sender, EventArgs e)
    {
        var lines = new List<string>();
        foreach (var entry in _vm.Entries)
        {
            var line = entry.IsError
                ? $"[{entry.TimeText}] 异常 | {entry.Message} | {entry.ExceptionType}"
                : $"[{entry.TimeText}] 操作 | {entry.Message}";
            lines.Add(line);
            if (entry.IsError && entry.StackTrace != null)
                lines.Add($"  {entry.StackTrace}");
        }

        if (lines.Count == 0)
        {
            await DisplayAlertAsync("导出", "没有可导出的日志。", "确定");
            return;
        }

        var text = string.Join("\n", lines);
        await Share.RequestAsync(new ShareTextRequest
        {
            Text = text,
            Title = "OpenNas 日志"
        });
    }
}
