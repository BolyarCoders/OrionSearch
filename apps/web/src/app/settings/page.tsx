"use client";
import React, { useState } from "react";
import { DEFAULT_API_URL } from "@/lib/api/client";
import { useAppMode } from "@/lib/context/app-mode-context";
import { Button } from "@/components/ui/button";
import { ApiProblem } from "@/components/ui/api-problem";
import { Input } from "@/components/ui/input";
import { useAuth } from "@/lib/context/auth-context";

function AccountSection() {
  const { client, isDemo } = useAppMode();
  const { user, logout } = useAuth();
  const [current, setCurrent] = useState("");
  const [next, setNext] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  if (isDemo) return null;
  async function change(event: React.FormEvent) {
    event.preventDefault();
    setBusy(true); setError(null);
    try {
      await client.changePassword(current, next);
      await logout();
    } catch (failure) { setError(failure); setBusy(false); }
  }
  return <section className="bg-surface border border-border rounded-panel p-6 space-y-4">
    <h2 className="font-semibold">Account</h2>
    {user && <p className="text-sm text-content-secondary">Signed in as <strong>{user.email}</strong></p>}
    <form onSubmit={change} className="space-y-3 max-w-sm">
      <ApiProblem error={error} />
      <Input label="Current password" type="password" autoComplete="current-password" required value={current} onChange={e => setCurrent(e.target.value)} />
      <Input label="New password" type="password" autoComplete="new-password" required minLength={12} maxLength={128} value={next} onChange={e => setNext(e.target.value)} hint="Changing your password signs you out on every device." />
      <Button type="submit" isLoading={busy}>Change password</Button>
    </form>
    <Button variant="secondary" size="sm" onClick={() => void logout()}>Sign out</Button>
  </section>;
}
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
    <AccountSection />
  </div>;
}
