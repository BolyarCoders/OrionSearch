using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OrionSearch.Application.Archives;
using OrionSearch.Domain.Auth;
using OrionSearch.Domain.Content;
using OrionSearch.Domain.Jobs;
using OrionSearch.Domain.Knowledge;
using OrionSearch.Domain.Research;
using OrionSearch.Tests.Persistence;

namespace OrionSearch.Tests.Archives;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSQL")]
public sealed class ArchiveApiTests(PostgreSqlFixture database)
{
    [Fact]
    public async Task CreatePersistsSettingsAndTrustedOwnerWithSafeDefaults()
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        using var client = scenario.Client("nonmember");
        client.DefaultRequestHeaders.Add("X-User-Id", scenario.Users["owner"].ToString());
        using var response = await client.PostAsJsonAsync("/archives", new { title = "  New research  " });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<ArchiveDetails>())!;
        Assert.Equal($"/archives/{result.Archive.Id}", response.Headers.Location?.OriginalString);
        Assert.Equal("New research", result.Archive.Title);
        Assert.Equal("owner", result.Archive.Role);
        Assert.Empty(result.Settings.Languages);
        Assert.Equal(JsonValueKind.Object, result.Settings.Config.ValueKind);
        await using var context = scenario.Context();
        var archive = await context.Archives.SingleAsync(x => x.Id == result.Archive.Id);
        Assert.Equal(scenario.Users["nonmember"], archive.OwnerId);
        Assert.True(archive.CreatedAt > DateTime.UnixEpoch);
        Assert.Equal(DateTimeKind.Utc, archive.UpdatedAt.Kind);
        var membership = await context.ArchiveMembers.SingleAsync(x => x.ArchiveId == archive.Id);
        Assert.Equal(archive.OwnerId, membership.UserId);
        Assert.Equal("owner", membership.Role);
        Assert.True(await context.ArchiveSettings.AnyAsync(x => x.ArchiveId == archive.Id));
        Assert.Equal(5, await context.Users.CountAsync());
    }

    [Theory]
    [InlineData("{\"title\":\"\"}")]
    [InlineData("{\"title\":null}")]
    [InlineData("{\"title\":\"New\",\"ownerId\":\"00000000-0000-0000-0000-000000000001\"}")]
    [InlineData("{\"title\":\"New\",\"settings\":null}")]
    [InlineData("{\"title\":\"New\",\"settings\":{\"dateFrom\":\"2026-05-21\",\"dateTo\":\"2026-05-20\"}}")]
    [InlineData("{\"title\":\"New\",\"settings\":{\"languages\":[\"en\",\"EN\"]}}")]
    [InlineData("{\"title\":\"New\",\"settings\":{\"languages\":[\"not-a-language\"]}}")]
    [InlineData("{\"title\":\"New\",\"settings\":{\"languages\":null}}")]
    [InlineData("{\"title\":\"New\",\"settings\":{\"preferredDomains\":[\"EXAMPLE.COM.\",\"example.com\"]}}")]
    [InlineData("{\"title\":\"New\",\"settings\":{\"preferredDomains\":[\"https://example.com\"]}}")]
    [InlineData("{\"title\":\"New\",\"settings\":{\"preferredDomains\":[\"example.com\"],\"excludedDomains\":[\"EXAMPLE.COM.\"]}}")]
    [InlineData("{\"title\":\"New\",\"settings\":{\"config\":[]}}")]
    [InlineData("{\"title\":\"New\",\"settings\":{\"config\":null}}")]
    [InlineData("{\"title\":\"New\",\"settings\":{\"ownerId\":\"spoof\"}}")]
    [InlineData("{bad json")]
    public async Task InvalidCreationReturnsProblemAndLeavesNoRows(string json)
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        using var client = scenario.Client();
        using var response = await client.PostAsync("/archives", new StringContent(json, Encoding.UTF8, "application/json"));
        await AssertProblemAsync(response, HttpStatusCode.BadRequest);
        await using var context = scenario.Context();
        Assert.Equal(1, await context.Archives.CountAsync());
        Assert.Equal(1, await context.ArchiveSettings.CountAsync());
        Assert.Equal(3, await context.ArchiveMembers.CountAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FailedChildInsertRollsBackArchiveCreation(bool rejectSettings)
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        await using var context = scenario.Context();
        await context.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION public.reject_write() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'injected write failure'; END $$;
            """);
        await context.Database.ExecuteSqlRawAsync(rejectSettings
            ? "CREATE TRIGGER reject_write BEFORE INSERT ON research.archive_settings FOR EACH ROW EXECUTE FUNCTION public.reject_write()"
            : "CREATE TRIGGER reject_write BEFORE INSERT ON auth.archive_members FOR EACH ROW EXECUTE FUNCTION public.reject_write()");
        using var client = scenario.Client();
        using var response = await client.PostAsJsonAsync("/archives", new { title = "Must roll back" });
        await AssertProblemAsync(response, HttpStatusCode.InternalServerError);
        Assert.DoesNotContain("injected write failure", await response.Content.ReadAsStringAsync());
        Assert.Equal(1, await context.Archives.CountAsync());
        Assert.Equal(1, await context.ArchiveSettings.CountAsync());
        Assert.Equal(3, await context.ArchiveMembers.CountAsync());
    }

    [Theory]
    [InlineData("owner", 200)]
    [InlineData("editor", 200)]
    [InlineData("viewer", 200)]
    [InlineData("nonmember", 404)]
    [InlineData(null, 401)]
    [InlineData("missing", 401)]
    [InlineData("invalid", 401)]
    [InlineData("00000000-0000-0000-0000-000000000000", 401)]
    [InlineData("00000000-0000-0000-0000-000000000099", 401)]
    [InlineData("disabled", 401)]
    public async Task DetailsEnforcePersistedIdentityAndMembership(string? role, int status)
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        using var client = scenario.Client(role);
        using var response = await client.GetAsync($"/archives/{scenario.ArchiveId}");
        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        if (status != 200) await AssertProblemAsync(response, (HttpStatusCode)status);
        else
        {
            var result = (await response.Content.ReadFromJsonAsync<ArchiveDetails>())!;
            Assert.Equal(role, result.Archive.Role);
            Assert.Equal(new DateOnly(2026, 5, 20), result.Settings.DateTo);
            var json = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("email", json, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task UnknownArchivesAndInconsistentOwnershipFailClosed()
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        using var client = scenario.Client();
        using var missing = await client.GetAsync($"/archives/{Guid.NewGuid()}");
        await AssertProblemAsync(missing, HttpStatusCode.NotFound);
        await using var context = scenario.Context();
        await context.Archives.Where(x => x.Id == scenario.ArchiveId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.OwnerId, scenario.Users["nonmember"]));
        using var conflict = await client.GetAsync($"/archives/{scenario.ArchiveId}");
        await AssertProblemAsync(conflict, HttpStatusCode.Conflict);
        using var deletion = await client.DeleteAsync($"/archives/{scenario.ArchiveId}");
        await AssertProblemAsync(deletion, HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ListFiltersMembershipStatusAndPaginatesStably()
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        using var owner = scenario.Client();
        using var other = scenario.Client("nonmember");
        using var hidden = await other.PostAsJsonAsync("/archives", new { title = "Private" });
        var ids = new List<Guid> { scenario.ArchiveId };
        for (var index = 0; index < 3; index++)
        {
            using var created = await owner.PostAsJsonAsync("/archives", new { title = $"Page item {index}" });
            ids.Add((await created.Content.ReadFromJsonAsync<ArchiveDetails>())!.Archive.Id);
        }
        using var archived = await owner.DeleteAsync($"/archives/{ids[^1]}");
        var first = (await owner.GetFromJsonAsync<Page<ArchiveSummary>>("/archives?pageSize=2"))!;
        var second = (await owner.GetFromJsonAsync<Page<ArchiveSummary>>("/archives?page=2&pageSize=2"))!;
        Assert.Equal(3, first.TotalCount);
        Assert.Equal(2, first.Items.Count);
        Assert.Single(second.Items);
        Assert.Equal(3, first.Items.Concat(second.Items).Select(x => x.Id).Distinct().Count());
        Assert.All(first.Items.Concat(second.Items), item => Assert.Contains(item.Id, ids));
        Assert.Equal(4, (await owner.GetFromJsonAsync<Page<ArchiveSummary>>("/archives?includeArchived=true"))!.TotalCount);
        Assert.Single((await owner.GetFromJsonAsync<Page<ArchiveSummary>>("/archives?status=archived"))!.Items);
        using var viewer = scenario.Client("viewer");
        Assert.Single((await viewer.GetFromJsonAsync<Page<ArchiveSummary>>("/archives"))!.Items);
    }

    [Theory]
    [InlineData("owner", 200)]
    [InlineData("editor", 200)]
    [InlineData("viewer", 403)]
    [InlineData("nonmember", 404)]
    public async Task EditPermissionsComeFromMembershipNotPrincipalRole(string role, int status)
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        using var client = scenario.Client(role);
        using var response = await scenario.PatchAsync(client, new { title = "Changed", settings = new { languages = new[] { " BG " } } });
        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        await using var context = scenario.Context();
        var archive = await context.Archives.SingleAsync();
        var settings = await context.ArchiveSettings.SingleAsync();
        Assert.Equal(status == 200 ? "Changed" : "Existing archive", archive.Title);
        Assert.Equal(new[] { status == 200 ? "bg" : "en" }, settings.Languages);
        Assert.Equal("Keep this description", archive.Description);
        Assert.Equal(new DateOnly(2026, 5, 20), settings.DateTo);
        Assert.Equal("example.com", Assert.Single(settings.PreferredDomains));
        Assert.True(JsonDocument.Parse(settings.Config).RootElement.GetProperty("keep").GetBoolean());
        Assert.Equal(3, await context.ArchiveMembers.CountAsync());
    }

    [Theory]
    [InlineData("{\"title\":\"Partly changed\",\"settings\":{\"dateFrom\":\"2026-06-01\"}}")]
    [InlineData("{\"description\":\"Partly changed\",\"settings\":{\"excludedDomains\":[\"example.com\"]}}")]
    [InlineData("{\"title\":null}")]
    [InlineData("{\"status\":null}")]
    [InlineData("{\"status\":\"deleted\"}")]
    [InlineData("{\"settings\":null}")]
    [InlineData("{\"settings\":{\"languages\":null}}")]
    [InlineData("{\"settings\":{\"config\":null}}")]
    [InlineData("{\"ownerId\":\"spoof\"}")]
    public async Task InvalidPatchValidatesMergedSettingsAndPersistsNothing(string json)
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        using var client = scenario.Client();
        using var response = await client.PatchAsync($"/archives/{scenario.ArchiveId}", new StringContent(json, Encoding.UTF8, "application/json"));
        await AssertProblemAsync(response, HttpStatusCode.BadRequest);
        await using var context = scenario.Context();
        Assert.Equal("Existing archive", (await context.Archives.SingleAsync()).Title);
        Assert.Equal("Keep this description", (await context.Archives.SingleAsync()).Description);
        Assert.Equal(new DateOnly(2026, 5, 1), (await context.ArchiveSettings.SingleAsync()).DateFrom);
        Assert.Empty((await context.ArchiveSettings.SingleAsync()).ExcludedDomains);
    }

    [Fact]
    public async Task NullClearsNullableFieldsAndConfigReplacementIsExplicit()
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        using var client = scenario.Client();
        using var response = await scenario.PatchAsync(client, new
        {
            description = (string?)null,
            settings = new { dateTo = (DateOnly?)null, config = new { replacement = 5 }, preferredDomains = new[] { "BÜCHER.DE." } }
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<ArchiveDetails>())!;
        Assert.Null(result.Archive.Description);
        Assert.Null(result.Settings.DateTo);
        Assert.Equal(new DateOnly(2026, 5, 1), result.Settings.DateFrom);
        Assert.Equal("xn--bcher-kva.de", Assert.Single(result.Settings.PreferredDomains));
        Assert.False(result.Settings.Config.TryGetProperty("keep", out _));
        Assert.Equal(5, result.Settings.Config.GetProperty("replacement").GetInt32());
    }

    [Fact]
    public async Task SettingsWriteFailureRollsBackMetadataUpdate()
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        await using var context = scenario.Context();
        await context.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION public.reject_update() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'injected settings failure'; END $$;
            CREATE TRIGGER reject_update BEFORE UPDATE ON research.archive_settings
            FOR EACH ROW EXECUTE FUNCTION public.reject_update();
            """);
        using var client = scenario.Client();
        using var response = await scenario.PatchAsync(client, new { title = "Must roll back", settings = new { languages = new[] { "bg" } } });
        await AssertProblemAsync(response, HttpStatusCode.InternalServerError);
        Assert.Equal("Existing archive", (await context.Archives.SingleAsync()).Title);
        Assert.Equal("en", Assert.Single((await context.ArchiveSettings.SingleAsync()).Languages));
    }

    [Theory]
    [InlineData("owner", 204)]
    [InlineData("editor", 403)]
    [InlineData("viewer", 403)]
    [InlineData("nonmember", 404)]
    public async Task OnlyOwnerCanArchiveAndUnarchive(string role, int status)
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        using var client = scenario.Client(role);
        using var response = await client.DeleteAsync($"/archives/{scenario.ArchiveId}");
        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        using var lifecycle = await scenario.PatchAsync(client, new { status = "active" });
        Assert.Equal(status == 204 ? HttpStatusCode.OK : (HttpStatusCode)status, lifecycle.StatusCode);
    }

    [Fact]
    public async Task SoftArchiveIsIdempotentAndRetainsResearchEvidenceAndGlobalContent()
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        await using var context = scenario.Context();
        var version = await TestData.CreateVersionAsync(context);
        var chunk = await TestData.CreateChunkAsync(context, version.Id);
        var finding = new Finding { Statement = "Preserve evidence" };
        var run = new ResearchRun { ArchiveId = scenario.ArchiveId };
        context.AddRange(finding, run);
        await context.SaveChangesAsync();
        context.ArchiveDocuments.Add(new ArchiveDocument { ArchiveId = scenario.ArchiveId, DocumentId = version.DocumentId });
        context.FindingEvidence.Add(new FindingEvidence { FindingId = finding.Id, DocumentVersionId = version.Id, ChunkId = chunk.Id });
        context.Jobs.Add(new Job { JobType = "research_plan", ResearchRunId = run.Id });
        await context.SaveChangesAsync();
        using var client = scenario.Client();
        using var first = await client.DeleteAsync($"/archives/{scenario.ArchiveId}");
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        var timestamp = (await context.Archives.AsNoTracking().SingleAsync()).UpdatedAt;
        using var second = await client.DeleteAsync($"/archives/{scenario.ArchiveId}");
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        var archive = await context.Archives.AsNoTracking().SingleAsync();
        Assert.Equal("archived", archive.Status);
        Assert.Equal(timestamp, archive.UpdatedAt);
        Assert.Equal(3, await context.ArchiveMembers.CountAsync());
        Assert.Single(await context.ArchiveSettings.ToListAsync());
        Assert.Single(await context.ResearchRuns.ToListAsync());
        Assert.Single(await context.Jobs.ToListAsync());
        Assert.Single(await context.ArchiveDocuments.ToListAsync());
        Assert.Single(await context.Documents.ToListAsync());
        Assert.Single(await context.DocumentVersions.ToListAsync());
        Assert.Single(await context.DocumentChunks.ToListAsync());
        Assert.Single(await context.FindingEvidence.ToListAsync());
        var sources = await client.GetFromJsonAsync<Page<SourceSummary>>($"/archives/{scenario.ArchiveId}/sources");
        Assert.Single(sources!.Items);
    }

    [Fact]
    public async Task InputBoundsRejectOversizedOrNullSettingsWithoutPartialCreation()
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        using var client = scenario.Client();
        object[] invalid =
        [
            new { title = new string('x', 201) },
            new { title = "New", description = new string('x', 10001) },
            new { title = "New", settings = new { preferredDomains = (string[]?)null } },
            new { title = "New", settings = new { excludedDomains = new[] { "EXAMPLE.COM", "example.com." } } },
            new { title = "New", settings = new { preferredDomains = new string?[] { null } } },
            new { title = "New", settings = new { languages = Enumerable.Repeat("en", 101).ToArray() } },
            new { title = "New", settings = new { config = new { value = new string('x', 16384) } } }
        ];
        foreach (var body in invalid)
        {
            using var response = await client.PostAsJsonAsync("/archives", body);
            await AssertProblemAsync(response, HttpStatusCode.BadRequest);
        }
        await using var context = scenario.Context();
        Assert.Single(await context.Archives.ToListAsync());
    }

    [Fact]
    public async Task ConcurrentDatePatchesValidateAgainstTheSerializedResult()
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        using var firstClient = scenario.Client();
        using var secondClient = scenario.Client("editor");
        var responses = await Task.WhenAll(
            scenario.PatchAsync(firstClient, new { settings = new { dateFrom = "2026-05-15" } }),
            scenario.PatchAsync(secondClient, new { settings = new { dateTo = "2026-05-10" } }));
        try
        {
            Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.BadRequest }, responses.Select(x => x.StatusCode).Order());
            await using var context = scenario.Context();
            var settings = await context.ArchiveSettings.SingleAsync();
            Assert.True(settings.DateFrom <= settings.DateTo);
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    internal static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((int)status, problem.GetProperty("status").GetInt32());
        Assert.True(problem.TryGetProperty("traceId", out _));
    }
}
