import React from "react";
import { afterEach, expect, it, vi } from "vitest";
import { cleanup, render, screen } from "@testing-library/react";
import { AppModeProvider } from "@/lib/context/app-mode-context";
import ArchivesPage from "@/app/archives/page";
const query = new URLSearchParams();
vi.mock("next/navigation", () => ({ useSearchParams: () => query }));
afterEach(() => { cleanup(); localStorage.clear(); vi.unstubAllGlobals(); });
it("shows the actual authentication failure and trace with a sign-in link", async () => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify({ title: "Unauthorized", traceId: "live-trace" }), { status: 401, headers: { "content-type": "application/problem+json" } })));
  render(<AppModeProvider><ArchivesPage /></AppModeProvider>);
  expect(await screen.findByText("Идентификатор за проследяване: live-trace")).toBeInTheDocument();
  expect(screen.getByRole("link", { name: "Вход" })).toHaveAttribute("href", "/login");
});
