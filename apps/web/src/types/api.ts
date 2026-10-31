export type ArchiveStatus = "active" | "paused" | "archived";
export type ArchiveRole = "owner" | "editor" | "viewer";
export type ResearchRunState = "queued" | "running" | "completed" | "failed" | "cancelled";

export type SourceSortOption =
  | "added_desc"
  | "added_asc"
  | "published_desc"
  | "published_asc"
  | "relevance_desc"
  | "title_asc";

export interface Archive {
  id: string;
  title: string;
  description: string | null;
  status: ArchiveStatus;
  role: ArchiveRole;
  createdAt: string;
  updatedAt: string;
  sourceCount: number;
}

export interface ArchiveSettings {
  dateFrom: string | null;
  dateTo: string | null;
  languages: string[];
  preferredDomains: string[];
  excludedDomains: string[];
  config: Record<string, unknown>;
  updatedAt: string;
}

export interface ArchiveDetails {
  archive: Archive;
  settings: ArchiveSettings;
}

export interface ArchiveSummary {
  id: string;
  title: string;
  description: string | null;
  status: ArchiveStatus;
  role: ArchiveRole;
  createdAt: string;
  updatedAt: string;
  sourceCount: number;
}

export interface SourceSummary {
  documentId: string;
  canonicalUrl: string;
  title: string | null;
  publisherName: string | null;
  publisherDomain: string | null;
  language: string | null;
  publishedAt: string | null;
  firstSeenAt: string;
  lastSeenAt: string;
  relevanceScore: number | null;
  addedAt: string;
}

export interface ResearchStatistics {
  queries: number;
  sources: number;
  versions: number;
  findings: number;
}

export interface ResearchRunStatus {
  id: string;
  archiveId: string;
  status: ResearchRunState;
  createdAt: string;
  startedAt: string | null;
  finishedAt: string | null;
  statistics: ResearchStatistics;
  attempts: number;
  maxAttempts: number;
  nextAttemptAt: string | null;
  retrying: boolean;
  error: string | null;
}

export interface PagedResult<T> {
  items: T[];
  pageNumber: number;
  pageSize: number;
  totalCount: number;
}

export interface ProblemDetails {
  title?: string;
  status?: number;
  instance?: string;
  traceId?: string;
  type?: string;
  errors?: Record<string, string[]>;
}

export interface CreateArchiveRequest {
  title: string;
  description?: string | null;
  settings?: {
    dateFrom?: string | null;
    dateTo?: string | null;
    languages?: string[];
    preferredDomains?: string[];
    excludedDomains?: string[];
    config?: Record<string, unknown>;
  };
}

export interface PatchArchiveRequest {
  title?: string;
  description?: string | null;
  status?: ArchiveStatus;
  settings?: {
    dateFrom?: string | null;
    dateTo?: string | null;
    languages?: string[];
    preferredDomains?: string[];
    excludedDomains?: string[];
    config?: Record<string, unknown>;
  };
}

export interface SubmitResearchJobRequest {
  objective?: string | null;
}

export interface ListArchivesParams {
  page?: number;
  pageSize?: number;
  topic?: string;
  status?: ArchiveStatus;
  includeArchived?: boolean;
}

export interface ListSourcesParams {
  page?: number;
  pageSize?: number;
  topic?: string;
  publishedFrom?: string;
  publishedTo?: string;
  language?: string;
  publisherDomain?: string;
  sort?: SourceSortOption;
}

export type AppMode = "demo" | "live";
