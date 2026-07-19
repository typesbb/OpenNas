namespace OpenNas.Helpers;

public static class ShellNavigation
{
    public static Task PushAsync(Page page)
    {
        // 二级页不显示底部 TabBar；返回一级页后由页面自身默认值恢复。
        Shell.SetTabBarIsVisible(page, false);

        if (Shell.Current != null)
            return Shell.Current.Navigation.PushAsync(page);

        var root = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (root?.Navigation != null)
            return root.Navigation.PushAsync(page);

        throw new InvalidOperationException("无法导航：未找到 Shell 或 Navigation。");
    }

    /// <summary>
    /// 全屏媒体预览等：用 Modal 盖在 Shell 之上，根本不经过 Tab 内容区，
    /// 避免 TabBarIsVisible=false 在 Android 上仍留底边空白导致画面不居中。
    /// </summary>
    public static Task PushModalAsync(Page page, bool animated = true)
    {
        if (Shell.Current?.Navigation != null)
            return Shell.Current.Navigation.PushModalAsync(page, animated);

        var root = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (root?.Navigation != null)
            return root.Navigation.PushModalAsync(page, animated);

        throw new InvalidOperationException("无法导航：未找到 Shell 或 Navigation。");
    }

    public static Task PopModalAsync(bool animated = true)
    {
        if (Shell.Current?.Navigation != null)
            return Shell.Current.Navigation.PopModalAsync(animated);

        var root = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (root?.Navigation != null)
            return root.Navigation.PopModalAsync(animated);

        throw new InvalidOperationException("无法返回：未找到 Shell 或 Navigation。");
    }
}
