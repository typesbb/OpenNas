using Xunit;

namespace NSynology.Tests;

/// <summary>实机 NAS 集成测试串行执行，避免并行登录/上传互相干扰。</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class NasIntegrationCollection
{
    public const string Name = "NasIntegration";
}
