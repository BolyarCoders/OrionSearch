import { describe, it, expect } from "vitest";
import React from "react";
import { render, screen } from "@testing-library/react";
import { ReportContent } from "@/components/archive-detail/report-content";

describe("ReportContent", () => {
  it("renders emphasis, inline code and ordered lists while preserving citations", () => {
    const { container } = render(<ReportContent runId="format" text={`**Strong [1]** and *emphasis* with \`code [1]\`.

1. First [1]
2. Second **item**

Sources (1):
[1] An article — https://example.com/article`} />);
    expect(container.querySelector("strong")).toHaveTextContent("Strong [1]");
    expect(container.querySelector("em")).toHaveTextContent("emphasis");
    expect(container.querySelector("code")).toHaveTextContent("code [1]");
    const list = container.querySelector("article > ol");
    expect(list?.querySelectorAll("li")).toHaveLength(2);
    expect(screen.getAllByRole("link", { name: /Източник 1/ })).toHaveLength(2);
  });

  it("ignores malformed bibliography entries and keeps valid English sources", () => {
    render(<ReportContent runId="bad" text={`Citations [1] [2] [3] [4].

Sources:
[1] Good — https://example.com/a
[2] Script — javascript:alert(1)
[3] No host — https://
This is not a source line
[4] Wrong protocol — ftp://example.com/file`} />);
    expect(screen.getByRole("link", { name: "Good" })).toHaveAttribute("href", "https://example.com/a");
    expect(screen.getByRole("link", { name: /Източник 1/ })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /Източник [234]/ })).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /Script|No host|Wrong protocol/ })).not.toBeInTheDocument();
  });
  it("renders thematic headings, citations and safe source links", () => {
    render(<ReportContent runId="run-one" text={`# Новини за електромобили

## Основни тенденции

Tesla представи нов модел [1]. BYD обяви подобрения [2].

- Батерии с по-голям пробег [2].

Източници (2):
[1] Tesla launches EV — https://ev.example/tesla
[2] BYD battery technology — https://ev.example/byd`} />);
    expect(screen.getByRole("heading", { name: "Новини за електромобили" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Основни тенденции" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Източници (2)" })).toBeInTheDocument();
    const source = screen.getByRole("link", { name: "Tesla launches EV" });
    expect(source).toHaveAttribute("href", "https://ev.example/tesla");
    expect(source).toHaveAttribute("rel", "noopener noreferrer");
    expect(screen.getByRole("link", { name: /Източник 1/ })).toHaveAttribute("href", "#report-run-one-source-1");
  });

  it("renders article text safely, without trusting HTML", () => {
    const malicious = `## Overview

<script>window.evil=1</script> Launch [1].

Източници (1):
[1] Web story — javascript:alert(1)`;
    const { container } = render(<ReportContent runId="run-x" text={malicious} />);
    expect(container.querySelector("script")).toBeNull();
    expect(screen.getByText(/<script>window.evil=1<\/script>/)).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Web story" })).toBeNull();
  });

  it("handles no sources without crashing", () => {
    render(<ReportContent runId="empty" text="Не са намерени проверими публикации." />);
    expect(screen.getByText("Не са намерени проверими публикации.")).toBeInTheDocument();
  });
});
