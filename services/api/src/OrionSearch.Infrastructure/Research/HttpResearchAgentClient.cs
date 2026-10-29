using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using OrionSearch.Application.Research;

namespace OrionSearch.Infrastructure.Research;

public sealed class HttpResearchAgentClient(HttpClient client, ResearchWorkerOptions options, TimeProvider? timeProvider = null) : IResearchAgentClient
{
    public bool IsConfigured => options.HasAgentConfiguration();

    public async Task<AgentResult> ExecuteAsync(AgentRequest request, CancellationToken ct)
    {
        if (!IsConfigured) throw new ResearchAgentException(ResearchFailure.Authentication);
        if (!ResearchContract.IsSupported(request.ContractVersion, options.AllowProvisionalContract))
            throw new ResearchAgentException(ResearchFailure.Contract);
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
                        ? ResearchFailure.Unavailable : ResearchFailure.Contract, RetryDelay(response));
            if (!string.Equals(response.Content.Headers.ContentType?.MediaType, "application/json", StringComparison.OrdinalIgnoreCase))
                throw new ResearchAgentException(ResearchFailure.Contract);
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
            var result = await JsonSerializer.DeserializeAsync<AgentResult>(bytes, ResearchContract.Json, timeout.Token);
            return ResearchOutputValidator.Normalize(result, request.ResearchRunId, request.ContractVersion);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new ResearchAgentException(ResearchFailure.Timeout); }
        catch (HttpRequestException) { throw new ResearchAgentException(ResearchFailure.Unavailable); }
        catch (IOException) { throw new ResearchAgentException(ResearchFailure.Unavailable); }
        catch (JsonException) { throw new ResearchAgentException(ResearchFailure.Contract); }
    }

    private int? RetryDelay(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Retry-After", out var values)) return null;
        var headers = values.ToArray();
        if (headers.Length != 1 || !RetryConditionHeaderValue.TryParse(headers[0], out var retry)) return null;
        var delay = retry.Delta ?? retry.Date - (timeProvider ?? TimeProvider.System).GetUtcNow();
        return delay is { TotalSeconds: > 0 } value ? (int)Math.Min(Math.Ceiling(value.TotalSeconds), options.RetryMaxSeconds) : null;
    }
}
