using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Elastic.Clients.Elasticsearch;
using Elastic.Transport;
using elasticdump;
using Humanizer;
using IdentityModel.Client;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Text.Json.Serialization;

namespace FunctionalTests.Tests;

public class IntegrationTest
{
    public class MyDoc
    {
        public int Id2 { get; set; }
        public string Text { get; set; }

        [JsonPropertyName("@timestamp")]
        public DateTime Timestamp { get; set; }
    }
    public static async Task PolulateElasticsearchIndice(Uri uri, string username, string password, string indice, int numDocs)
    {
        var settings = new ElasticsearchClientSettings(uri)
            .Authentication(new BasicAuthentication(username, password));
        var client = new ElasticsearchClient(settings);

        var existResponse = await client.Indices.ExistsAsync(indice);

        if (existResponse.Exists)
        {
            await client.Indices.DeleteAsync(indice);
        }

        await client.Indices.CreateAsync(indice);

        for (int i = 0; i < numDocs; i++)
        {
            var mydoc = new MyDoc
            {
                Id2 = i,
                Text = $"test {i}",
                Timestamp = DateTime.Now
            };

            var response = await client.IndexAsync(mydoc, index: indice);

            if (!response.IsValidResponse)
            {
                throw new Exception("Could not index document");
            }
        }
    }
    // Instructions:
    // 1. Add a project reference to the target AppHost project, e.g.:
    //
    //    <ItemGroup>
    //        <ProjectReference Include="../MyAspireApp.AppHost/MyAspireApp.AppHost.csproj" />
    //    </ItemGroup>
    //
    // 2. Uncomment the following example test and update 'Projects.MyAspireApp_AppHost' to match your AppHost project:
    //
    [Fact]
    public async Task GetWebResourceRootReturnsOkStatusCode()
    {
        // Arrange

        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>();
        appHost.Services.ConfigureHttpClientDefaults(clientBuilder =>
        {
            clientBuilder.AddStandardResilienceHandler();
        });
        // To output logs to the xUnit.net ITestOutputHelper, consider adding a package from https://www.nuget.org/packages?q=xunit+logging

        await using var app = await appHost.BuildAsync();
        var resourceNotificationService = app.Services.GetRequiredService<ResourceNotificationService>();
        await app.StartAsync();

        // Act
        //var httpClient = app.CreateHttpClient("webfrontend");
        //await resourceNotificationService.WaitForResourceAsync("webfrontend", KnownResourceStates.Running).WaitAsync(TimeSpan.FromSeconds(30));
        //var response = await httpClient.GetAsync("/");
        var httpClient = app.CreateHttpClient("elasticsearch");
        httpClient.SetBasicAuthentication("elastic", "password");

        //await Task.Delay(5000);
        await resourceNotificationService.WaitForResourceAsync("elasticsearch", KnownResourceStates.Running).WaitAsync(TimeSpan.FromSeconds(60));
        await resourceNotificationService.WaitForResourceHealthyAsync("elasticsearch");
        var response = await httpClient.GetAsync("/_cluster/health");
        

        string tempPath = Path.GetTempPath();

        // Generate a random directory name (using GUID for uniqueness)
        string randomDirectoryName = Guid.NewGuid().ToString();

        // Combine the temp path with our random directory name
        string newDirectoryPath = Path.Combine(tempPath, randomDirectoryName);

        // Create the directory
        Directory.CreateDirectory(newDirectoryPath);


        await PolulateElasticsearchIndice(httpClient.BaseAddress, "elastic", "password", "test-index", 100);

        response = await httpClient.GetAsync("/test-index");

        ElasticDump elasticDump = new ElasticDump(httpClient.BaseAddress.AbsoluteUri, "elastic", "password", newDirectoryPath);
        await elasticDump.Dump(
            index: "test-index",
            rowPerRequest: 10000
        );

        Directory.Delete(newDirectoryPath, true);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
