using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using OrionSearch.Application.Research;

namespace OrionSearch.Infrastructure.Research;

public sealed class HttpResearchAgentClient(HttpClient client, ResearchWorkerOptions options) : IResearchAgentClient
{
    public bool IsConfigured => options.AllowProvisionalContract && options.AgentApiKey is { Length: > 0 and <= 4096 } key
        && key.All(character => character is >= '!' and <= '~')
        && options.AgentBaseAddress is { } address && ResearchWorkerOptions.ValidAddress(address);

    public async Task<AgentResult> ExecuteAsync(AgentRequest request, CancellationToken ct)
    {
        if (!IsConfigured) throw new ResearchAgentException(ResearchFailure.Authentication);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(options.AgentBaseAddress!.TrimEnd('/') + "/"), "research"));
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.AgentApiKey);
            message.Content = JsonContent.Create(request, options: ResearchContract.Json);
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
                throw new ResearchAgentException(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                    ? ResearchFailure.Authentication : (int)response.StatusCode >= 500 || response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
                        ? ResearchFailure.Unavailable : ResearchFailure.Contract);
            if (response.Content.Headers.ContentLength > options.MaxResponseBytes) throw new ResearchAgentException(ResearchFailure.Contract);
            await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var bytes = new MemoryStream();
            var buffer = new byte[16384];
            while (true)
            {
                var count = await body.ReadAsync(buffer, timeout.Token);
                if (count == 0) break;
                if (bytes.Length + count > options.MaxResponseBytes) throw new ResearchAgentException(ResearchFailure.Contract);
                bytes.Write(buffer, 0, count);
            }
            bytes.Position = 0;
            return await JsonSerializer.DeserializeAsync<AgentResult>(bytes, ResearchContract.Json, timeout.Token)
                ?? throw new ResearchAgentException(ResearchFailure.Contract);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new ResearchAgentException(ResearchFailure.Timeout); }
        catch (HttpRequestException) { throw new ResearchAgentException(ResearchFailure.Unavailable); }
        catch (IOException) { throw new ResearchAgentException(ResearchFailure.Unavailable); }
        catch (JsonException) { throw new ResearchAgentException(ResearchFailure.Contract); }
    }
}
