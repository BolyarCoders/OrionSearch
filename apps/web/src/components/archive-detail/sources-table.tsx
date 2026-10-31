"use client";

import React, { useState } from "react";
import { SourceSummary, SourceSortOption } from "@/types/api";
import { formatRelativeTime } from "@/lib/normalization";
import {
  ExternalLink,
  Search,
  ArrowUpDown,
  FileText,
  ChevronLeft,
  ChevronRight,
  Globe,
} from "lucide-react";

export interface SourceFilters { publishedFrom: string; publishedTo: string; language: string; publisherDomain: string; }

interface SourcesTableProps {
  filters?: SourceFilters;
  onFiltersChange?: (filters: SourceFilters) => void;
  sources: SourceSummary[];
  totalCount: number;
  currentPage: number;
  pageSize: number;
  onPageSizeChange?: (size: number) => void;
  onPageChange: (page: number) => void;
  searchTopic: string;
  onSearchChange: (topic: string) => void;
  sortOption: SourceSortOption;
  onSortChange: (sort: SourceSortOption) => void;
  isLoading?: boolean;
}

export function SourcesTable({
  sources,
  totalCount,
  currentPage,
  pageSize, onPageSizeChange,
  onPageChange,
  searchTopic,
  onSearchChange,
  sortOption,
  onSortChange,
  isLoading = false,
  filters, onFiltersChange,
}: SourcesTableProps) {
  const [draft, setDraft] = useState<SourceFilters>(filters || { publishedFrom: "", publishedTo: "", language: "", publisherDomain: "" });
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize));

  return (
    <div className="bg-surface border border-border rounded-panel overflow-hidden">
      {/* Table Toolbar */}
      <div className="p-4 border-b border-border/80 flex flex-col sm:flex-row items-stretch sm:items-center justify-between gap-3">
        {/* Search by document title */}
        <div className="relative flex-1 max-w-sm">
          <Search className="w-4 h-4 text-content-muted absolute left-3 top-1/2 -translate-y-1/2 pointer-events-none" />
          <input
            type="text"
            value={searchTopic}
            onChange={(e) => onSearchChange(e.target.value)}
            placeholder="Search discovered sources..."
            aria-label="Search source titles"
            maxLength={200}
            className="w-full bg-surface-raised text-content-primary placeholder-content-muted text-xs pl-9 pr-3 py-1.5 rounded-control border border-border focus:border-accent focus:outline-none"
          />
        </div>

        {/* Sort Select */}
        <div className="flex items-center gap-2">
          <span className="text-xs text-content-muted shrink-0 hidden sm:inline">Sort:</span>
          <div className="relative flex items-center">
            <ArrowUpDown className="w-3.5 h-3.5 text-content-muted absolute left-2.5 pointer-events-none" />
            <select
              aria-label="Sort sources"
              value={sortOption}
              onChange={(e) => onSortChange(e.target.value as SourceSortOption)}
              className="bg-surface-raised border border-border text-content-primary text-xs rounded-control pl-7 pr-6 py-1.5 appearance-none focus:outline-none focus:border-accent cursor-pointer"
            >
              <option value="added_desc">Recently added</option>
              <option value="added_asc">Oldest added</option>
              <option value="published_desc">Latest published</option>
              <option value="published_asc">Earliest published</option>
              <option value="relevance_desc">Highest relevance</option>
              <option value="title_asc">Title A-Z</option>
            </select>
          </div>
        </div>
      </div>

      {onFiltersChange && <form onSubmit={e => { e.preventDefault(); onFiltersChange(draft); }} className="p-4 border-b border-border grid sm:grid-cols-2 xl:grid-cols-5 gap-3 text-xs">
        {([['publishedFrom','Published from','date'], ['publishedTo','Published to','date'], ['language','Language','text'], ['publisherDomain','Publisher domain','text']] as const).map(([key,label,type]) => <label key={key} className="text-content-muted space-y-1"><span>{label}</span><input type={type} value={draft[key]} onChange={e => setDraft({ ...draft, [key]: e.target.value })} className="block w-full rounded-control border border-border bg-surface-raised text-content-primary p-2" /></label>)}
        <div className="flex gap-2 items-end"><button className="bg-accent text-on-accent rounded-control px-3 py-2" type="submit">Apply</button><button className="border border-border rounded-control px-3 py-2" type="button" onClick={() => { const empty = { publishedFrom: '', publishedTo: '', language: '', publisherDomain: '' }; setDraft(empty); onFiltersChange(empty); }}>Reset</button></div>
      </form>}
      {/* Sources Table */}
      <div className="overflow-x-auto">
        <table className="w-full text-left text-xs">
          <thead>
            <tr className="bg-surface-raised/40 text-content-muted border-b border-border/80">
              <th className="py-3 px-4 font-medium">Title</th>
              <th className="py-3 px-4 font-medium">Publisher / Domain</th>
              <th className="py-3 px-4 font-medium">Published Date</th>
              <th className="py-3 px-4 font-medium">Added</th>
              <th className="py-3 px-4 font-medium text-right">Relevance</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-border/40">
            {isLoading ? (
              <tr>
                <td colSpan={5} className="py-12 text-center text-content-muted">
                  Loading sources...
                </td>
              </tr>
            ) : sources.length === 0 ? (
              <tr>
                <td colSpan={5} className="py-12 text-center text-content-muted">
                  <div className="w-8 h-8 rounded-full bg-surface-raised flex items-center justify-center mx-auto mb-2 text-content-muted">
                    <FileText className="w-4 h-4" />
                  </div>
                  {searchTopic || Object.values(filters || {}).some(Boolean) ? "No sources match these filters." : "No sources discovered yet for this archive."}
                </td>
              </tr>
            ) : (
              sources.map((src) => {
                const formattedPublished = src.publishedAt
                  ? new Date(src.publishedAt).toLocaleDateString("en-US", {
                      timeZone: "UTC",
                      year: "numeric",
                      month: "short",
                      day: "numeric",
                    })
                  : "Unknown";

                return (
                  <tr key={src.documentId} className="hover:bg-surface-raised/40 transition-colors">
                    {/* Title & canonical link */}
                    <td className="py-3.5 px-4 max-w-md">
                      <a
                        href={src.canonicalUrl}
                        target="_blank"
                        rel="noreferrer"
                        className="font-medium text-content-primary hover:text-accent transition-colors flex items-center gap-1.5 group"
                      >
                        <span className="line-clamp-1">{src.title || src.canonicalUrl}</span>
                        <ExternalLink className="w-3 h-3 text-content-muted group-hover:text-accent shrink-0" />
                      </a>
                    </td>

                    {/* Publisher */}
                    <td className="py-3.5 px-4 text-content-secondary whitespace-nowrap">
                      <div className="flex items-center gap-1.5">
                        <Globe className="w-3 h-3 text-content-muted shrink-0" />
                        <span className="font-medium text-content-primary">
                          {src.publisherName || src.publisherDomain || "Unknown"}
                        </span>
                        {src.publisherName && (
                          <span className="text-xs text-content-muted">({src.publisherDomain})</span>
                        )}
                      </div>
                    </td>

                    {/* Publication date */}
                    <td className="py-3.5 px-4 whitespace-nowrap">
                      {src.publishedAt ? (
                        <span className="text-content-secondary">{formattedPublished}</span>
                      ) : (
                        <span className="text-content-muted font-mono text-xs bg-surface-raised px-1.5 py-0.5 rounded border border-border/60">
                          Unknown
                        </span>
                      )}
                    </td>

                    {/* Added */}
                    <td className="py-3.5 px-4 text-content-muted whitespace-nowrap">
                      {formatRelativeTime(src.addedAt)}
                    </td>

                    {/* Relevance score */}
                    <td className="py-3.5 px-4 text-right whitespace-nowrap">
                      {src.relevanceScore !== null ? (
                        <span className="inline-flex items-center px-2 py-0.5 rounded-full text-xs font-mono font-medium bg-accent/15 text-accent border border-accent/30">
                          {src.relevanceScore.toFixed(2)}
                        </span>
                      ) : (
                        <span className="text-content-muted font-mono text-xs">—</span>
                      )}
                    </td>
                  </tr>
                );
              })
            )}
          </tbody>
        </table>
      </div>

      {/* Pagination Footer */}
      <div className="p-4 border-t border-border/80 flex items-center justify-between text-xs text-content-muted">
        <div>
          Showing {sources.length} of {totalCount} discovered sources
          {onPageSizeChange && <select aria-label="Sources per page" className="bg-surface-raised border border-border rounded-control p-1 ml-2" value={pageSize} onChange={e => onPageSizeChange(Number(e.target.value))}>{[20, 50, 100].map(n => <option key={n} value={n}>{n} per page</option>)}</select>}
        </div>
        <div className="flex items-center gap-2">
          <button
            onClick={() => onPageChange(currentPage - 1)}
            disabled={currentPage <= 1 || isLoading}
            className="p-1 rounded-control border border-border hover:bg-surface-raised disabled:opacity-30 disabled:cursor-not-allowed"
            aria-label="Previous page"
          >
            <ChevronLeft className="w-4 h-4" />
          </button>
          <span>
            Page {currentPage} of {totalPages}
          </span>
          <button
            onClick={() => onPageChange(currentPage + 1)}
            disabled={currentPage >= totalPages || isLoading}
            className="p-1 rounded-control border border-border hover:bg-surface-raised disabled:opacity-30 disabled:cursor-not-allowed"
            aria-label="Next page"
          >
            <ChevronRight className="w-4 h-4" />
          </button>
        </div>
      </div>
    </div>
  );
}
