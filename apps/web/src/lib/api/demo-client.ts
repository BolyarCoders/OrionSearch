import {
  ArchiveDetails,
  ArchiveSummary,
  CreateArchiveRequest,
  ListArchivesParams,
  ListSourcesParams,
  PagedResult,
  PatchArchiveRequest,
  ResearchRunStatus,
  SourceSummary,
  SubmitResearchJobRequest,
} from "@/types/api";
import { ApiError, IApiClient } from "./client";
import { normalizeDomain, validateTitle } from "../normalization";
import { validateArchiveInput } from "../archive-validation";
import { INITIAL_ARCHIVES, INITIAL_SOURCES } from "./mock-data";

export class DemoApiClient implements IApiClient {
  private archives: ArchiveDetails[];
  private sources: Record<string, SourceSummary[]>;
  private jobs: Map<string, ResearchRunStatus>;

  private requests = new Map<string, { input: string; id: string }>();

  constructor() {
    // Clone initial arrays to preserve original seeds across resets
    this.archives = JSON.parse(JSON.stringify(INITIAL_ARCHIVES));
    this.sources = JSON.parse(JSON.stringify(INITIAL_SOURCES));
    this.jobs = new Map();
  }

  async listArchives(params: ListArchivesParams = {}): Promise<PagedResult<ArchiveSummary>> {
    let list = this.archives.map((a) => ({ ...a.archive }));

    // Status filtering
    if (params.status) {
      list = list.filter((a) => a.status === params.status);
    } else if (!params.includeArchived) {
      list = list.filter((a) => a.status !== "archived");
    }

    // Topic substring filtering
    if (params.topic && params.topic.trim()) {
      const q = params.topic.trim().toLowerCase();
      list = list.filter((a) => {
        const titleMatch = a.title.toLowerCase().includes(q);
        const descMatch = a.description ? a.description.toLowerCase().includes(q) : false;
        return titleMatch || descMatch;
      });
    }

    // Sort: createdAt DESC, id ASC
    list.sort((a, b) => {
      const dateA = new Date(a.createdAt).getTime();
      const dateB = new Date(b.createdAt).getTime();
      if (dateB !== dateA) return dateB - dateA;
      return a.id.localeCompare(b.id);
    });

    const page = params.page && params.page > 0 ? params.page : 1;
    const pageSize = params.pageSize && params.pageSize > 0 ? params.pageSize : 20;
    const totalCount = list.length;
    const start = (page - 1) * pageSize;
    const items = list.slice(start, start + pageSize);

    return {
      items,
      pageNumber: page,
      pageSize,
      totalCount,
    };
  }

  async getArchive(id: string): Promise<ArchiveDetails> {
    const item = this.archives.find((a) => a.archive.id === id);
    if (!item) {
      throw new ApiError(404, `Archive with ID ${id} not found.`);
    }
    return JSON.parse(JSON.stringify(item));
  }

  async createArchive(req: CreateArchiveRequest): Promise<ArchiveDetails> {
    const errors = validateArchiveInput(req);
    if (Object.keys(errors).length) throw new ApiError(400, "Invalid archive request.", { errors });
    const id = crypto.randomUUID();
    const now = new Date().toISOString();

    const newArchive: ArchiveDetails = {
      archive: {
        id,
        title: req.title.trim(),
        description: req.description ?? null,
        status: "active",
        role: "owner",
        createdAt: now,
        updatedAt: now,
        sourceCount: 0,
      },
      settings: {
        dateFrom: req.settings?.dateFrom || null,
        dateTo: req.settings?.dateTo || null,
        languages: (req.settings?.languages || []).map(s => s.trim().toLowerCase()),
        preferredDomains: (req.settings?.preferredDomains || []).map(s => normalizeDomain(s).domain!),
        excludedDomains: (req.settings?.excludedDomains || []).map(s => normalizeDomain(s).domain!),
        config: req.settings?.config || {},
        updatedAt: now,
      },
    };

    this.archives.unshift(newArchive);
    this.sources[id] = [];

    return JSON.parse(JSON.stringify(newArchive));
  }

  async patchArchive(id: string, req: PatchArchiveRequest): Promise<ArchiveDetails> {
    const item = this.archives.find((a) => a.archive.id === id);
    if (!item) {
      throw new ApiError(404, `Archive with ID ${id} not found.`);
    }

    if (item.archive.role === "viewer" || (req.status !== undefined && item.archive.role !== "owner")) throw new ApiError(403, "Your role cannot perform this action.");
    const next = { title: req.title ?? item.archive.title, description: req.description === undefined ? item.archive.description : req.description, settings: { ...item.settings, ...req.settings } };
    const errors = validateArchiveInput(next);
    if (Object.keys(errors).length) throw new ApiError(400, "Invalid archive request.", { errors });
    next.settings.languages = next.settings.languages.map(s => s.trim().toLowerCase());
    next.settings.preferredDomains = next.settings.preferredDomains.map(s => normalizeDomain(s).domain!);
    next.settings.excludedDomains = next.settings.excludedDomains.map(s => normalizeDomain(s).domain!);
    const now = new Date().toISOString();
    const settingsChanged = JSON.stringify(next.settings) !== JSON.stringify(item.settings);
    const metadataChanged = next.title !== item.archive.title || next.description !== item.archive.description || (req.status !== undefined && req.status !== item.archive.status);
    if (metadataChanged || settingsChanged) {
      item.archive.title = next.title.trim(); item.archive.description = next.description;
      if (req.status) item.archive.status = req.status;
      item.archive.updatedAt = now;
      if (settingsChanged) item.settings = JSON.parse(JSON.stringify({ ...next.settings, updatedAt: now }));
    }
    return structuredClone(item);
  }

  async deleteArchive(id: string): Promise<void> {
    const item = this.archives.find((a) => a.archive.id === id);
    if (!item) {
      throw new ApiError(404, `Archive with ID ${id} not found.`);
    }
    if (item.archive.role !== "owner") throw new ApiError(403, "Only the owner can archive this workspace.");
    if (item.archive.status !== "archived") { item.archive.status = "archived"; item.archive.updatedAt = new Date().toISOString(); }
  }

  async listSources(archiveId: string, params: ListSourcesParams = {}): Promise<PagedResult<SourceSummary>> {
    await this.getArchive(archiveId);
    let list = this.sources[archiveId] ? [...this.sources[archiveId]] : [];

    // Filter by topic (matches title)
    if (params.topic && params.topic.trim()) {
      const q = params.topic.trim().toLowerCase();
      list = list.filter((s) => (s.title ? s.title.toLowerCase().includes(q) : false));
    }

    // Filter by publication date range
    if (params.publishedFrom) {
      const fromTime = new Date(params.publishedFrom).getTime();
      list = list.filter((s) => (s.publishedAt ? new Date(s.publishedAt).getTime() >= fromTime : false));
    }
    if (params.publishedTo) {
      const toTime = new Date(params.publishedTo).getTime() + 86400000;
      list = list.filter((s) => (s.publishedAt ? new Date(s.publishedAt).getTime() < toTime : false));
    }

    // Filter by language
    if (params.language) {
      list = list.filter((s) => s.language?.toLowerCase() === params.language?.toLowerCase());
    }

    // Filter by publisher domain
    if (params.publisherDomain) {
      list = list.filter((s) => s.publisherDomain?.toLowerCase() === params.publisherDomain?.toLowerCase());
    }

    // Sort handling
    const sort = params.sort || "added_desc";
    list.sort((a, b) => {
      const nullable = (x: string | number | null, y: string | number | null, descending = false) => {
        if (x === null && y === null) return 0;
        if (x === null) return 1;
        if (y === null) return -1;
        const comparison = typeof x === 'number' && typeof y === 'number' ? x - y : String(x).localeCompare(String(y));
        return descending ? -comparison : comparison;
      };
      let result: number;
      switch (sort) {
        case "published_desc": result = nullable(a.publishedAt, b.publishedAt, true); break;
        case "published_asc": result = nullable(a.publishedAt, b.publishedAt); break;
        case "relevance_desc": result = nullable(a.relevanceScore, b.relevanceScore, true); break;
        case "title_asc": result = nullable(a.title, b.title); break;
        case "added_asc": result = nullable(a.addedAt, b.addedAt); break;
        default: result = nullable(a.addedAt, b.addedAt, true);
      }
      return result || a.documentId.localeCompare(b.documentId);
    });

    const page = params.page && params.page > 0 ? params.page : 1;
    const pageSize = params.pageSize && params.pageSize > 0 ? params.pageSize : 20;
    const totalCount = list.length;
    const start = (page - 1) * pageSize;
    const items = list.slice(start, start + pageSize);

    return {
      items,
      pageNumber: page,
      pageSize,
      totalCount,
    };
  }

  async submitResearchJob(
    archiveId: string,
    req: SubmitResearchJobRequest = {},
    idempotencyKey?: string
  ): Promise<ResearchRunStatus> {
    const target = this.archives.find((a) => a.archive.id === archiveId);
    if (!target) {
      throw new ApiError(404, `Archive ${archiveId} not found`);
    }

    if (target.archive.role === "viewer") throw new ApiError(403, "Viewers cannot submit research.");
    if (req.objective != null && (!req.objective.trim() || req.objective.length > 2000)) throw new ApiError(400, "Objective must be nonblank and at most 2000 characters.");
    if (idempotencyKey !== undefined && !/^[!-~]{1,200}$/.test(idempotencyKey)) throw new ApiError(400, "Invalid idempotency key.");
    const input = JSON.stringify(req.objective == null ? null : req.objective.trim());
    const namespace = `${archiveId}:${idempotencyKey}`;
    const previous = idempotencyKey === undefined ? undefined : this.requests.get(namespace);
    if (previous) {
      if (previous.input !== input) throw new ApiError(409, "This key was used with a different objective.");
      return this.getResearchJob(previous.id);
    }
    if (target.archive.status !== "active") {
      throw new ApiError(409, `Cannot submit research for archive with status '${target.archive.status}'`);
    }

    const runId = crypto.randomUUID();
    const now = new Date();

    const jobStatus: ResearchRunStatus = {
      id: runId,
      archiveId,
      status: "queued",
      createdAt: now.toISOString(),
      startedAt: null,
      finishedAt: null,
      statistics: { queries: 0, sources: 0, versions: 0, findings: 0 },
      attempts: 0,
      maxAttempts: 3,
      nextAttemptAt: now.toISOString(),
      retrying: false,
      error: null,
    };

    this.jobs.set(runId, jobStatus);
    if (idempotencyKey !== undefined) this.requests.set(namespace, { input, id: runId });

    return JSON.parse(JSON.stringify(jobStatus));
  }

  async getResearchJob(id: string): Promise<ResearchRunStatus> {
    const job = this.jobs.get(id);
    if (!job) {
      throw new ApiError(404, `Research job ${id} not found.`);
    }
    const target = this.archives.find(a => a.archive.id === job.archiveId)!;
    if (["queued", "running"].includes(job.status)) {
      if (target.archive.status === "archived") { job.status = "cancelled"; job.finishedAt = new Date().toISOString(); }
      else if (job.status === "queued" && target.archive.status === "active" && Date.now() - Date.parse(job.createdAt) >= 1500) { job.status = "running"; job.startedAt = new Date().toISOString(); job.attempts = 1; job.nextAttemptAt = null; }
      else if (job.status === "running" && Date.now() - Date.parse(job.startedAt!) >= 3000) {
        job.status = "completed"; job.finishedAt = new Date().toISOString();
        const now = job.finishedAt;
        this.sources[job.archiveId].unshift({ documentId: crypto.randomUUID(), canonicalUrl: 'https://example.com/demo-research', title: `Demo research: ${target.archive.title}`, publisherName: 'Demo publisher', publisherDomain: 'example.com', language: 'en', publishedAt: null, firstSeenAt: now, lastSeenAt: now, addedAt: now, relevanceScore: null });
        target.archive.sourceCount++;
        job.statistics = { queries: 1, sources: 1, versions: 0, findings: 0 };
      }
    }
    return JSON.parse(JSON.stringify(job));
  }

  async checkReadiness(): Promise<string> { return "Healthy (simulated)"; }

  async checkHealth(): Promise<{ status: string }> {
    return { status: "healthy (demo-mode)" };
  }
}
