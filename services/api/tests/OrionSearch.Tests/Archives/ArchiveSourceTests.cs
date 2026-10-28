using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using OrionSearch.Application.Archives;
using OrionSearch.Domain.Content;
using OrionSearch.Tests.Persistence;

namespace OrionSearch.Tests.Archives;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSQL")]
public sealed class ArchiveSourceTests(PostgreSqlFixture database)
{
    [Fact]
    public async Task SourcesProjectOnlyArchiveLinksIncludingSharedAndDiscoveredDocuments()
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        var documents = await SeedAsync(scenario);
        using var other = scenario.Client("nonmember");
        using var created = await other.PostAsJsonAsync("/archives", new { title = "Other user's archive" });
        var otherId = (await created.Content.ReadFromJsonAsync<ArchiveDetails>())!.Archive.Id;
        await using var context = scenario.Context();
        context.ArchiveDocuments.Add(new ArchiveDocument { ArchiveId = otherId, DocumentId = documents[0] });
        await context.SaveChangesAsync();
        foreach (var role in new[] { "owner", "editor", "viewer" })
        {
            using var client = scenario.Client(role);
            var page = (await client.GetFromJsonAsync<Page<SourceSummary>>($"/archives/{scenario.ArchiveId}/sources"))!;
            Assert.Equal(3, page.TotalCount);
            Assert.Equal(documents.Order(), page.Items.Select(x => x.DocumentId).Order());
            var discovered = Assert.Single(page.Items, x => x.PublishedAt is null);
            Assert.Null(discovered.PublisherName);
            Assert.Null(discovered.PublisherDomain);
            Assert.True(discovered.FirstSeenAt > DateTime.UnixEpoch);
            Assert.True(discovered.LastSeenAt >= discovered.FirstSeenAt);
        }
        var shared = (await other.GetFromJsonAsync<Page<SourceSummary>>($"/archives/{otherId}/sources"))!;
        Assert.Equal(documents[0], Assert.Single(shared.Items).DocumentId);
        using var denied = await other.GetAsync($"/archives/{scenario.ArchiveId}/sources");
        await ArchiveApiTests.AssertProblemAsync(denied, HttpStatusCode.NotFound);
        using var owner = scenario.Client();
        using var reverseDenied = await owner.GetAsync($"/archives/{otherId}/sources");
        await ArchiveApiTests.AssertProblemAsync(reverseDenied, HttpStatusCode.NotFound);
        using var missing = await owner.GetAsync($"/archives/{Guid.NewGuid()}/sources");
        await ArchiveApiTests.AssertProblemAsync(missing, HttpStatusCode.NotFound);
        Assert.Empty(await context.DocumentVersions.ToListAsync());
    }

    [Theory]
    [InlineData("language=BG", 1)]
    [InlineData("publisherDomain=EXAMPLE.COM.", 2)]
    [InlineData("publishedFrom=2026-05-10&publishedTo=2026-05-10", 1)]
    [InlineData("publishedFrom=2026-05-11", 1)]
    [InlineData("publishedTo=2026-05-09", 0)]
    [InlineData("publishedTo=9999-12-31", 2)]
    [InlineData("publishedFrom=2026-05-01&language=en&publisherDomain=example.com", 1)]
    public async Task FiltersUseActualPublicationDatesAndNormalizedLanguageAndDomain(string query, int count)
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        await SeedAsync(scenario);
        using var client = scenario.Client();
        using var response = await client.GetAsync($"/archives/{scenario.ArchiveId}/sources?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = (await response.Content.ReadFromJsonAsync<Page<SourceSummary>>())!;
        Assert.Equal(count, page.TotalCount);
        Assert.Equal(count, page.Items.Count);
        if (query.Contains("published", StringComparison.Ordinal)) Assert.All(page.Items, source => Assert.NotNull(source.PublishedAt));
    }

    [Theory]
    [InlineData("added_desc")]
    [InlineData("added_asc")]
    [InlineData("published_desc")]
    [InlineData("published_asc")]
    [InlineData("relevance_desc")]
    [InlineData("title_asc")]
    public async Task SortingAndPaginationAreStableWithNullsLast(string sort)
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        await SeedAsync(scenario);
        using var client = scenario.Client();
        var path = $"/archives/{scenario.ArchiveId}/sources?sort={sort}&pageSize=2";
        var first = (await client.GetFromJsonAsync<Page<SourceSummary>>(path))!;
        var repeat = (await client.GetFromJsonAsync<Page<SourceSummary>>(path))!;
        var second = (await client.GetFromJsonAsync<Page<SourceSummary>>(path + "&page=2"))!;
        Assert.Equal(first.Items.Select(x => x.DocumentId), repeat.Items.Select(x => x.DocumentId));
        Assert.Equal(3, first.TotalCount);
        Assert.Equal(2, first.Items.Count);
        Assert.Single(second.Items);
        var items = first.Items.Concat(second.Items).ToArray();
        Assert.Equal(3, items.Select(x => x.DocumentId).Distinct().Count());
        if (sort.StartsWith("published", StringComparison.Ordinal)) Assert.Null(items[^1].PublishedAt);
        if (sort == "relevance_desc") Assert.Null(items[^1].RelevanceScore);
        if (sort == "title_asc") Assert.Null(items[^1].Title);
        if (sort == "published_asc") Assert.True(items[0].PublishedAt < items[1].PublishedAt);
        if (sort == "published_desc") Assert.True(items[0].PublishedAt > items[1].PublishedAt);
        if (sort.StartsWith("added", StringComparison.Ordinal)) Assert.Equal(items.Select(x => x.DocumentId).Order(), items.Select(x => x.DocumentId));
    }

    [Theory]
    [InlineData("/archives?page=0")]
    [InlineData("/archives?pageSize=101")]
    [InlineData("/archives?page=2147483647")]
    [InlineData("/archives?status=deleted")]
    [InlineData("/archives?page=invalid")]
    [InlineData("/archives/{id}/sources?pageSize=0")]
    [InlineData("/archives/{id}/sources?publishedFrom=2026-05-21&publishedTo=2026-05-20")]
    [InlineData("/archives/{id}/sources?publishedFrom=invalid")]
    [InlineData("/archives/{id}/sources?language=xx-invalid")]
    [InlineData("/archives/{id}/sources?publisherDomain=https://example.com")]
    [InlineData("/archives/{id}/sources?sort=arbitrary_sql")]
    public async Task InvalidQueriesReturnBadRequestProblem(string path)
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        using var client = scenario.Client();
        using var response = await client.GetAsync(path.Replace("{id}", scenario.ArchiveId.ToString()));
        await ArchiveApiTests.AssertProblemAsync(response, HttpStatusCode.BadRequest);
    }

    private static async Task<Guid[]> SeedAsync(ArchiveApiScenario scenario)
    {
        await using var context = scenario.Context();
        var publisher = new Publisher { Name = "Example", Domain = "example.com" };
        context.Publishers.Add(publisher);
        await context.SaveChangesAsync();
        var documents = new[]
        {
            new Document { CanonicalUrl = "https://example.com/one", Title = "Alpha", PublisherId = publisher.Id,
                Language = "en", PublishedAt = new DateTime(2026, 5, 10, 23, 59, 59, DateTimeKind.Utc) },
            new Document { CanonicalUrl = "https://example.com/two", Title = "Beta", PublisherId = publisher.Id,
                Language = "bg", PublishedAt = new DateTime(2026, 5, 11, 0, 0, 0, DateTimeKind.Utc) },
            new Document { CanonicalUrl = "https://discovered.example/", Language = "en" }
        };
        context.Documents.AddRange(documents);
        context.Documents.Add(new Document { CanonicalUrl = "https://unlinked.example/", Title = "Do not leak" });
        await context.SaveChangesAsync();
        for (var index = 0; index < documents.Length; index++)
            context.ArchiveDocuments.Add(new ArchiveDocument
            {
                ArchiveId = scenario.ArchiveId,
                DocumentId = documents[index].Id,
                RelevanceScore = index == 2 ? null : 0.8m - index * 0.1m,
                AddedAt = new DateTime(2026, 5, 15, 0, 0, 0, DateTimeKind.Utc)
            });
        await context.SaveChangesAsync();
        return documents.Select(x => x.Id).ToArray();
    }
}
