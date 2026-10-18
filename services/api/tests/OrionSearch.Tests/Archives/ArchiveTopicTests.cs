using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using OrionSearch.Application.Archives;
using OrionSearch.Domain.Content;
using OrionSearch.Tests.Persistence;

namespace OrionSearch.Tests.Archives;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSQL")]
public sealed class ArchiveTopicTests(PostgreSqlFixture database)
{
    [Fact]
    public async Task ArchiveTopicMatchesTitleOrDescriptionBeforeCountingAndPaging()
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        using var client = scenario.Client();
        var title = await CreateAsync(client, "Artificial Intelligence", null);
        var description = await CreateAsync(client, "Research notes", "Updates on artificial intelligence");
        await CreateAsync(client, "Unrelated", null);
        using var outsider = scenario.Client("nonmember");
        await CreateAsync(outsider, "Artificial Intelligence private", null);
        const string path = "/archives?topic=%20ARTIFICIAL%20intelligence%20&pageSize=1";
        var first = (await client.GetFromJsonAsync<Page<ArchiveSummary>>(path))!;
        var second = (await client.GetFromJsonAsync<Page<ArchiveSummary>>(path + "&page=2"))!;
        var repeat = (await client.GetFromJsonAsync<Page<ArchiveSummary>>(path))!;
        Assert.Equal(2, first.TotalCount);
        Assert.Equal(2, second.TotalCount);
        Assert.Single(first.Items);
        Assert.Single(second.Items);
        Assert.Equal(first.Items, repeat.Items);
        Assert.Equal(new[] { title, description }.Order(), first.Items.Concat(second.Items).Select(x => x.Id).Order());
        var none = (await client.GetFromJsonAsync<Page<ArchiveSummary>>("/archives?topic=missing"))!;
        Assert.Equal(0, none.TotalCount);
        Assert.Empty(none.Items);
        using var viewer = scenario.Client("viewer");
        Assert.Empty((await viewer.GetFromJsonAsync<Page<ArchiveSummary>>(path))!.Items);
    }

    [Fact]
    public async Task TopicCombinesWithStatusAndArchivedVisibility()
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        using var client = scenario.Client();
        var active = await CreateAsync(client, "Climate active", null);
        var paused = await CreateAsync(client, "Climate paused", null);
        var archived = await CreateAsync(client, "Climate archived", null);
        using var pause = await client.PatchAsJsonAsync($"/archives/{paused}", new { status = "paused" });
        Assert.Equal(HttpStatusCode.OK, pause.StatusCode);
        using var archive = await client.DeleteAsync($"/archives/{archived}");
        Assert.Equal(HttpStatusCode.NoContent, archive.StatusCode);
        foreach (var (query, expected) in new[]
        {
            ("", new[] { active, paused }), ("&includeArchived=true", new[] { active, paused, archived }),
            ("&status=paused", new[] { paused }), ("&status=archived", new[] { archived }),
            ("&status=active&includeArchived=true", new[] { active })
        })
        {
            var page = (await client.GetFromJsonAsync<Page<ArchiveSummary>>("/archives?topic=CLIMATE" + query))!;
            Assert.Equal(expected.Length, page.TotalCount);
            Assert.Equal(expected.Order(), page.Items.Select(x => x.Id).Order());
        }
    }

    [Theory]
    [InlineData("%")]
    [InlineData("_")]
    [InlineData("\\")]
    [InlineData("!")]
    [InlineData("%' OR TRUE --")]
    public async Task TopicTreatsSqlMetacharactersAsLiteralText(string topic)
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        using var client = scenario.Client();
        var expected = await CreateAsync(client, "Literal " + topic, null);
        await CreateAsync(client, "Wildcard decoy", null);
        await using var context = scenario.Context();
        var document = new Document { CanonicalUrl = "https://example.com/literal", Title = "Literal " + topic };
        var decoy = new Document { CanonicalUrl = "https://example.com/decoy", Title = "Wildcard decoy" };
        context.AddRange(document, decoy);
        await context.SaveChangesAsync();
        context.ArchiveDocuments.AddRange(new ArchiveDocument { ArchiveId = expected, DocumentId = document.Id },
            new ArchiveDocument { ArchiveId = expected, DocumentId = decoy.Id });
        await context.SaveChangesAsync();
        var query = "?topic=" + Uri.EscapeDataString(topic);
        var archives = (await client.GetFromJsonAsync<Page<ArchiveSummary>>("/archives" + query))!;
        Assert.Equal(1, archives.TotalCount);
        Assert.Equal(expected, Assert.Single(archives.Items).Id);
        var sources = (await client.GetFromJsonAsync<Page<SourceSummary>>($"/archives/{expected}/sources" + query))!;
        Assert.Equal(1, sources.TotalCount);
        Assert.Equal(document.Id, Assert.Single(sources.Items).DocumentId);
    }

    [Fact]
    public async Task SourceTopicCombinesWithDatesLanguageDomainSortAndMembership()
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        using var client = scenario.Client();
        using var outsider = scenario.Client("nonmember");
        var otherArchive = await CreateAsync(outsider, "Private climate", null);
        await using var context = scenario.Context();
        var publisher = new Publisher { Domain = "example.com", Name = "Example" };
        context.Add(publisher);
        await context.SaveChangesAsync();
        var titles = new string?[] { "Climate A", "CLIMATE B", "Climate too old", "Climate undated", "Unrelated", null, "Climate private" };
        var documents = titles.Select((title, index) => new Document
        {
            CanonicalUrl = $"https://example.com/{index}",
            Title = title,
            Language = "en",
            PublisherId = publisher.Id,
            PublishedAt = index is 3 or 5 ? null : new DateTime(2026, index == 2 ? 4 : 5, 10, 0, 0, 0, DateTimeKind.Utc)
        }).ToArray();
        context.AddRange(documents);
        await context.SaveChangesAsync();
        foreach (var (document, index) in documents.Select((document, index) => (document, index)))
            context.Add(new ArchiveDocument { ArchiveId = index == 6 ? otherArchive : scenario.ArchiveId, DocumentId = document.Id });
        await context.SaveChangesAsync();
        var path = $"/archives/{scenario.ArchiveId}/sources?topic=%20cLiMaTe%20&publishedFrom=2026-05-01&publishedTo=2026-05-31&language=EN&publisherDomain=EXAMPLE.COM.&sort=title_asc&pageSize=1";
        var first = (await client.GetFromJsonAsync<Page<SourceSummary>>(path))!;
        var second = (await client.GetFromJsonAsync<Page<SourceSummary>>(path + "&page=2"))!;
        var repeat = (await client.GetFromJsonAsync<Page<SourceSummary>>(path))!;
        Assert.Equal(2, first.TotalCount);
        Assert.Equal(2, second.TotalCount);
        Assert.Equal(first.Items, repeat.Items);
        Assert.Equal(documents.Take(2).Select(x => x.Id).Order(), first.Items.Concat(second.Items).Select(x => x.DocumentId).Order());
        Assert.Empty((await client.GetFromJsonAsync<Page<SourceSummary>>($"/archives/{scenario.ArchiveId}/sources?topic=absent"))!.Items);
        using var viewer = scenario.Client("viewer");
        Assert.Equal(2, (await viewer.GetFromJsonAsync<Page<SourceSummary>>(path))!.TotalCount);
        using var denied = await outsider.GetAsync(path);
        await ArchiveApiTests.AssertProblemAsync(denied, HttpStatusCode.NotFound);
        using var reverse = await client.GetAsync($"/archives/{otherArchive}/sources?topic=climate");
        await ArchiveApiTests.AssertProblemAsync(reverse, HttpStatusCode.NotFound);
        using var hidden = await outsider.GetAsync($"/archives/{scenario.ArchiveId}/sources?topic=%20");
        await ArchiveApiTests.AssertProblemAsync(hidden, HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\u0001")]
    [InlineData("\0")]
    [InlineData("oversized")]
    public async Task InvalidTopicsReturnValidationProblemsOnBothRoutes(string topic)
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        using var client = scenario.Client();
        if (topic == "oversized") topic = new string('x', 201);
        foreach (var path in new[] { "/archives", $"/archives/{scenario.ArchiveId}/sources" })
        {
            using var response = await client.GetAsync(path + "?topic=" + Uri.EscapeDataString(topic));
            await ArchiveApiTests.AssertProblemAsync(response, HttpStatusCode.BadRequest);
            Assert.Contains("topic", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task MaximumLengthTopicIsAcceptedAfterTrimming()
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        using var client = scenario.Client();
        var topic = new string('x', 200);
        var expected = await CreateAsync(client, topic, null);
        var result = (await client.GetFromJsonAsync<Page<ArchiveSummary>>("/archives?topic=" + Uri.EscapeDataString(" " + topic + " ")))!;
        Assert.Equal(expected, Assert.Single(result.Items).Id);
    }

    private static async Task<Guid> CreateAsync(HttpClient client, string title, string? description)
    {
        using var response = await client.PostAsJsonAsync("/archives", new { title, description });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ArchiveDetails>())!.Archive.Id;
    }
}
