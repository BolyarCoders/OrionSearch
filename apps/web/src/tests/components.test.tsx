import { describe, it, expect } from "vitest";
import React from "react";
import { render, screen } from "@testing-library/react";
import { StatusPill } from "@/components/ui/status-pill";
import { Badge } from "@/components/ui/badge";
import { StatSummary } from "@/components/dashboard/stat-summary";
import { ArchiveCard } from "@/components/dashboard/archive-card";
import { FilterBar } from "@/components/dashboard/filter-bar";
import { ArchiveSummary } from "@/types/api";
import { ApiProblem } from "@/components/ui/api-problem";
import { ApiCloudflareChallengeError } from "@/lib/api/client";

it("does not describe a Cloudflare challenge as an archive permission error", () => {
  render(<ApiProblem error={new ApiCloudflareChallengeError(403)} />);
  expect(screen.getByRole("alert")).toHaveTextContent("Защитата на сайта изисква проверка");
  expect(screen.queryByText(/Ролята ви в архива/)).not.toBeInTheDocument();
});

describe("StatusPill Component", () => {
  it("renders active status with label", () => {
    render(<StatusPill status="active" />);
    expect(screen.getByText("Активен")).toBeInTheDocument();
  });

  it("renders running status with label", () => {
    render(<StatusPill status="running" />);
    expect(screen.getByText("Изпълнява се")).toBeInTheDocument();
  });

  it("renders completed status with label", () => {
    render(<StatusPill status="completed" />);
    expect(screen.getByText("Завършено")).toBeInTheDocument();
  });

  it("renders failed status with label", () => {
    render(<StatusPill status="failed" />);
    expect(screen.getByText("Неуспешно")).toBeInTheDocument();
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
    expect(screen.getByText("Източници (видими)")).toBeInTheDocument();
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
    expect(screen.getByText("240 източника")).toBeInTheDocument();
    expect(screen.getByText("Активен")).toBeInTheDocument();
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

    expect(screen.getByPlaceholderText(/търсене на архиви/i)).toBeInTheDocument();
    expect(screen.getByText("Всички")).toBeInTheDocument();
    expect(screen.getByText("Активни")).toBeInTheDocument();
    expect(screen.getByText("На пауза")).toBeInTheDocument();
  });
});
