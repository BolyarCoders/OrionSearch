"use client";
import React, { useState } from "react";
import { DEFAULT_API_URL } from "@/lib/api/client";
import { useAppMode } from "@/lib/context/app-mode-context";
import { Button } from "@/components/ui/button";
import { ApiProblem } from "@/components/ui/api-problem";
import { Input } from "@/components/ui/input";
import { useAuth } from "@/lib/context/auth-context";

function AccountSection() {
  const { client } = useAppMode();
  const { user, logout } = useAuth();
  const [current, setCurrent] = useState("");
  const [next, setNext] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  async function change(event: React.FormEvent) {
    event.preventDefault();
    setBusy(true); setError(null);
    try {
      await client.changePassword(current, next);
      await logout();
    } catch (failure) { setError(failure); setBusy(false); }
  }
  return <section className="bg-surface border border-border rounded-panel p-6 space-y-4">
    <h2 className="font-semibold">Акаунт</h2>
    {user && <p className="text-sm text-content-secondary">Влезли сте като <strong>{user.email}</strong></p>}
    <form onSubmit={change} className="space-y-3 max-w-sm">
      <ApiProblem error={error} />
      <Input label="Текуща парола" type="password" autoComplete="current-password" required value={current} onChange={e => setCurrent(e.target.value)} />
      <Input label="Нова парола" type="password" autoComplete="new-password" required minLength={12} maxLength={128} value={next} onChange={e => setNext(e.target.value)} hint="Смяната на паролата ви отписва от всички устройства." />
      <Button type="submit" isLoading={busy}>Смяна на парола</Button>
    </form>
    <Button variant="secondary" size="sm" onClick={() => void logout()}>Изход</Button>
  </section>;
}

export default function ConnectionSettings() {
  const { client } = useAppMode();
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
  return <div className="space-y-6 max-w-3xl"><div><h1 className="text-2xl font-bold">Настройки</h1><p className="mt-2 text-sm text-content-muted">Проверете връзката с API и управлявайте акаунта си.</p></div>
    <section className="bg-surface border border-border rounded-panel p-6 space-y-4"><h2 className="font-semibold">Връзка с API</h2>
      <p className="text-sm text-content-secondary break-all">{DEFAULT_API_URL}</p>
      <Button isLoading={busy} onClick={check}>Провери връзката</Button>
      <ApiProblem error={error} />
      {health && <p role="status" className="text-sm">Работоспособност на API: <span className="text-status-success">{health}</span></p>}
      {ready && <p role="status" className="text-sm">Връзка с базата данни: <span className="text-status-success">{ready}</span></p>}
      <p className="text-xs text-content-muted">Здравата връзка не гарантира удостоверяване, съвместимост на базата или наличност на работника за проучвания. Първата заявка може да отнеме време, докато хостнатото API се събуди.</p>
    </section>
    <AccountSection />
  </div>;
}
