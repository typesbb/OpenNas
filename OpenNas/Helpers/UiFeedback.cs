using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;

namespace OpenNas.Helpers;

public static class UiFeedback
{
    public static async Task ToastAsync(string message, ToastDuration duration = ToastDuration.Short)
    {
        var toast = Toast.Make(message, duration);
        await toast.Show();
    }

    public static Task<bool> ConfirmAsync(Page page, string title, string message, string accept = "确定", string cancel = "取消") =>
        page.DisplayAlert(title, message, accept, cancel);

    public static Task AlertAsync(Page page, string title, string message, string accept = "确定") =>
        page.DisplayAlert(title, message, accept);
}
