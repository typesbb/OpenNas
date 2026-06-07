using Android.Runtime;
using Java.Lang;
using Java.Lang.Reflect;
using Java.Util.Concurrent;
using OpenNas.Services;

namespace OpenNas.Platforms.Android;

/// <summary>让 WebView 对局域网 NAS 直连，绕过系统 HTTP 代理（Clash/VPN 等）。</summary>
internal static class WebViewProxyConfigurator
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static string? _configuredHost;

    /// <summary>为 NAS 主机配置 WebView 代理绕过规则（幂等，可重复调用）。</summary>
    public static async Task EnsureDirectForNasAsync(string nasBaseUrl, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(nasBaseUrl.TrimEnd('/'), UriKind.Absolute, out var uri))
            return;

        var host = uri.Host;
        if (string.IsNullOrWhiteSpace(host))
            return;

        await Gate.WaitAsync(cancellationToken);
        try
        {
            if (string.Equals(_configuredHost, host, StringComparison.OrdinalIgnoreCase))
                return;

            if (!IsProxyOverrideSupported())
            {
                AppLog.Warn("当前 WebView 不支持 ProxyOverride，无法绕过系统代理");
                return;
            }

            var rules = BuildBypassRules(host);
            if (rules == null)
            {
                AppLog.Warn("WebView 代理绕过规则构建失败");
                return;
            }

            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var executor = Executors.NewSingleThreadExecutor()!;
            var callback = new ProxyRunnable(() => tcs.TrySetResult());
            if (!TrySetProxyOverride(rules, executor.JavaCast<Java.Lang.Object>(), callback))
            {
                AppLog.Warn("WebView SetProxyOverride 调用失败");
                return;
            }

            await tcs.Task.WaitAsync(TimeSpan.FromSeconds(8), cancellationToken);
            _configuredHost = host;
            AppLog.Warn($"WebView 已对 {host} 配置代理绕过（DIRECT）");
        }
        catch (System.Exception ex)
        {
            AppLog.Warn("WebView 代理绕过配置失败", ex);
        }
        finally
        {
            Gate.Release();
        }
    }

    private static bool IsProxyOverrideSupported()
    {
        try
        {
            var featureClass = Class.ForName("androidx.webkit.WebViewFeature");
            var method = featureClass.GetMethod(
                "isFeatureSupported",
                Class.FromType(typeof(Java.Lang.String)));
            var result = method?.Invoke(null, "PROXY_OVERRIDE");
            return result is Java.Lang.Boolean b && b.BooleanValue();
        }
        catch
        {
            return false;
        }
    }

    private static Java.Lang.Object? BuildBypassRules(string host)
    {
        try
        {
            var builderClass = Class.ForName("androidx.webkit.ProxyConfig$Builder");
            var builder = builderClass.GetConstructor()?.NewInstance();
            if (builder == null)
                return null;

            var addBypass = builderClass.GetMethod(
                "addBypassRule",
                Class.FromType(typeof(Java.Lang.String)));

            foreach (var rule in new[]
                     {
                         "<local>",
                         host,
                         $"{host}:*",
                         "192.168.*",
                         "10.*",
                         "172.16.*",
                         "172.17.*",
                         "172.18.*",
                         "172.19.*",
                         "172.2*",
                         "172.30.*",
                         "172.31.*"
                     })
            {
                addBypass?.Invoke(builder, new Java.Lang.String(rule));
            }

            return builderClass.GetMethod("build")?.Invoke(builder);
        }
        catch (System.Exception ex)
        {
            AppLog.Warn("BuildBypassRules 异常", ex);
            return null;
        }
    }

    private static bool TrySetProxyOverride(
        Java.Lang.Object proxyConfig,
        Java.Lang.Object executor,
        Java.Lang.Object callback)
    {
        try
        {
            var controllerClass = Class.ForName("androidx.webkit.ProxyController");
            var instance = controllerClass.GetMethod("getInstance")?.Invoke(null);
            if (instance == null)
                return false;

            var executorClass = Class.ForName("java.util.concurrent.Executor");
            var runnableClass = Class.ForName("java.lang.Runnable");
            var configClass = Class.ForName("androidx.webkit.ProxyConfig");

            var method = controllerClass.GetMethod(
                "setProxyOverride",
                configClass,
                executorClass,
                runnableClass);
            method?.Invoke(instance, proxyConfig, executor, callback);
            return method != null;
        }
        catch (System.Exception ex)
        {
            AppLog.Warn("TrySetProxyOverride 异常", ex);
            return false;
        }
    }

    private sealed class ProxyRunnable(Action action) : Java.Lang.Object, IRunnable
    {
        public void Run() => action();
    }
}
