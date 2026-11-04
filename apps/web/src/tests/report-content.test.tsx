import { describe, it, expect } from "vitest";
import React from "react";
import { render, screen } from "@testing-library/react";
import { ReportContent } from "@/components/archive-detail/report-content";

describe("ReportContent", () => {
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
