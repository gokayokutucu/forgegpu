using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace ForgeGPU.IntegrationTests;

[CollectionDefinition(nameof(IntegrationTestCollection))]
public sealed class IntegrationTestCollection : ICollectionFixture<RuntimeDependenciesFixture>
{
}
