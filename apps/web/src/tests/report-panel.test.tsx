import React from "react";
import { fireEvent, render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ApiError } from "@/lib/api/client";
import { ReportPanel } from "@/components/archive-detail/report-panel";

const mocks = vi.hoisted(() => ({ getResearchReport: vi.fn() }));
vi.mock("@/lib/context/app-mode-context", () => ({ useAppMode: () => ({ client: mocks }) }));

describe("research reports", () => {
  beforeEach(() => { mocks.getResearchReport.mockReset(); });

  it("loads the report for the selected run", async () => {
    mocks.getResearchReport.mockResolvedValue({ researchRunId: "run-one", text: "Findings [1].\n\nSources (1):\n[1] Article — https://example.com/article" });
    render(<ReportPanel runId="run-one" />);
    expect(await screen.findByRole("link", { name: "Article" })).toHaveAttribute("href", "https://example.com/article");
    expect(mocks.getResearchReport).toHaveBeenCalledWith("run-one");
  });

  it("does not reveal whether an inaccessible report exists", async () => {
    mocks.getResearchReport.mockRejectedValue(new ApiError(404, "Not found"));
    render(<ReportPanel runId="run-one" />);
    expect(await screen.findByText("Докладът не е наличен или нямате достъп до него.")).toBeInTheDocument();
  });

  it("allows retrying a temporary report error", async () => {
    mocks.getResearchReport.mockRejectedValueOnce(new ApiError(503, "Unavailable"))
      .mockResolvedValueOnce({ researchRunId: "run-one", text: "Recovered report" });
    render(<ReportPanel runId="run-one" />);
    fireEvent.click(await screen.findByRole("button", { name: "Опитай отново" }));
    expect(await screen.findByText("Recovered report")).toBeInTheDocument();
  });
});
