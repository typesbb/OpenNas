namespace OpenNas;

public static class AppServices
{
    public static IServiceProvider Provider { get; private set; } = null!;

    public static void Init(IServiceProvider provider) => Provider = provider;

    public static T GetRequired<T>() where T : notnull =>
        Provider.GetRequiredService<T>();
}
