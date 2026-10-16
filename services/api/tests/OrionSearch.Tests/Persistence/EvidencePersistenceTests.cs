using Microsoft.EntityFrameworkCore;
using Npgsql;
using OrionSearch.Domain.Knowledge;

namespace OrionSearch.Tests.Persistence;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSQL")]
public sealed class EvidencePersistenceTests(PostgreSqlFixture database)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EvidenceCanReferenceAVersionWithAnOptionalMatchingChunk(bool includeChunk)
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var version = await TestData.CreateVersionAsync(context);
        var chunk = includeChunk ? await TestData.CreateChunkAsync(context, version.Id) : null;
        var finding = new Finding { Statement = "A sourced observation", FindingType = "observation" };
        context.Findings.Add(finding);
        await context.SaveChangesAsync();
        var evidence = new FindingEvidence
        {
            FindingId = finding.Id,
            DocumentVersionId = version.Id,
            ChunkId = chunk?.Id,
            Stance = "supports"
        };
        context.FindingEvidence.Add(evidence);

        await context.SaveChangesAsync();

        Assert.NotEqual(Guid.Empty, evidence.Id);
    }

    [Fact]
    public async Task PostgreSqlRejectsEvidenceWithAChunkFromAnotherVersion()
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var firstVersion = await TestData.CreateVersionAsync(context);
        var secondVersion = await TestData.CreateVersionAsync(context);
        var chunk = await TestData.CreateChunkAsync(context, firstVersion.Id);
        var finding = new Finding { Statement = "A finding" };
        context.Findings.Add(finding);
        await context.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO knowledge.finding_evidence (finding_id, document_version_id, chunk_id, stance)
            VALUES ({finding.Id}, {secondVersion.Id}, {chunk.Id}, 'supports')
            """));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
        Assert.Equal("fk_finding_evidence_chunk_version", exception.ConstraintName);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EvidenceRequiresExistingFindingsAndVersions(bool missingFinding)
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var version = await TestData.CreateVersionAsync(context);
        var finding = new Finding { Statement = "A finding" };
        context.Findings.Add(finding);
        await context.SaveChangesAsync();
        context.FindingEvidence.Add(new FindingEvidence
        {
            FindingId = missingFinding ? Guid.NewGuid() : finding.Id,
            DocumentVersionId = missingFinding ? version.Id : Guid.NewGuid()
        });

        await TestData.AssertRejectedAsync(() => context.SaveChangesAsync(), PostgresErrorCodes.ForeignKeyViolation);
    }

    [Fact]
    public async Task ReferencedDocumentVersionsCannotBeDeleted()
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var version = await TestData.CreateVersionAsync(context);
        var finding = new Finding { Statement = "A finding" };
        context.Findings.Add(finding);
        await context.SaveChangesAsync();
        context.FindingEvidence.Add(new FindingEvidence { FindingId = finding.Id, DocumentVersionId = version.Id });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            context.DocumentVersions.Where(x => x.Id == version.Id).ExecuteDeleteAsync());

        TestData.AssertRestricted(exception, "FK_finding_evidence_document_versions_document_version_id");
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public async Task ConfidenceOutsideTheUnitIntervalIsRejected(double confidence)
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        context.Findings.Add(new Finding { Statement = "A finding", Confidence = (decimal)confidence });

        await TestData.AssertRejectedAsync(() => context.SaveChangesAsync(), PostgresErrorCodes.CheckViolation, "ck_findings_confidence");
    }

    [Theory]
    [InlineData("status", "ck_findings_status")]
    [InlineData("type", "ck_findings_type")]
    [InlineData("stance", "ck_finding_evidence_stance")]
    public async Task InvalidFindingAndEvidenceCategoriesAreRejected(string field, string constraint)
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var finding = new Finding { Statement = "A finding" };
        context.Findings.Add(finding);
        await context.SaveChangesAsync();
        if (field == "stance")
        {
            var version = await TestData.CreateVersionAsync(context);
            context.FindingEvidence.Add(new FindingEvidence
            {
                FindingId = finding.Id,
                DocumentVersionId = version.Id,
                Stance = "invalid"
            });
        }
        else if (field == "status")
        {
            finding.Status = "invalid";
        }
        else
        {
            finding.FindingType = "invalid";
        }

        await TestData.AssertRejectedAsync(() => context.SaveChangesAsync(), PostgresErrorCodes.CheckViolation, constraint);
    }
}
