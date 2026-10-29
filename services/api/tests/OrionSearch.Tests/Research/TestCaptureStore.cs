using System.Text;
using Microsoft.Extensions.DependencyInjection;
using OrionSearch.Application.Content;
using OrionSearch.Application.Research;
using OrionSearch.Domain.Content;

namespace OrionSearch.Tests.Research;

internal sealed class TestCaptureStore(Dictionary<string, byte[]> objects) : ICaptureStore
{
    public Task<CaptureBytes> ReadAsync(string storageUri, string sha256, Guid? runId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!objects.TryGetValue(storageUri, out var bytes) || ContentFingerprint.ComputeRaw(bytes) != sha256)
            throw new ResearchAgentException(ResearchFailure.Contract);
        return Task.FromResult(new CaptureBytes(bytes, "text/html"));
    }

    public static void RegisterLegacyFixtures(IServiceCollection services) => services.AddSingleton<ICaptureStore>(new TestCaptureStore(new()
    {
        ["urn:orion:test:durable-raw"] = Encoding.UTF8.GetBytes("preserved raw bytes"),
        ["urn:orion:test:raw-html"] = Encoding.UTF8.GetBytes("<html>Research preserves evidence.</html>"),
        ["urn:orion:test:original-raw"] = Encoding.UTF8.GetBytes("Research preserves evidence."),
        ["urn:orion:test:html"] = Encoding.UTF8.GetBytes("<html>Research preserves evidence.</html>")
    }));
}
