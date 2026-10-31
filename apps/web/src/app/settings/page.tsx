"use client";
import { useState } from "react";
import { DEFAULT_API_URL } from "@/lib/api/client";
import { useAppMode } from "@/lib/context/app-mode-context";
import { Button } from "@/components/ui/button";
import { ApiProblem } from "@/components/ui/api-problem";
export default function ConnectionSettings() {
  const { client, isDemo, setMode } = useAppMode();
  const [health, setHealth] = useState("");
  const [ready, setReady] = useState("");
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);
  async function check() {
    setBusy(true); setError(null); setHealth(""); setReady("");
    const results = await Promise.allSettled([client.checkHealth(), client.checkReadiness()]);
    if (results[0].status === "fulfilled") setHealth(results[0].value.status); else setError(results[0].reason);
    if (results[1].status === "fulfilled") setReady(results[1].value); else setError(results[1].reason);
    setBusy(false);
  }
  return <div className="space-y-6 max-w-3xl"><div><h1 className="text-2xl font-bold">Connection settings</h1><p className="mt-2 text-sm text-content-muted">Check the API connection and choose your workspace mode.</p></div>
    <section className="bg-surface border border-border rounded-panel p-6 space-y-4"><div className="flex flex-wrap gap-3 items-center justify-between"><h2 className="font-semibold">{isDemo ? "Demo workspace" : "Live API"}</h2><Button variant="secondary" size="sm" onClick={() => setMode(isDemo ? "live" : "demo")}>Switch to {isDemo ? "Live API" : "Demo"}</Button></div>
      <p className="text-sm text-content-secondary break-all">{isDemo ? "Simulated data in this browser session. No backend requests or accounts." : DEFAULT_API_URL}</p>
      <Button isLoading={busy} onClick={check}>Check connection</Button>
      <ApiProblem error={error} />
      {health && <p role="status" className="text-sm">API liveness: <span className="text-status-success">{health}</span></p>}
      {ready && <p role="status" className="text-sm">Database connection: <span className="text-status-success">{ready}</span></p>}
      <p className="text-xs text-content-muted">A healthy connection does not establish authentication, database schema compatibility or availability of the research worker. Live requests may wait while the hosted API wakes up.</p>
    </section>
    <section className="bg-surface border border-border rounded-panel p-6 space-y-3"><h2 className="font-semibold">Live access</h2><p className="text-sm text-content-secondary">The documented API currently rejects protected requests until its authentication provider is configured. There is no sign-in endpoint in this API version. Demo mode is an explicit alternative, never an automatic fallback.</p><p className="text-xs text-content-muted">The API origin is configured at build time with NEXT_PUBLIC_API_URL. Cross-origin browser access also requires the API to allow this frontend's origin.</p><div className="flex flex-wrap gap-4 text-sm"><a className="text-accent" href={`${DEFAULT_API_URL}/health`} target="_blank" rel="noreferrer">API health</a><a className="text-accent" href={`${DEFAULT_API_URL}/health/ready`} target="_blank" rel="noreferrer">Database readiness</a><a className="text-accent" href={`${DEFAULT_API_URL}/openapi/v1.json`} target="_blank" rel="noreferrer">Development OpenAPI</a></div><p className="text-xs text-content-muted">OpenAPI is available at /openapi/v1.json only in a Development API environment; a 404 in Production is expected.</p></section>
  </div>;
}
