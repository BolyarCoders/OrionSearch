using System.Net.Http.Json;
using System.Text.Json;

namespace OrionSearch.Tests;

public sealed class ArchiveOpenApiTests
{
    [Fact]
    public async Task ArchiveOperationsDescribeContractsPermissionsAndErrorResponses()
    {
        using var factory = new ApiFactory("Development");
        using var client = factory.CreateClient();
        var document = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        var paths = document.GetProperty("paths");
        var operations = new[]
        {
            ("/archives", "post"), ("/archives", "get"), ("/archives/{id}", "get"),
            ("/archives/{id}", "patch"), ("/archives/{id}", "delete"), ("/archives/{id}/sources", "get")
        };
        foreach (var (path, method) in operations)
        {
            var operation = paths.GetProperty(path).GetProperty(method);
            Assert.True(operation.GetProperty("responses").TryGetProperty("401", out _));
            Assert.Contains("Authentication is currently unconfigured", operation.GetProperty("description").GetString());
            Assert.False(string.IsNullOrWhiteSpace(operation.GetProperty("summary").GetString()));
        }
        Assert.True(paths.GetProperty("/archives").GetProperty("post").TryGetProperty("requestBody", out _));
        Assert.True(paths.GetProperty("/archives/{id}").GetProperty("patch").TryGetProperty("requestBody", out _));
        Assert.True(paths.GetProperty("/archives").GetProperty("post").GetProperty("responses").TryGetProperty("201", out _));
        var sourceParameters = paths.GetProperty("/archives/{id}/sources").GetProperty("get").GetProperty("parameters")
            .EnumerateArray().Select(parameter => parameter.GetProperty("name").GetString()).ToArray();
        foreach (var parameter in new[] { "Page", "PageSize", "PublishedFrom", "PublishedTo", "Language", "PublisherDomain", "Sort" })
            Assert.Contains(parameter, sourceParameters, StringComparer.OrdinalIgnoreCase);
        var schemas = document.GetProperty("components").GetProperty("schemas").ToString();
        Assert.DoesNotContain("hasTitle", schemas, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("passwordHash", schemas, StringComparison.OrdinalIgnoreCase);
    }
}
