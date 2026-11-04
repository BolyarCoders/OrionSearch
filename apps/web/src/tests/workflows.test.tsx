import React from "react";
import { act, cleanup, fireEvent, render, renderHook, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ApiError } from "@/lib/api/client";
import { ResearchRunStatus } from "@/types/api";
import { ArchiveDetails } from "@/types/api";
import { useResearchRun } from "@/lib/hooks/use-research-run";
import { StartResearchModal } from "@/components/archive-detail/start-research-modal";
import { SettingsPanel } from "@/components/archive-detail/settings-panel";
import { ArchiveHeader } from "@/components/archive-detail/archive-header";
const mocks = vi.hoisted(() => ({ getResearchJob: vi.fn(), submitResearchJob: vi.fn(), patchArchive: vi.fn() }));
vi.mock("@/lib/context/app-mode-context", () => ({ useAppMode: () => ({ client: mocks, }) }));
const ARCHIVE: ArchiveDetails = { archive: { id: "11111111-1111-4111-8111-111111111111", title: "Test", description: "d", status: "active", role: "owner", createdAt: "2026-01-01T00:00:00Z", updatedAt: "2026-01-01T00:00:00Z", sourceCount: 0 }, settings: { dateFrom: null, dateTo: null, languages: ["en"], preferredDomains: [], excludedDomains: [], config: {}, updatedAt: "2026-01-01T00:00:00Z" } };
const job: ResearchRunStatus = { id: "22222222-2222-4222-8222-222222222222", archiveId: ARCHIVE.archive.id, status: "queued", createdAt: "2026-10-27T00:00:00Z", startedAt: null, finishedAt: null, statistics: { queries: 0, sources: 0, versions: 0, findings: 0 }, attempts: 0, maxAttempts: 3, nextAttemptAt: null, retrying: false, error: null };
afterEach(() => { cleanup(); vi.useRealTimers(); vi.clearAllMocks(); sessionStorage.clear(); });
describe("Research polling", () => {
  it("allows one in-flight request and stops after completion", async () => {
    vi.useFakeTimers(); let resolve!: (value: ResearchRunStatus) => void;
    mocks.getResearchJob.mockReturnValue(new Promise<ResearchRunStatus>(r => { resolve = r; }));
    const completed = vi.fn(); const { result } = renderHook(() => useResearchRun(job, completed));
    await act(async () => { await vi.advanceTimersByTimeAsync(12000); });
    expect(mocks.getResearchJob).toHaveBeenCalledTimes(1);
    await act(async () => { resolve({ ...job, status: "completed" }); });
    expect(result.current.job.status).toBe("completed"); expect(completed).toHaveBeenCalledTimes(1);
    await act(async () => { await vi.advanceTimersByTimeAsync(12000); });
    expect(mocks.getResearchJob).toHaveBeenCalledTimes(1);
  });
  it("stops on lost authorization, exposes the failure and supports explicit retry", async () => {
    vi.useFakeTimers(); mocks.getResearchJob.mockRejectedValue(new ApiError(401, "Unauthorized"));
    const { result } = renderHook(() => useResearchRun(job));
    await act(async () => { await vi.advanceTimersByTimeAsync(2000); });
    expect(result.current.error).toMatchObject({ status: 401 });
    await act(async () => { await vi.advanceTimersByTimeAsync(12000); });
    expect(mocks.getResearchJob).toHaveBeenCalledTimes(1);
    mocks.getResearchJob.mockResolvedValue({ ...job, status: "cancelled" });
    act(() => result.current.retry());
    await act(async () => { await vi.advanceTimersByTimeAsync(2000); });
    expect(result.current.job.status).toBe("cancelled");
  });
  it("ignores a late result after unmount", async () => {
    vi.useFakeTimers(); let resolve!: (value: ResearchRunStatus) => void;
    mocks.getResearchJob.mockReturnValue(new Promise<ResearchRunStatus>(r => { resolve = r; }));
    const updated = vi.fn(); const { unmount } = renderHook(() => useResearchRun(job, undefined, updated));
    await act(async () => { await vi.advanceTimersByTimeAsync(2000); }); unmount();
    await act(async () => resolve({ ...job, status: "completed" }));
    expect(updated).not.toHaveBeenCalled();
  });
});
describe("Documented form behavior", () => {
  it("reuses the key and objective after an uncertain research submission", async () => {
    mocks.submitResearchJob.mockRejectedValueOnce(new ApiError(503, "Network unavailable")).mockResolvedValueOnce(job);
    const started = vi.fn(); render(<StartResearchModal isOpen onClose={vi.fn()} archiveId={job.archiveId} archiveTitle="AI" onJobStarted={started} />);
    fireEvent.change(screen.getByLabelText("Цел на проучването (по избор)"), { target: { value: "Test objective" } });
    fireEvent.click(screen.getByRole("button", { name: "Постави на опашка" }));
    await screen.findByRole("alert");
    fireEvent.click(screen.getByRole("button", { name: "Опитай същата заявка отново" }));
    await waitFor(() => expect(started).toHaveBeenCalled());
    expect(mocks.submitResearchJob.mock.calls[0]).toEqual(mocks.submitResearchJob.mock.calls[1]);
    expect(mocks.submitResearchJob.mock.calls[0][1]).toEqual({ objective: "Test objective" });
  });
  it("sends a title-only sparse patch without status or stale settings", async () => {
    const details = structuredClone(ARCHIVE); mocks.patchArchive.mockResolvedValue(details);
    render(<SettingsPanel details={details} onUpdate={vi.fn()} />);
    fireEvent.change(screen.getByLabelText(/Заглавие на архива/), { target: { value: "Renamed" } });
    fireEvent.click(screen.getByRole("button", { name: "Запази настройките" }));
    await waitFor(() => expect(mocks.patchArchive).toHaveBeenCalledWith(details.archive.id, { title: "Renamed" }));
  });
  it("provides read-only settings and disables research/lifecycle actions for viewers", () => {
    const details = structuredClone(ARCHIVE); details.archive.role = "viewer";
    render(<><SettingsPanel details={details} onUpdate={vi.fn()} /><ArchiveHeader details={details} onStartResearch={vi.fn()} onStatusChange={vi.fn()} /></>);
    expect(screen.getByLabelText(/Заглавие на архива/)).toBeDisabled();
    expect(screen.queryByRole("button", { name: "Запази настройките" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Стартирай проучване" })).toBeDisabled();
    expect(screen.queryByRole("button", { name: "Още опции за архива" })).not.toBeInTheDocument();
  });
});
