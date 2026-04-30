using Xunit;

namespace LzqNet.Extensions.Azure.Tests;

/// <summary>
/// 使用 Azurite 夹具的测试集合
/// </summary>
[CollectionDefinition("Azurite")]
public class AzuriteCollection : ICollectionFixture<AzuriteFixture>
{
}