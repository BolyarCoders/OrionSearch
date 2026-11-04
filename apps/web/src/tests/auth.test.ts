import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { LiveApiClient, tokenStore } from "@/lib/api/client";

const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json" } });
const tokens = (n: string) => ({ tokenType: "Bearer", accessToken: `a-${n}`, expiresIn: 900, refreshToken: `r-${n}` });

beforeEach(() => localStorage.clear());
afterEach(() => vi.unstubAllGlobals());

describe("authentication", () => {
  it("stores tokens on login and sends them as a bearer header", async () => {
    const fetchMock = vi.fn().mockResolvedValueOnce(json(tokens("1"))).mockResolvedValueOnce(json({ items: [], pageNumber: 1, pageSize: 20, totalCount: 0 }));
    vi.stubGlobal("fetch", fetchMock);
    const client = new LiveApiClient("http://api.test");
    await client.login("a@b.co", "pw");
    expect(client.isAuthenticated()).toBe(true);
    await client.listArchives();
    expect(fetchMock.mock.calls[1][1].headers.Authorization).toBe("Bearer a-1");
  });

  it("refreshes once on 401 and retries the request", async () => {
    tokenStore.write(tokens("old"));
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(json({ title: "Unauthorized" }, 401))
      .mockResolvedValueOnce(json(tokens("new")))
      .mockResolvedValueOnce(json({ id: "1", email: "a@b.co", displayName: "a" }));
    vi.stubGlobal("fetch", fetchMock);
    const me = await new LiveApiClient("http://api.test").me();
    expect(me.email).toBe("a@b.co");
    expect(fetchMock.mock.calls[1][0]).toBe("http://api.test/auth/refresh");
    expect(fetchMock.mock.calls[2][1].headers.Authorization).toBe("Bearer a-new");
  });

  it("clears the session when refresh is rejected", async () => {
    tokenStore.write(tokens("old"));
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(json({ title: "Unauthorized" }, 401)));
    await expect(new LiveApiClient("http://api.test").me()).rejects.toMatchObject({ status: 401 });
    expect(tokenStore.read()).toBeNull();
  });
});
