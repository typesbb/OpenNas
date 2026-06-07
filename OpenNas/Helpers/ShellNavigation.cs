namespace OpenNas.Helpers;

public static class ShellNavigation
{
    public static Task PushAsync(Page page)
    {
        if (Shell.Current != null)
            return Shell.Current.Navigation.PushAsync(page);

        var root = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (root?.Navigation != null)
            return root.Navigation.PushAsync(page);

        throw new InvalidOperationException("无法导航：未找到 Shell 或 Navigation。");
    }
}
