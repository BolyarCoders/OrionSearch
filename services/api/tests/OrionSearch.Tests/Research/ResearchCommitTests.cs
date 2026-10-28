using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OrionSearch.Tests.Archives;
using OrionSearch.Tests.Persistence;

namespace OrionSearch.Tests.Research;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSQL")]
public sealed class ResearchCommitTests(PostgreSqlFixture database)
{
    [Fact]
    public async Task AcceptedResponseCannotPrecedeTheRunAndJobCommit()
    {
        await using var scenario = await ArchiveApiScenario.CreateAsync(database);
        await using var context = scenario.Context();
        await context.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION public.wait_for_submission_commit() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN PERFORM pg_advisory_xact_lock(881122); RETURN NEW; END $$;
            CREATE TRIGGER wait_for_submission_commit BEFORE INSERT ON system.jobs
            FOR EACH ROW EXECUTE FUNCTION public.wait_for_submission_commit();
            """);
        await using var blocker = new NpgsqlConnection(scenario.ConnectionString);
        await blocker.OpenAsync();
        await using (var command = new NpgsqlCommand("SELECT pg_advisory_lock(881122)", blocker)) await command.ExecuteNonQueryAsync();
        using var client = scenario.Client();
        var submission = client.PostAsJsonAsync($"/archives/{scenario.ArchiveId}/research-jobs", new { });
        try
        {
            var deadline = Stopwatch.StartNew();
            while (true)
            {
                var waiting = await context.Database.SqlQuery<int>($"""
                    SELECT count(*)::integer AS "Value" FROM pg_stat_activity
                    WHERE datname = current_database() AND wait_event = 'advisory'
                    """).SingleAsync();
                if (waiting > 0) break;
                Assert.False(submission.IsCompleted, "Submission should be blocked at the job insert.");
                Assert.True(deadline.Elapsed < TimeSpan.FromSeconds(15), "The insertion did not reach the controlled database barrier.");
                await Task.Delay(10);
            }
            Assert.False(submission.IsCompleted);
            Assert.Empty(await context.ResearchRuns.AsNoTracking().ToListAsync());
            Assert.Empty(await context.Jobs.AsNoTracking().ToListAsync());
        }
        finally
        {
            await using var release = new NpgsqlCommand("SELECT pg_advisory_unlock(881122)", blocker);
            await release.ExecuteNonQueryAsync();
        }
        using var response = await submission;
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Single(await context.ResearchRuns.AsNoTracking().ToListAsync());
        Assert.Single(await context.Jobs.AsNoTracking().ToListAsync());
    }
}
