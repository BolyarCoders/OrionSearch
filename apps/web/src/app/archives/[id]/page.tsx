"use client";

import React, { useEffect, useState, useRef, use } from "react";
import Link from "next/link";
import { useAppMode } from "@/lib/context/app-mode-context";
import {
  ArchiveDetails,
  ResearchRunStatus,
  SourceSummary,
  SourceSortOption,
} from "@/types/api";
import { ApiAuthError, ApiError } from "@/lib/api/client";
import { ArchiveHeader } from "@/components/archive-detail/archive-header";
import { SourcesTable, SourceFilters } from "@/components/archive-detail/sources-table";
import { ResearchRunCard } from "@/components/archive-detail/research-run-card";
import { SettingsPanel } from "@/components/archive-detail/settings-panel";
import { StartResearchModal } from "@/components/archive-detail/start-research-modal";
import { Tabs } from "@/components/ui/tabs";
import { AuthNotice } from "@/components/ui/auth-notice";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { ApiProblem } from "@/components/ui/api-problem";
import { Input } from "@/components/ui/input";
import { LANGUAGE_CODES } from "@/lib/archive-validation";
import { normalizeDomain, validateDateWindow, formatRelativeTime } from "@/lib/normalization";
import {
  FileText,
  Lightbulb,
  Clock,
  Search,
  AlertCircle,
  FolderArchive,
  ArrowRight,
  Globe,
  Calendar,
  Layers,
  Edit,
} from "lucide-react";


interface ArchiveDetailPageProps {
  params: Promise<{ id: string }>;
  searchParams?: Promise<{ tab?: string }>;
}

export default function ArchiveDetailPage({ params, searchParams }: ArchiveDetailPageProps) {
  const { id } = use(params);
  const resolvedSearchParams = searchParams ? use(searchParams) : undefined;
  const initialTab =
    resolvedSearchParams?.tab &&
    ["overview", "sources", "research", "settings"].includes(resolvedSearchParams.tab)
      ? resolvedSearchParams.tab
      : "overview";

  const { client, mode } = useAppMode();
  const archiveRequest = useRef(0);
  const sourceRequest = useRef(0);
  const runRequest = useRef(0);
  const [runId, setRunId] = useState("");
  const [runError, setRunError] = useState<unknown>(null);
  const [actionError, setActionError] = useState<unknown>(null);
  const [sourcesError, setSourcesError] = useState<unknown>(null);
  const [sourceFilters, setSourceFilters] = useState<SourceFilters>({ publishedFrom: "", publishedTo: "", language: "", publisherDomain: "" });

  const [details, setDetails] = useState<ArchiveDetails | null>(null);
  const [activeTab, setActiveTab] = useState<string>(initialTab);
  const [isLoading, setIsLoading] = useState<boolean>(true);
  const [authError, setAuthError] = useState<unknown>(null);
  const [notFound, setNotFound] = useState<boolean>(false);
  const [errorMsg, setErrorMsg] = useState<unknown>(null);

  useEffect(() => {
    if (typeof window !== "undefined") {
      const params = new URLSearchParams(window.location.search);
      const tabParam = params.get("tab");
      if (tabParam && ["overview", "sources", "research", "settings"].includes(tabParam)) {
        setActiveTab(tabParam);
      }
    }
  }, []);

  // Sources state
  const [sources, setSources] = useState<SourceSummary[]>([]);
  const [sourcesTotal, setSourcesTotal] = useState<number>(0);
  const [sourcesSize, setSourcesSize] = useState(20);
  const [sourcesPage, setSourcesPage] = useState<number>(1);
  const [sourcesSearch, setSourcesSearch] = useState<string>("");
  const [sourcesSort, setSourcesSort] = useState<SourceSortOption>("added_desc");
  const [isSourcesLoading, setIsSourcesLoading] = useState<boolean>(false);

  // Active / Latest Research Job
  const [activeJob, setActiveJob] = useState<ResearchRunStatus | null>(null);
  const [isStartModalOpen, setIsStartModalOpen] = useState(false);

  const fetchArchive = async () => {
    const current = ++archiveRequest.current;
    setIsLoading(true);
    setAuthError(null);
    setNotFound(false);
    setErrorMsg(null);

    try {
      const data = await client.getArchive(id);
      if (current === archiveRequest.current) setDetails(data);
    } catch (err: unknown) {
      if (current !== archiveRequest.current) return;
      setDetails(null);
      if (err instanceof ApiAuthError || (err as { status?: number })?.status === 401) {
        setAuthError(err);
      } else if ((err as { status?: number })?.status === 404) {
        setNotFound(true);
      } else {
        setErrorMsg(err);
      }
    } finally {
      if (current === archiveRequest.current) setIsLoading(false);
    }
  };

  const fetchSources = async () => {
    if (!id) return;
    const current = ++sourceRequest.current;
    setIsSourcesLoading(true);
    setSourcesError(null);
    try {
      const invalidDate = validateDateWindow(sourceFilters.publishedFrom, sourceFilters.publishedTo);
      if (invalidDate) throw new ApiError(400, invalidDate);
      if (sourceFilters.language && !LANGUAGE_CODES.has(sourceFilters.language.trim().toLowerCase())) throw new ApiError(400, "Use a recognized two-letter language code.");
      const domain = sourceFilters.publisherDomain ? normalizeDomain(sourceFilters.publisherDomain) : null;
      if (domain?.error) throw new ApiError(400, domain.error);
      const res = await client.listSources(id, {
        page: sourcesPage,
        pageSize: sourcesSize,
        topic: sourcesSearch.trim() || undefined,
        sort: sourcesSort,
        publishedFrom: sourceFilters.publishedFrom || undefined,
        publishedTo: sourceFilters.publishedTo || undefined,
        language: sourceFilters.language.trim().toLowerCase() || undefined,
        publisherDomain: domain?.domain || undefined,
      });
      if (current !== sourceRequest.current) return;
      setSources(res.items);
      setSourcesTotal(res.totalCount);
    } catch (error) {
      if (current !== sourceRequest.current) return;
      setSources([]); setSourcesTotal(0); setSourcesError(error);
    } finally {
      if (current === sourceRequest.current) setIsSourcesLoading(false);
    }
  };

  useEffect(() => {
    fetchArchive();
    // eslint-disable-next-line react-hooks/exhaustive-deps
    return () => { archiveRequest.current++; };
  }, [id, client]);

  useEffect(() => {
    if (activeTab === "sources" || activeTab === "overview") {
      fetchSources();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
    return () => { sourceRequest.current++; };
  }, [id, client, sourcesPage, sourcesSize, sourcesSearch, sourcesSort, activeTab, sourceFilters]);

  const handleStatusChange = async (newStatus: "active" | "paused" | "archived") => {
    try {
      if (newStatus === "archived") { await client.deleteArchive(id); await fetchArchive(); }
      else setDetails(await client.patchArchive(id, { status: newStatus }));
      setActionError(null);
    } catch (err: unknown) {
      setActionError(err);
    }
  };

  const rememberRun = (job: ResearchRunStatus) => {
    setActiveJob(job); setRunId(job.id);
    try { sessionStorage.setItem(`orion-run:${mode}:${id}`, job.id); } catch { /* optional browser storage */ }
  };
  const openRun = async (knownId: string) => {
    const current = ++runRequest.current;
    setRunError(null);
    try {
      if (!/^[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/i.test(knownId)) throw new Error("Enter a valid research run UUID.");
      const job = await client.getResearchJob(knownId);
      if (current !== runRequest.current) return;
      if (job.archiveId !== id) throw new Error("This run does not belong to this archive.");
      rememberRun(job);
    } catch (error) { if (current === runRequest.current) setRunError(error); }
  };
  useEffect(() => {
    setActiveJob(null); setRunId("");
    try { const saved = sessionStorage.getItem(`orion-run:${mode}:${id}`); if (saved) { setRunId(saved); void openRun(saved); } } catch { /* optional browser storage */ }
    // The API has no run-history endpoint; restore only an explicitly remembered run.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    return () => { runRequest.current++; };
  }, [client, mode, id]);

  if (errorMsg) return <ApiProblem error={errorMsg} onRetry={fetchArchive} />;
  if (authError) {
    return (
      <div className="space-y-6">
        <AuthNotice error={authError} onRetry={fetchArchive} />
      </div>
    );
  }

  if (notFound) {
    return (
      <div className="py-20 text-center max-w-md mx-auto">
        <div className="w-12 h-12 rounded-full bg-surface-raised border border-border flex items-center justify-center mx-auto text-content-muted mb-4">
          <FolderArchive className="w-6 h-6 text-status-warning" />
        </div>
        <h2 className="text-xl font-bold text-content-primary">Archive Not Found</h2>
        <p className="text-xs text-content-muted mt-2 leading-relaxed">
          The requested archive could not be found or you do not have permission to view it.
        </p>
        <div className="mt-6">
          <Link href="/archives">
            <Button variant="secondary" size="sm">
              Back to Archives
            </Button>
          </Link>
        </div>
      </div>
    );
  }

  if (isLoading || !details) {
    return (
      <div className="space-y-6">
        <Skeleton className="h-44 w-full rounded-panel" />
        <div className="grid grid-cols-4 gap-4">
          {[1, 2, 3, 4].map((i) => (
            <Skeleton key={i} className="h-24 rounded-panel" />
          ))}
        </div>
        <Skeleton className="h-96 rounded-panel" />
      </div>
    );
  }

  const { archive, settings } = details;

  const tabs = [
    { id: "overview", label: "Overview" },
    { id: "sources", label: "Sources", count: archive.sourceCount },
    { id: "research", label: "Research", count: activeJob ? 1 : undefined },
    { id: "settings", label: "Settings" },
  ];

  return (
    <div className="space-y-6">
      {/* Archive Header */}
      <ArchiveHeader
        details={details}
        onStartResearch={() => setIsStartModalOpen(true)}
        onStatusChange={handleStatusChange}
      />

      <ApiProblem error={actionError} />
      <ApiProblem error={sourcesError} onRetry={fetchSources} />
      {/* Navigation Tabs */}
      <Tabs tabs={tabs} activeTab={activeTab} onChange={setActiveTab} />

      {/* OVERVIEW TAB */}
      {activeTab === "overview" && (
        <div className="space-y-6">
          <dl className="grid grid-cols-2 sm:grid-cols-4 gap-6 border-b border-border pb-6">
            <div><dt className="text-xs text-content-muted mb-2">Total sources</dt><dd className="text-2xl font-medium tabular-nums">{archive.sourceCount.toLocaleString()}</dd></div>
            <div><dt className="text-xs text-content-muted mb-2">Findings in tracked run</dt><dd className="text-2xl font-medium tabular-nums">{activeJob ? activeJob.statistics.findings : "—"}</dd></div>
            <div><dt className="text-xs text-content-muted mb-2">Tracked run</dt><dd className="text-sm font-medium">{activeJob ? activeJob.status : "No tracked run"}</dd></div>
            <div><dt className="text-xs text-content-muted mb-2">Archive updated</dt><dd className="text-sm font-medium">{formatRelativeTime(archive.updatedAt)}</dd></div>
          </dl>

          {/* 2-Column Overview Layout */}
          <div className="grid grid-cols-1 lg:grid-cols-12 gap-6 items-start">
            {/* Left: Active Research Run / Monitor */}
            <div className="lg:col-span-7 space-y-6">
              {activeJob ? (
                <ResearchRunCard
                  initialJob={activeJob}
                  onJobUpdated={setActiveJob}
              onJobCompleted={() => {
                    fetchArchive();
                    fetchSources();
                  }}
                />
              ) : (
                <div className="bg-surface border border-border rounded-panel p-6">
                  <div className="flex items-center justify-between pb-4 border-b border-border/80">
                    <div className="flex items-center gap-2">
                      <Search className="w-4 h-4 text-accent" />
                      <h3 className="text-sm font-semibold text-content-primary">
                        Research on demand
                      </h3>
                    </div>
                  </div>

                  <div className="py-6 text-center max-w-md mx-auto">
                    <p className="text-xs text-content-secondary leading-relaxed">
                      Start a research run to collect and preserve sources using this archive’s preferences. Queued work requires an available research worker; scheduling is not active.
                    </p>
                    <div className="mt-5">
                      <Button
                        variant="primary"
                        size="sm"
                        leftIcon={<Search className="w-4 h-4" />}
                        onClick={() => setIsStartModalOpen(true)}
                        disabled={archive.status !== "active" || archive.role === "viewer"}
                      >
                        Queue New Research Run
                      </Button>
                    </div>
                  </div>
                </div>
              )}

              {/* Discovered Sources Preview snippet */}
              <div className="bg-surface border border-border rounded-panel p-5 space-y-3">
                <div className="flex items-center justify-between pb-3 border-b border-border/80">
                  <h4 className="text-xs font-semibold uppercase tracking-wider text-content-primary">
                    Sources in current view
                  </h4>
                  <button
                    onClick={() => setActiveTab("sources")}
                    className="text-xs text-accent hover:underline flex items-center gap-1"
                  >
                    View all {archive.sourceCount} <ArrowRight className="w-3.5 h-3.5" />
                  </button>
                </div>

                <div className="divide-y divide-border/40">
                  {sources.slice(0, 4).map((s) => (
                    <div key={s.documentId} className="py-2.5 flex items-center justify-between gap-3 text-xs">
                      <div className="truncate flex-1">
                        <a
                          href={s.canonicalUrl}
                          target="_blank"
                          rel="noreferrer"
                          className="font-medium text-content-primary hover:text-accent transition-colors truncate block"
                        >
                          {s.title || s.canonicalUrl}
                        </a>
                        <span className="text-xs text-content-muted">{s.publisherDomain || "Unknown publisher"}</span>
                      </div>
                      <span className="text-xs font-mono text-accent bg-accent/10 px-2 py-0.5 rounded-full border border-accent/20 shrink-0">
                        {s.relevanceScore !== null ? s.relevanceScore.toFixed(2) : "—"}
                      </span>
                    </div>
                  ))}
                </div>
              </div>
            </div>

            {/* Right: "About this archive" Panel */}
            <div className="lg:col-span-5 bg-surface border border-border rounded-panel p-5 space-y-5">
              <div className="flex items-center justify-between pb-3 border-b border-border/80">
                <h3 className="text-sm font-semibold text-content-primary">About this archive</h3>
                <Button
                  variant="ghost"
                  size="sm"
                  onClick={() => setActiveTab("settings")}
                  leftIcon={<Edit className="w-3 h-3" />}
                >
                  Edit
                </Button>
              </div>

              <div className="space-y-4 text-xs">
                <div>
                  <div className="text-content-muted font-medium mb-1">Description</div>
                  <p className="text-content-secondary leading-relaxed">
                    {archive.description || "No topic description provided."}
                  </p>
                </div>

                <div className="pt-3 border-t border-border/60">
                  <div className="text-content-muted font-medium mb-1">Date Window</div>
                  <div className="flex items-center gap-1.5 text-content-secondary">
                    <Calendar className="w-3.5 h-3.5 text-content-muted" />
                    <span>
                      {settings.dateFrom || settings.dateTo
                        ? `${settings.dateFrom || "Any"} to ${settings.dateTo || "Present"}`
                        : "Unrestricted date window"}
                    </span>
                  </div>
                </div>

                <div className="pt-3 border-t border-border/60">
                  <div className="text-content-muted font-medium mb-1">Languages</div>
                  <div className="text-content-secondary">
                    {settings.languages.join(", ").toUpperCase()}
                  </div>
                </div>

                <div className="pt-3 border-t border-border/60">
                  <div className="text-content-muted font-medium mb-1">Preferred Domains</div>
                  <div className="flex flex-wrap gap-1 mt-1">
                    {settings.preferredDomains.length > 0 ? (
                      settings.preferredDomains.map((d) => (
                        <span
                          key={d}
                          className="bg-accent/15 text-accent border border-accent/30 text-xs font-mono px-2 py-0.5 rounded-full"
                        >
                          {d}
                        </span>
                      ))
                    ) : (
                      <span className="text-content-muted">None specified</span>
                    )}
                  </div>
                </div>

                <div className="pt-3 border-t border-border/60">
                  <div className="text-content-muted font-medium mb-1">Excluded Domains</div>
                  <div className="flex flex-wrap gap-1 mt-1">
                    {settings.excludedDomains.length > 0 ? (
                      settings.excludedDomains.map((d) => (
                        <span
                          key={d}
                          className="bg-status-failure/15 text-status-failure border border-status-failure/30 text-xs font-mono px-2 py-0.5 rounded-full"
                        >
                          {d}
                        </span>
                      ))
                    ) : (
                      <span className="text-content-muted">None specified</span>
                    )}
                  </div>
                </div>

                <div className="pt-3 border-t border-border/60">
                  <div className="text-content-muted font-medium mb-1">Update Cadence</div>
                  <div className="text-content-secondary capitalize">
                    {String(settings.config?.updateFrequency || "Weekly")}{" "}
                    <span className="text-content-muted text-xs">(manual for now)</span>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>
      )}

      {/* SOURCES TAB */}
      {activeTab === "sources" && (
        <SourcesTable
          sources={sources}
          totalCount={sourcesTotal}
          currentPage={sourcesPage}
          pageSize={sourcesSize}
          onPageSizeChange={size => { setSourcesSize(size); setSourcesPage(1); }}
          onPageChange={setSourcesPage}
          searchTopic={sourcesSearch}
          onSearchChange={value => { setSourcesSearch(value); setSourcesPage(1); }}
          sortOption={sourcesSort}
          onSortChange={value => { setSourcesSort(value); setSourcesPage(1); }}
          filters={sourceFilters}
          onFiltersChange={value => { setSourceFilters(value); setSourcesPage(1); }}
          isLoading={isSourcesLoading}
        />
      )}

      {/* RESEARCH TAB */}
      {activeTab === "research" && (
        <div className="space-y-6">
          <form onSubmit={e => { e.preventDefault(); void openRun(runId.trim()); }} className="bg-surface border border-border rounded-panel p-5 space-y-3">
            <Input label="Track a research run by ID" value={runId} onChange={e => setRunId(e.target.value)} placeholder="Research run UUID" hint="Only the run ID you supply or submit here is tracked. Global history is not available." />
            <Button type="submit" variant="secondary" size="sm">Load run</Button><ApiProblem error={runError} />
          </form>
          <div className="flex flex-wrap gap-3 items-center justify-between bg-surface border border-border rounded-panel p-5">
            <div>
              <h3 className="text-base font-semibold text-content-primary">
                Research Pipeline & Execution
              </h3>
              <p className="text-xs text-content-muted mt-0.5">
                Monitor background research jobs, status transitions, and committed findings.
              </p>
            </div>
            <Button
              variant="primary"
              size="sm"
              leftIcon={<Search className="w-4 h-4" />}
              onClick={() => setIsStartModalOpen(true)}
              disabled={archive.status !== "active" || archive.role === "viewer"}
            >
              Start Research Run
            </Button>
          </div>

          {activeJob ? (
            <ResearchRunCard
              initialJob={activeJob}
              onJobUpdated={setActiveJob}
              onJobCompleted={() => {
                fetchArchive();
                fetchSources();
              }}
            />
          ) : (
            <div className="bg-surface border border-border rounded-panel py-16 text-center max-w-md mx-auto">
              <div className="w-10 h-10 rounded-full bg-surface-raised flex items-center justify-center mx-auto text-content-muted mb-3">
                <Search className="w-5 h-5 text-accent" />
              </div>
              <h4 className="text-sm font-semibold text-content-primary">No Active Research Run</h4>
              <p className="text-xs text-content-muted mt-1 leading-relaxed">
                Click &ldquo;Start Research Run&rdquo; to dispatch an asynchronous background job for this archive.
              </p>
              <div className="mt-4">
                <Button
                  variant="primary"
                  size="sm"
                  onClick={() => setIsStartModalOpen(true)}
                  disabled={archive.status !== "active" || archive.role === "viewer"}
                >
                  Start Research
                </Button>
              </div>
            </div>
          )}
        </div>
      )}

      {/* SETTINGS TAB */}
      {activeTab === "settings" && (
        <SettingsPanel key={archive.id}
          details={details}
          onUpdate={(updated) => setDetails(updated)}
        />
      )}

      {/* Start Research Modal */}
      <StartResearchModal
        isOpen={isStartModalOpen}
        onClose={() => setIsStartModalOpen(false)}
        archiveId={archive.id}
        archiveTitle={archive.title}
        canSubmit={archive.role !== "viewer" && archive.status === "active"}
        onJobStarted={(job) => {
          rememberRun(job);
          setActiveTab("research");
        }}
      />
    </div>
  );
}
