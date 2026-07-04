using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using NSynology;

namespace OpenNas.Helpers;

public static class UiFeedback
{
    public static async Task ToastAsync(string message, ToastDuration duration = ToastDuration.Short)
    {
        var toast = Toast.Make(message, duration);
        await toast.Show();
    }

    public static Task<bool> ConfirmAsync(Page page, string title, string message, string accept = "确定", string cancel = "取消") =>
        page.DisplayAlertAsync(title, message, accept, cancel);

    public static Task AlertAsync(Page page, string title, string message, string accept = "确定") =>
        page.DisplayAlertAsync(title, message, accept);

    /// <summary>展示 NAS API 错误；会话失效（106/107）由 API 层统一跳转登录，此处不再弹窗。</summary>
    public static async Task ShowApiErrorAsync(Page? page, string title, Exception ex, string? message = null)
    {
        if (SynologyManager.ShouldSuppressApiErrorUi(ex))
            return;

        if (page == null)
            return;

        await page.DisplayAlertAsync(title, message ?? ex.Message, "确定");
    }
}
