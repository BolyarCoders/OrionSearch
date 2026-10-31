"use client";

import React, { useEffect, useState, useMemo, useRef, Suspense } from "react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { ApiProblem } from "@/components/ui/api-problem";
import { useAppMode } from "@/lib/context/app-mode-context";
import { ArchiveSummary, ArchiveStatus } from "@/types/api";
import { ApiAuthError } from "@/lib/api/client";
import { StatSummary } from "@/components/dashboard/stat-summary";
import { FilterBar, SortField, ViewMode } from "@/components/dashboard/filter-bar";
import { ArchiveCard } from "@/components/dashboard/archive-card";
import { RecentActivity } from "@/components/dashboard/recent-activity";
import { AuthNotice } from "@/components/ui/auth-notice";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { Plus, FolderArchive, AlertCircle } from "lucide-react";


function ArchivesContent() {
  const urlParams = useSearchParams();
  const requestId = useRef(0);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(6);
  const [includeArchived, setIncludeArchived] = useState(false);
  const { client } = useAppMode();

  const [archives, setArchives] = useState<ArchiveSummary[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [isLoading, setIsLoading] = useState(true);
  const [authError, setAuthError] = useState<unknown>(null);
  const [generalError, setGeneralError] = useState<unknown>(null);

  // Filters & layout state
  const [searchTopic, setSearchTopic] = useState("");
  useEffect(() => { if (urlParams.get("focus") === "search") document.querySelector<HTMLInputElement>('input[aria-label="Search archives"]')?.focus(); }, [urlParams]);
  const [statusFilter, setStatusFilter] = useState<ArchiveStatus | "all">("all");
  const [sortBy, setSortBy] = useState<SortField>("updated_desc");
  const [viewMode, setViewMode] = useState<ViewMode>("grid");

  useEffect(() => { setSearchTopic(urlParams.get("search") || ""); setPage(1); }, [urlParams]);

  const fetchArchives = async () => {
    const current = ++requestId.current;
    setIsLoading(true);
    setAuthError(null);
    setGeneralError(null);

    try {
      const res = await client.listArchives({
        page, pageSize, includeArchived,
        status: statusFilter === "all" ? undefined : statusFilter,
        topic: searchTopic.trim() || undefined,
      });
      if (current !== requestId.current) return;
      setArchives(res.items);
      setTotalCount(res.totalCount);
    } catch (err: unknown) {
      if (current !== requestId.current) return;
      setArchives([]); setTotalCount(0);
      if (err instanceof ApiAuthError || (err as { status?: number })?.status === 401) {
        setAuthError(err);
      } else {
        setGeneralError(err);
      }
    } finally {
      if (current === requestId.current) setIsLoading(false);
    }
  };

  useEffect(() => {
    fetchArchives();
    // eslint-disable-next-line react-hooks/exhaustive-deps
    return () => { requestId.current++; };
  }, [client, statusFilter, searchTopic, page, pageSize, includeArchived]);

  // Client-side sorting for display
  const sortedArchives = useMemo(() => {
    const list = [...archives];
    list.sort((a, b) => {
      if (sortBy === "title_asc") {
        return a.title.localeCompare(b.title);
      }
      if (sortBy === "sources_desc") {
        return (b.sourceCount || 0) - (a.sourceCount || 0);
      }
      return new Date(b.updatedAt).getTime() - new Date(a.updatedAt).getTime();
    });
    return list;
  }, [archives, sortBy]);

  const handleStatusChange = async (id: string, newStatus: ArchiveStatus) => {
    try {
      if (newStatus === "archived") await client.deleteArchive(id);
      else await client.patchArchive(id, { status: newStatus });
      fetchArchives();
    } catch (err: unknown) {
      setGeneralError(err);
    }
  };

  return (
    <div className="space-y-6">
      <div className="flex flex-col sm:flex-row sm:items-end justify-between gap-5 pb-3">
        <div>
          <h1 className="library-title">Archives</h1>
          <p className="text-base text-content-secondary mt-3 max-w-xl">Your topics, sources and ongoing research. All in one place.</p>
        </div>
        <Link href="/archives/new" className="self-start shrink-0"><Button leftIcon={<Plus className="w-4 h-4" />}>New archive</Button></Link>
      </div>

      {/* 401 Auth Notice in Live Mode */}
      {!!authError && <AuthNotice error={authError} onRetry={fetchArchives} />}

      <ApiProblem error={generalError} onRetry={fetchArchives} />

      {/* Stat Summary Row */}
      {!authError && <StatSummary archives={archives} totalCount={totalCount} />}

      {/* Filter and View Bar */}
      {!authError && (
        <FilterBar
          searchTopic={searchTopic}
          onSearchChange={value => { setSearchTopic(value); setPage(1); }}
          statusFilter={statusFilter}
          onStatusFilterChange={value => { setStatusFilter(value); setPage(1); }}
          sortBy={sortBy}
          onSortChange={setSortBy}
          viewMode={viewMode}
          onViewModeChange={setViewMode}
        />
      )}

      {!authError && <div className="flex flex-wrap items-center justify-between gap-3 text-xs text-content-muted">
        <label className="flex gap-2 items-center"><input type="checkbox" checked={includeArchived} onChange={e => { setIncludeArchived(e.target.checked); setPage(1); }} />Include archived in All</label>
        <span>Sorting applies to the archives on this page.</span>
      </div>}
      {/* Archives Content (Grid or List) */}
      {!authError && (
        <>
          {isLoading ? (
            <div className="archive-grid">
              {[1, 2, 3, 4, 5, 6].map((i) => (
                <div
                  key={i}
                  className="bg-surface border border-border rounded-panel overflow-hidden p-0 space-y-4"
                >
                  <Skeleton className="aspect-[16/9] w-full" />
                  <div className="p-5 space-y-2">
                    <Skeleton className="h-5 w-3/4" />
                    <Skeleton className="h-4 w-full" />
                    <Skeleton className="h-4 w-1/2" />
                  </div>
                </div>
              ))}
            </div>
          ) : sortedArchives.length === 0 ? (
            <div className="bg-surface border border-border rounded-panel py-16 px-6 text-center max-w-md mx-auto">
              <div className="w-12 h-12 rounded-full bg-surface-raised border border-border flex items-center justify-center mx-auto text-content-muted mb-4">
                <FolderArchive className="w-6 h-6 text-accent" />
              </div>
              <h3 className="text-base font-semibold text-content-primary">No archives found</h3>
              <p className="text-xs text-content-muted mt-1 leading-relaxed">
                {searchTopic || statusFilter !== "all"
                  ? "No research archives match your current filter criteria. Try resetting filters."
                  : "You haven't created any research archives yet. Get started by creating your first archive."}
              </p>
              <div className="mt-5 flex items-center justify-center gap-3">
                {searchTopic || statusFilter !== "all" ? (
                  <Button
                    variant="secondary"
                    size="sm"
                    onClick={() => {
                      setSearchTopic("");
                      setStatusFilter("all");
                    }}
                  >
                    Clear filters
                  </Button>
                ) : (
                  <Link href="/archives/new">
                    <Button variant="primary" size="sm" leftIcon={<Plus className="w-4 h-4" />}>
                      Create archive
                    </Button>
                  </Link>
                )}
              </div>
            </div>
          ) : viewMode === "grid" ? (
            <div className="archive-grid">
              {sortedArchives.map((arch) => (
                <ArchiveCard
                  key={arch.id}
                  archive={arch}
                  onStatusChange={handleStatusChange}
                />
              ))}
            </div>
          ) : (
            <div className="bg-surface border border-border rounded-panel divide-y divide-border/60">
              {sortedArchives.map((arch) => (
                <div
                  key={arch.id}
                  className="p-4 sm:p-5 flex flex-col sm:flex-row items-start sm:items-center justify-between gap-4 hover:bg-surface-raised/40 transition-colors"
                >
                  <div className="space-y-1">
                    <Link
                      href={`/archives/${arch.id}`}
                      className="text-base font-semibold text-content-primary hover:text-accent transition-colors"
                    >
                      {arch.title}
                    </Link>
                    <p className="text-xs text-content-secondary line-clamp-1 max-w-xl">
                      {arch.description || "No topic description provided."}
                    </p>
                  </div>
                  <div className="flex items-center gap-4 text-xs text-content-muted shrink-0">
                    <span>{arch.sourceCount.toLocaleString()} sources</span>
                    <Button
                      variant="secondary"
                      size="sm"
                      disabled={arch.role !== "owner"}
                      onClick={() => handleStatusChange(arch.id, arch.status === "active" ? "paused" : "active")}
                    >
                      {arch.status === "active" ? "Pause" : "Activate"}
                    </Button>
                  </div>
                </div>
              ))}
            </div>
          )}

          <div className="flex flex-wrap items-center justify-between gap-3 text-xs text-content-muted border-t border-border pt-4">
            <label>Per page <select aria-label="Archives per page" value={pageSize} onChange={e => { setPageSize(Number(e.target.value)); setPage(1); }} className="bg-surface-raised border border-border rounded p-2 ml-2">{[6, 20, 50, 100].map(n => <option key={n}>{n}</option>)}</select></label>
            <span>{totalCount} matching archives · Page {page} of {Math.max(1, Math.ceil(totalCount / pageSize))}</span>
            <div className="flex gap-2"><Button variant="secondary" size="sm" disabled={page === 1 || isLoading} onClick={() => setPage(page - 1)}>Previous</Button><Button variant="secondary" size="sm" disabled={page * pageSize >= totalCount || isLoading} onClick={() => setPage(page + 1)}>Next</Button></div>
          </div>
          {/* Recent Research Activity Section */}
          <RecentActivity />
        </>
      )}
    </div>
  );
}

export default function ArchivesPage() { return <Suspense fallback={<Skeleton className="h-96" />}><ArchivesContent /></Suspense>; }
