"use client";

import React from "react";
import { Search, ArrowUpDown, LayoutGrid, List } from "lucide-react";
import { ArchiveStatus } from "@/types/api";

export type SortField = "updated_desc" | "title_asc" | "sources_desc";
export type ViewMode = "grid" | "list";

interface FilterBarProps {
  searchTopic: string;
  onSearchChange: (value: string) => void;
  statusFilter: ArchiveStatus | "all";
  onStatusFilterChange: (status: ArchiveStatus | "all") => void;
  sortBy: SortField;
  onSortChange: (sort: SortField) => void;
  viewMode: ViewMode;
  onViewModeChange: (mode: ViewMode) => void;
}

export function FilterBar({
  searchTopic,
  onSearchChange,
  statusFilter,
  onStatusFilterChange,
  sortBy,
  onSortChange,
  viewMode,
  onViewModeChange,
}: FilterBarProps) {
  const statusOptions: { label: string; value: ArchiveStatus | "all" }[] = [
    { label: "All", value: "all" },
    { label: "Active", value: "active" },
    { label: "Paused", value: "paused" },
    { label: "Archived", value: "archived" },
  ];

  return (
    <div className="flex flex-col md:flex-row items-stretch md:items-center justify-between gap-3 pt-1">
      {/* Search Input */}
      <div className="relative flex-1 max-w-md">
        <Search className="w-4 h-4 text-content-muted absolute left-3.5 top-1/2 -translate-y-1/2 pointer-events-none" />
        <input
          type="text"
          aria-label="Search archives"
          maxLength={200}
          value={searchTopic}
          onChange={(e) => onSearchChange(e.target.value)}
          placeholder="Search archives…"
          className="w-full bg-surface-raised text-content-primary placeholder-content-muted text-xs sm:text-sm pl-10 pr-4 py-2 rounded-control border border-border focus:border-accent focus:outline-none focus:ring-1 focus:ring-accent transition-colors"
        />
      </div>

      {/* Controls row */}
      <div className="flex flex-wrap items-center gap-3">
        {/* Status segment pills */}
        <div className="flex items-center bg-surface-raised border border-border rounded-control p-0.5">
          {statusOptions.map((opt) => {
            const isSelected = statusFilter === opt.value;
            return (
              <button
                key={opt.value}
                aria-pressed={isSelected}
                onClick={() => onStatusFilterChange(opt.value)}
                className={`px-3 py-1.5 text-xs font-medium rounded-control transition-colors duration-micro ${
                  isSelected
                    ? "bg-surface text-content-primary shadow-sm font-semibold"
                    : "text-content-secondary hover:text-content-primary hover:bg-surface/60"
                }`}
              >
                {opt.label}
              </button>
            );
          })}
        </div>

        {/* Sort select */}
        <div className="relative flex items-center">
          <ArrowUpDown className="w-3.5 h-3.5 text-content-muted absolute left-3 pointer-events-none" />
          <select
            aria-label="Sort visible archives"
            value={sortBy}
            onChange={(e) => onSortChange(e.target.value as SortField)}
            className="bg-surface-raised border border-border text-content-primary text-xs rounded-control pl-8 pr-7 py-2 appearance-none focus:outline-none focus:border-accent cursor-pointer"
          >
            <option value="updated_desc">Last updated</option>
            <option value="title_asc">Title A-Z</option>
            <option value="sources_desc">Most sources</option>
          </select>
        </div>

        {/* View Switcher */}
        <div className="flex items-center bg-surface-raised border border-border rounded-control p-0.5">
          <button
            onClick={() => onViewModeChange("grid")}
            className={`p-2 rounded-control transition-colors ${
              viewMode === "grid"
                ? "bg-accent/20 text-accent"
                : "text-content-muted hover:text-content-primary"
            }`}
            aria-label="Grid view" aria-pressed={viewMode === "grid"}
            title="Grid view"
          >
            <LayoutGrid className="w-4 h-4" />
          </button>
          <button
            onClick={() => onViewModeChange("list")}
            className={`p-2 rounded-control transition-colors ${
              viewMode === "list"
                ? "bg-accent/20 text-accent"
                : "text-content-muted hover:text-content-primary"
            }`}
            aria-label="List view" aria-pressed={viewMode === "list"}
            title="List view"
          >
            <List className="w-4 h-4" />
          </button>
        </div>
      </div>
    </div>
  );
}
