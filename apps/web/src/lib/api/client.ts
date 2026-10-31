import {
  ArchiveDetails,
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
  constructor(message = "Authentication is required. The current API has no configured sign-in provider.", problem?: ProblemDetails) {
    super(401, message, problem);
    this.name = "ApiAuthError";
  }
}

export interface IApiClient {
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

  private async request<T>(path: string, options: RequestInit = {}, plainText = false): Promise<T> {
    const url = `${this.baseUrl}${path}`;
    const headers: Record<string, string> = {
      Accept: "application/json",
      ...(options.headers as Record<string, string>),
    };

    if (options.body && !headers["Content-Type"]) {
      headers["Content-Type"] = "application/json";
    }

    let response: Response;
    try {
      response = await fetch(url, { ...options, headers, cache: "no-store", signal: AbortSignal.timeout(75000) });
    } catch (err: unknown) {
      throw new ApiError(
        503,
        `Failed to connect to backend at ${this.baseUrl}: ${err instanceof Error ? err.message : String(err)}`
      );
    }

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

      if (response.status === 401) throw new ApiAuthError(problem?.title || undefined, problem);
      throw new ApiError(response.status, errorMsg, problem);
    }

    if (isJson) {
      try { return (await response.json()) as T; } catch { throw new ApiError(502, "The API returned invalid JSON."); }
    }

    if (!plainText) throw new ApiError(502, "The API returned an unexpected response format.");
    return (await response.text()) as unknown as T;
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
