import {
  ArchiveDetails,
  CapturedVersion,
  CapturedVersionSummary,
  FindingDetails,
  FindingSummary,
  ListFindingsParams,
  ListRunsParams,
  PageParams,
  ResearchReport,
  SourceDetails,
  TokenResponse,
  UserProfile,
  ArchiveSummary,
  CreateArchiveRequest,
  ListArchivesParams,
  ListSourcesParams,
  PagedResult,
  PatchArchiveRequest,
  ProblemDetails,
  ResearchRunStatus,
  SourceSummary,
  SubmitResearchJobRequest,
} from "@/types/api";

export class ApiError extends Error {
  status: number;
  problem?: ProblemDetails;

  constructor(status: number, message: string, problem?: ProblemDetails) {
    super(message);
    this.name = "ApiError";
    this.status = status;
    this.problem = problem;
  }
}

export const DEFAULT_API_URL = process.env.NEXT_PUBLIC_API_URL || "https://orionsearch-api.onrender.com";

export class ApiAuthError extends ApiError {
  constructor(message = "Authentication is required. Sign in to continue.", problem?: ProblemDetails) {
    super(401, message, problem);
    this.name = "ApiAuthError";
  }
}

const TOKEN_KEY = "orion_tokens";

/** Persisted opaque bearer tokens. Never decoded or logged. */
export const tokenStore = {
  read(): TokenResponse | null {
    try {
      const raw = localStorage.getItem(TOKEN_KEY);
      const parsed = raw ? (JSON.parse(raw) as TokenResponse) : null;
      return parsed && parsed.accessToken && parsed.refreshToken ? parsed : null;
    } catch { return null; }
  },
  write(tokens: TokenResponse | null) {
    try {
      if (tokens) localStorage.setItem(TOKEN_KEY, JSON.stringify(tokens));
      else localStorage.removeItem(TOKEN_KEY);
    } catch { /* storage unavailable */ }
    listeners.forEach(fn => fn());
  },
  subscribe(fn: () => void) { listeners.add(fn); return () => { listeners.delete(fn); }; },
};
const listeners = new Set<() => void>();

export interface IApiClient {
  register(email: string, password: string): Promise<UserProfile>;
  login(email: string, password: string): Promise<void>;
  logout(): Promise<void>;
  changePassword(currentPassword: string, newPassword: string): Promise<void>;
  me(): Promise<UserProfile>;
  isAuthenticated(): boolean;
  listResearchJobs(archiveId: string, params?: ListRunsParams): Promise<PagedResult<ResearchRunStatus>>;
  getResearchReport(runId: string): Promise<ResearchReport>;
  listFindings(archiveId: string, params?: ListFindingsParams): Promise<PagedResult<FindingSummary>>;
  getFinding(archiveId: string, findingId: string, params?: PageParams): Promise<FindingDetails>;
  getSource(archiveId: string, documentId: string): Promise<SourceDetails>;
  listVersions(archiveId: string, documentId: string, params?: PageParams): Promise<PagedResult<CapturedVersionSummary>>;
  getVersion(archiveId: string, documentId: string, versionId: string): Promise<CapturedVersion>;
  downloadRaw(archiveId: string, documentId: string, versionId: string): Promise<Blob>;
  listArchives(params?: ListArchivesParams): Promise<PagedResult<ArchiveSummary>>;
  getArchive(id: string): Promise<ArchiveDetails>;
  createArchive(req: CreateArchiveRequest): Promise<ArchiveDetails>;
  patchArchive(id: string, req: PatchArchiveRequest): Promise<ArchiveDetails>;
  deleteArchive(id: string): Promise<void>;
  listSources(archiveId: string, params?: ListSourcesParams): Promise<PagedResult<SourceSummary>>;
  submitResearchJob(
    archiveId: string,
    req?: SubmitResearchJobRequest,
    idempotencyKey?: string
  ): Promise<ResearchRunStatus>;
  getResearchJob(id: string): Promise<ResearchRunStatus>;
  checkHealth(): Promise<{ status: string }>;
  checkReadiness(): Promise<string>;
}

export class LiveApiClient implements IApiClient {
  private baseUrl: string;

  constructor(baseUrl = DEFAULT_API_URL) {
    this.baseUrl = baseUrl.replace(/\/+$/, "");
  }

  private refreshing: Promise<boolean> | null = null;

  isAuthenticated() { return tokenStore.read() !== null; }

  /** Exchanges the refresh token once for concurrent callers; clears the session on failure. */
  private refresh(): Promise<boolean> {
    if (!this.refreshing) {
      this.refreshing = (async () => {
        const current = tokenStore.read();
        if (!current) return false;
        try {
          const res = await fetch(`${this.baseUrl}/auth/refresh`, {
            method: "POST",
            headers: { "Content-Type": "application/json", Accept: "application/json" },
            body: JSON.stringify({ refreshToken: current.refreshToken }),
            cache: "no-store",
            signal: AbortSignal.timeout(75000),
          });
          if (!res.ok) { if (res.status === 401) tokenStore.write(null); return false; }
          tokenStore.write((await res.json()) as TokenResponse);
          return true;
        } catch { return false; }
      })().finally(() => { this.refreshing = null; });
    }
    return this.refreshing;
  }

  private async send(path: string, options: RequestInit, auth: boolean, retried = false): Promise<Response> {
    const headers: Record<string, string> = {
      Accept: "application/json",
      ...(options.headers as Record<string, string>),
    };
    if (options.body && !headers["Content-Type"]) headers["Content-Type"] = "application/json";
    const tokens = auth ? tokenStore.read() : null;
    if (tokens) headers.Authorization = `Bearer ${tokens.accessToken}`;
    let response: Response;
    try {
      response = await fetch(`${this.baseUrl}${path}`, { ...options, headers, cache: "no-store", signal: AbortSignal.timeout(75000) });
    } catch (err: unknown) {
      throw new ApiError(
        503,
        `Failed to connect to backend at ${this.baseUrl}: ${err instanceof Error ? err.message : String(err)}`
      );
    }
    if (response.status === 401 && auth && tokens && !retried && (await this.refresh())) {
      return this.send(path, options, auth, true);
    }
    return response;
  }

  private async request<T>(path: string, options: RequestInit = {}, plainText = false, auth = true): Promise<T> {
    const response = await this.send(path, options, auth);

    if (response.status === 204) {
      return undefined as unknown as T;
    }

    const contentType = response.headers.get("content-type") || "";
    const isJson = contentType.includes("application/json") || contentType.includes("problem+json");

    if (!response.ok) {
      let problem: ProblemDetails | undefined;
      let errorMsg = `HTTP Error ${response.status}: ${response.statusText}`;

      if (isJson) {
        try {
          problem = (await response.json()) as ProblemDetails;
          if (problem.title) {
            errorMsg = problem.title;
          }
        } catch {
          // ignore parsing error
        }
      }

      if (response.status === 401) {
        if (auth && tokenStore.read()) tokenStore.write(null);
        throw new ApiAuthError(problem?.title || undefined, problem);
      }
      throw new ApiError(response.status, errorMsg, problem);
    }

    if (isJson) {
      try { return (await response.json()) as T; } catch { throw new ApiError(502, "The API returned invalid JSON."); }
    }

    if (!plainText) throw new ApiError(502, "The API returned an unexpected response format.");
    return (await response.text()) as unknown as T;
  }

  async register(email: string, password: string): Promise<UserProfile> {
    return this.request<UserProfile>("/auth/register", { method: "POST", body: JSON.stringify({ email, password }) }, false, false);
  }

  async login(email: string, password: string): Promise<void> {
    const tokens = await this.request<TokenResponse>("/auth/login", { method: "POST", body: JSON.stringify({ email, password }) }, false, false);
    tokenStore.write(tokens);
  }

  async logout(): Promise<void> {
    try { await this.request<void>("/auth/logout", { method: "POST" }); }
    catch (err) { if (!(err instanceof ApiAuthError)) throw err; }
    finally { tokenStore.write(null); }
  }

  async changePassword(currentPassword: string, newPassword: string): Promise<void> {
    await this.request<void>("/auth/change-password", { method: "POST", body: JSON.stringify({ currentPassword, newPassword }) });
    tokenStore.write(null);
  }

  async me(): Promise<UserProfile> {
    return this.request<UserProfile>("/me");
  }

  private pageQuery(params: PageParams & Record<string, unknown> = {}) {
    const query = new URLSearchParams();
    for (const [key, value] of Object.entries(params)) if (value !== undefined && value !== "") query.set(key, String(value));
    const qs = query.toString();
    return qs ? `?${qs}` : "";
  }

  async listResearchJobs(archiveId: string, params: ListRunsParams = {}): Promise<PagedResult<ResearchRunStatus>> {
    return this.request(`/archives/${archiveId}/research-jobs${this.pageQuery({ ...params })}`);
  }

  async getResearchReport(runId: string): Promise<ResearchReport> {
    return this.request(`/research-jobs/${runId}/report`);
  }

  async listFindings(archiveId: string, params: ListFindingsParams = {}): Promise<PagedResult<FindingSummary>> {
    return this.request(`/archives/${archiveId}/findings${this.pageQuery({ ...params })}`);
  }

  async getFinding(archiveId: string, findingId: string, params: PageParams = {}): Promise<FindingDetails> {
    return this.request(`/archives/${archiveId}/findings/${findingId}${this.pageQuery({ ...params })}`);
  }

  async getSource(archiveId: string, documentId: string): Promise<SourceDetails> {
    return this.request(`/archives/${archiveId}/sources/${documentId}`);
  }

  async listVersions(archiveId: string, documentId: string, params: PageParams = {}): Promise<PagedResult<CapturedVersionSummary>> {
    return this.request(`/archives/${archiveId}/sources/${documentId}/versions${this.pageQuery({ ...params })}`);
  }

  async getVersion(archiveId: string, documentId: string, versionId: string): Promise<CapturedVersion> {
    return this.request(`/archives/${archiveId}/sources/${documentId}/versions/${versionId}`);
  }

  async downloadRaw(archiveId: string, documentId: string, versionId: string): Promise<Blob> {
    const response = await this.send(`/archives/${archiveId}/sources/${documentId}/versions/${versionId}/raw`, {}, true);
    if (!response.ok) {
      if (response.status === 401) throw new ApiAuthError();
      throw new ApiError(response.status, response.status === 503 ? "Stored capture is unavailable or failed its integrity check." : `HTTP Error ${response.status}`);
    }
    return response.blob();
  }

  async listArchives(params: ListArchivesParams = {}): Promise<PagedResult<ArchiveSummary>> {
    const query = new URLSearchParams();
    if (params.page !== undefined) query.set("page", String(params.page));
    if (params.pageSize !== undefined) query.set("pageSize", String(params.pageSize));
    if (params.topic) query.set("topic", params.topic);
    if (params.status) query.set("status", params.status);
    if (params.includeArchived !== undefined) query.set("includeArchived", String(params.includeArchived));

    const qs = query.toString();
    return this.request<PagedResult<ArchiveSummary>>(`/archives${qs ? `?${qs}` : ""}`);
  }

  async getArchive(id: string): Promise<ArchiveDetails> {
    return this.request<ArchiveDetails>(`/archives/${id}`);
  }

  async createArchive(req: CreateArchiveRequest): Promise<ArchiveDetails> {
    return this.request<ArchiveDetails>("/archives", {
      method: "POST",
      body: JSON.stringify(req),
    });
  }

  async patchArchive(id: string, req: PatchArchiveRequest): Promise<ArchiveDetails> {
    return this.request<ArchiveDetails>(`/archives/${id}`, {
      method: "PATCH",
      body: JSON.stringify(req),
    });
  }

  async deleteArchive(id: string): Promise<void> {
    return this.request<void>(`/archives/${id}`, {
      method: "DELETE",
    });
  }

  async listSources(archiveId: string, params: ListSourcesParams = {}): Promise<PagedResult<SourceSummary>> {
    const query = new URLSearchParams();
    if (params.page !== undefined) query.set("page", String(params.page));
    if (params.pageSize !== undefined) query.set("pageSize", String(params.pageSize));
    if (params.topic) query.set("topic", params.topic);
    if (params.publishedFrom) query.set("publishedFrom", params.publishedFrom);
    if (params.publishedTo) query.set("publishedTo", params.publishedTo);
    if (params.language) query.set("language", params.language);
    if (params.publisherDomain) query.set("publisherDomain", params.publisherDomain);
    if (params.sort) query.set("sort", params.sort);

    const qs = query.toString();
    return this.request<PagedResult<SourceSummary>>(`/archives/${archiveId}/sources${qs ? `?${qs}` : ""}`);
  }

  async submitResearchJob(
    archiveId: string,
    req: SubmitResearchJobRequest = {},
    idempotencyKey?: string
  ): Promise<ResearchRunStatus> {
    const headers: Record<string, string> = {};
    if (idempotencyKey) {
      headers["Idempotency-Key"] = idempotencyKey;
    }

    return this.request<ResearchRunStatus>(`/archives/${archiveId}/research-jobs`, {
      method: "POST",
      headers,
      body: JSON.stringify(req),
    });
  }

  async getResearchJob(id: string): Promise<ResearchRunStatus> {
    return this.request<ResearchRunStatus>(`/research-jobs/${id}`);
  }

  async checkReadiness(): Promise<string> {
    return this.request<string>("/health/ready", {}, true);
  }

  async checkHealth(): Promise<{ status: string }> {
    return this.request<{ status: string }>("/health");
  }
}
