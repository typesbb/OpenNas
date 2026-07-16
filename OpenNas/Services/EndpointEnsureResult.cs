namespace OpenNas.Services;

/// <summary>EnsureBestEndpointAsync 的结果。</summary>
public enum EndpointEnsureResult
{
    /// <summary>当前已是可达的最优地址。</summary>
    Ready,

    /// <summary>已切换到更合适的地址。</summary>
    Switched,

    /// <summary>探测后内外网均不可达。</summary>
    Unreachable,

    /// <summary>自动切换关闭、仅一个地址、或网络事件处于手动冷却中。</summary>
    Skipped
}
