import { describe, it, expect, vi, beforeEach } from "vitest";
import { LiveApiClient, ApiAuthError, ApiError } from "@/lib/api/client";

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
