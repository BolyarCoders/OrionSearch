import { afterEach, describe, expect, it, vi } from "vitest";
import { LiveApiClient, ApiAuthError } from "@/lib/api/client";
import { normalizeDomain, validateDateWindow } from "@/lib/normalization";
import { parseConfig, sparseSettingsPatch } from "@/lib/archive-validation";
const id = "11111111-1111-4111-8111-111111111111";
afterEach(() => { vi.useRealTimers(); vi.unstubAllGlobals(); localStorage.clear(); });
const details = {
  archive: { id, title: "Test", description: "d", status: "active" as const, role: "owner" as const, createdAt: "", updatedAt: "", sourceCount: 0 },
  settings: { dateFrom: "2026-01-01", dateTo: null, languages: ["en"], preferredDomains: [], excludedDomains: [], config: {}, updatedAt: "" },
};
describe("Documented archive semantics", () => {
  it("sends only changed fields and preserves explicit clears", () => {
    const patch = sparseSettingsPatch({ ...details.archive, settings: details.settings }, { title: details.archive.title, description: null, settings: { ...details.settings, dateFrom: null } });
    expect(patch).toEqual({ description: null, settings: { dateFrom: null } });
  });
});
describe("API validation and transport", () => {
  it("supports IDNA and rejects authority tricks and invalid dates", () => {
    expect(normalizeDomain("BÜCHER.DE.").domain).toBe("xn--bcher-kva.de");
    expect(normalizeDomain("user@example.com").error).toBeTruthy();
    expect(validateDateWindow("2026-02-30", null)).toBeTruthy();
    expect(() => parseConfig("[]")).toThrow();
    expect(() => parseConfig(JSON.stringify({ x: "я".repeat(9000) }))).toThrow("16 KiB");
  });
  it("retains 401 ProblemDetails and its trace ID", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify({ title: "Unauthorized", traceId: "trace-123" }), { status: 401, headers: { "content-type": "application/problem+json" } })));
    try { await new LiveApiClient().listArchives(); expect.fail("Expected 401"); }
    catch (e) { expect(e).toBeInstanceOf(ApiAuthError); expect((e as ApiAuthError).problem?.traceId).toBe("trace-123"); }
  });
  it("sends JSON and the supplied key, and handles 204 without JSON parsing", async () => {
    const fetcher = vi.fn().mockResolvedValueOnce(new Response("{}", { status: 202, headers: { "content-type": "application/json" } })).mockResolvedValueOnce(new Response(null, { status: 204 }));
    vi.stubGlobal("fetch", fetcher); const client = new LiveApiClient("https://api.example/");
    await client.submitResearchJob(id, {}, "stable");
    expect(fetcher.mock.calls[0][1]).toMatchObject({ body: "{}", headers: { "Idempotency-Key": "stable" } });
    await expect(client.deleteArchive(id)).resolves.toBeUndefined();
  });
});

describe("Backend error boundary", () => {
  it.each([400, 403, 404, 409, 500, 503])("preserves status %s and its ProblemDetails without falling back to demo data", async status => {
    const problem = { title: `Request rejected (${status})`, status, traceId: `trace-${status}`, errors: { title: ["Field message"] } };
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify(problem), { status, headers: { "content-type": "application/problem+json" } })));
    await expect(new LiveApiClient().listArchives()).rejects.toMatchObject({ status, problem });
  });
  it("serializes every source filter and sort before paging", async () => {
    const fetcher = vi.fn().mockResolvedValue(new Response('{"items":[]}', { headers: { "content-type": "application/json" } })); vi.stubGlobal("fetch", fetcher);
    await new LiveApiClient("https://api.example").listSources(id, { topic: "%_!", page: 2, pageSize: 100, publishedFrom: "2026-01-01", publishedTo: "2026-10-20", language: "bg", publisherDomain: "example.com", sort: "published_asc" });
    expect(Object.fromEntries(new URL(fetcher.mock.calls[0][0]).searchParams)).toEqual({ topic: "%_!", page: "2", pageSize: "100", publishedFrom: "2026-01-01", publishedTo: "2026-10-20", language: "bg", publisherDomain: "example.com", sort: "published_asc" });
  });
});
