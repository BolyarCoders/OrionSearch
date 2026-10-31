import { describe, it, expect, vi, beforeEach } from "vitest";
import { DemoApiClient } from "@/lib/api/demo-client";
import { LiveApiClient, ApiAuthError, ApiError } from "@/lib/api/client";

describe("DemoApiClient Implementation", () => {
  let client: DemoApiClient;

  beforeEach(() => {
    client = new DemoApiClient();
  });

  it("lists all non-archived archives by default", async () => {
    const res = await client.listArchives();
    expect(res.items.length).toBeGreaterThanOrEqual(5);
    expect(res.items.every((a) => a.status !== "archived")).toBe(true);
  });

  it("includes archived archives when requested", async () => {
    const res = await client.listArchives({ includeArchived: true });
    const hasArchived = res.items.some((a) => a.status === "archived");
    expect(hasArchived).toBe(true);
  });

  it("filters archives by topic substring", async () => {
    const res = await client.listArchives({ topic: "quantum" });
    expect(res.items.length).toBe(1);
    expect(res.items[0].title).toBe("Quantum Computing");
  });

  it("retrieves archive details by ID", async () => {
    const res = await client.getArchive("11111111-1111-4111-8111-111111111111");
    expect(res.archive.title).toBe("Artificial Intelligence");
    expect(res.settings.languages).toContain("en");
  });

  it("throws 404 for nonexistent archive ID", async () => {
    await expect(client.getArchive("nonexistent-uuid")).rejects.toThrow("not found");
  });

  it("creates a new archive and adds it to list", async () => {
    const created = await client.createArchive({
      title: "Neuromorphic Chips",
      description: "Brain-inspired silicon architectures.",
      settings: {
        languages: ["en"],
        preferredDomains: ["ieee.org"],
      },
    });

    expect(created.archive.id).toBeDefined();
    expect(created.archive.title).toBe("Neuromorphic Chips");
    expect(created.archive.sourceCount).toBe(0);

    const fetched = await client.getArchive(created.archive.id);
    expect(fetched.archive.title).toBe("Neuromorphic Chips");
  });

  it("patches archive metadata and settings", async () => {
    const updated = await client.patchArchive("11111111-1111-4111-8111-111111111111", {
      description: "Updated AI focus description",
    });

    expect(updated.archive.description).toBe("Updated AI focus description");
  });

  it("soft archives via deleteArchive", async () => {
    await client.deleteArchive("11111111-1111-4111-8111-111111111111");
    const item = await client.getArchive("11111111-1111-4111-8111-111111111111");
    expect(item.archive.status).toBe("archived");
  });

  it("lists sources with topic search", async () => {
    const res = await client.listSources("11111111-1111-4111-8111-111111111111", {
      topic: "Autonomous",
    });
    expect(res.items.length).toBeGreaterThan(0);
    expect(res.items[0].title).toContain("Autonomous");
  });

  it("submits research job and returns queued status", async () => {
    const status = await client.submitResearchJob(
      "11111111-1111-4111-8111-111111111111",
      { objective: "Test objective" }
    );

    expect(status.id).toBeDefined();
    expect(status.status).toBe("queued");
    expect(status.archiveId).toBe("11111111-1111-4111-8111-111111111111");
  });
});

describe("LiveApiClient Error Handling", () => {
  it("throws ApiAuthError on 401 response", async () => {
    const liveClient = new LiveApiClient("http://localhost:5190");

    global.fetch = vi.fn().mockResolvedValue({
      status: 401,
      ok: false,
      headers: { get: () => "application/problem+json" },
    } as unknown as Response);

    await expect(liveClient.listArchives()).rejects.toThrow(ApiAuthError);
  });

  it("parses ProblemDetails on 400 response", async () => {
    const liveClient = new LiveApiClient("http://localhost:5190");

    global.fetch = vi.fn().mockResolvedValue({
      status: 400,
      ok: false,
      headers: { get: () => "application/problem+json" },
      json: async () => ({
        title: "Topic must contain 1–200 characters",
        status: 400,
        errors: { topic: ["Topic is required"] },
      }),
    } as unknown as Response);

    try {
      await liveClient.createArchive({ title: "" });
      expect.fail("Should have thrown ApiError");
    } catch (err: unknown) {
      expect(err).toBeInstanceOf(ApiError);
      expect((err as ApiError).status).toBe(400);
      expect((err as ApiError).message).toContain("1–200 characters");
    }
  });
});
