using LzqNet.Extensions.Azure.Extensions;
using Microsoft.Extensions.Hosting;

namespace LzqNet.Extensions.Azure;

public static class AzureExtensions
{
    public static IHostApplicationBuilder AddLzqAzure(this IHostApplicationBuilder builder)
    {
        // 添加 Azure Blob Storage
        builder.AddLzqAzureBlobStorage()
            .AddLzqAzureTableStorage();


        return builder;
    }
}
