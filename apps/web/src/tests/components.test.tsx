import { describe, it, expect } from "vitest";
import React from "react";
import { render, screen } from "@testing-library/react";
import { StatusPill } from "@/components/ui/status-pill";
import { Badge } from "@/components/ui/badge";
import { StatSummary } from "@/components/dashboard/stat-summary";
import { ArchiveCard } from "@/components/dashboard/archive-card";
import { FilterBar } from "@/components/dashboard/filter-bar";
import { ArchiveSummary } from "@/types/api";

describe("StatusPill Component", () => {
  it("renders active status with label", () => {
    render(<StatusPill status="active" />);
    expect(screen.getByText("Active")).toBeInTheDocument();
  });

  it("renders running status with label", () => {
    render(<StatusPill status="running" />);
    expect(screen.getByText("Running")).toBeInTheDocument();
  });

  it("renders completed status with label", () => {
    render(<StatusPill status="completed" />);
    expect(screen.getByText("Completed")).toBeInTheDocument();
  });

  it("renders failed status with label", () => {
    render(<StatusPill status="failed" />);
    expect(screen.getByText("Failed")).toBeInTheDocument();
  });
});

describe("Badge Component", () => {
  it("renders children with correct variant styles", () => {
    render(<Badge variant="accent">New</Badge>);
    expect(screen.getByText("New")).toBeInTheDocument();
  });
});

describe("StatSummary Component", () => {
  const mockArchives: ArchiveSummary[] = [
    {
      id: "1",
      title: "Archive 1",
      description: null,
      status: "active",
      role: "owner",
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
      sourceCount: 150,
    },
    {
      id: "2",
      title: "Archive 2",
      description: null,
      status: "active",
      role: "owner",
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
      sourceCount: 50,
    },
    {
      id: "3",
      title: "Archive 3",
      description: null,
      status: "paused",
      role: "owner",
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
      sourceCount: 100,
    },
  ];

  it("computes active, paused, and visible sources correctly", () => {
    render(<StatSummary archives={mockArchives} totalCount={3} />);
    expect(screen.getByText("3")).toBeInTheDocument(); // Total archives
    expect(screen.getByText("2")).toBeInTheDocument(); // Active count
    expect(screen.getByText("1")).toBeInTheDocument(); // Paused count
    expect(screen.getByText("300")).toBeInTheDocument(); // 150 + 50 + 100 sources
    expect(screen.getByText("Indexed sources (visible)")).toBeInTheDocument();
  });
});

describe("ArchiveCard Component", () => {
  const sampleArchive: ArchiveSummary = {
    id: "sample-1",
    title: "Autonomous Space Exploration",
    description: "Deep probe navigation and mission autonomy",
    status: "active",
    role: "owner",
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
    sourceCount: 240,
  };

  it("renders archive card title, description, and source count", () => {
    render(<ArchiveCard archive={sampleArchive} />);
    expect(screen.getByText("Autonomous Space Exploration")).toBeInTheDocument();
    expect(screen.getByText("Deep probe navigation and mission autonomy")).toBeInTheDocument();
    expect(screen.getByText("240 sources")).toBeInTheDocument();
    expect(screen.getByText("Active")).toBeInTheDocument();
  });
});

describe("FilterBar Component", () => {
  it("renders filter controls and search input", () => {
    let currentTopic = "";
    render(
      <FilterBar
        searchTopic={currentTopic}
        onSearchChange={(val) => {
          currentTopic = val;
        }}
        statusFilter="all"
        onStatusFilterChange={() => {}}
        sortBy="updated_desc"
        onSortChange={() => {}}
        viewMode="grid"
        onViewModeChange={() => {}}
      />
    );

    expect(screen.getByPlaceholderText(/search archives/i)).toBeInTheDocument();
    expect(screen.getByText("All")).toBeInTheDocument();
    expect(screen.getByText("Active")).toBeInTheDocument();
    expect(screen.getByText("Paused")).toBeInTheDocument();
  });
});
