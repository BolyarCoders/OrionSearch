"use client";
import React, { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { useAuth } from "@/lib/context/auth-context";
import { useAppMode } from "@/lib/context/app-mode-context";
import { Input } from "@/components/ui/input";
import { Button } from "@/components/ui/button";
import { ApiProblem } from "@/components/ui/api-problem";

export default function LoginPage() {
  const { user, login, register } = useAuth();
  const { isDemo, setMode } = useAppMode();
  const router = useRouter();
  const [creating, setCreating] = useState(false);
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);

  useEffect(() => { if (user || isDemo) router.replace("/archives"); }, [user, isDemo, router]);

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    setBusy(true); setError(null);
    try {
      await (creating ? register(email.trim(), password) : login(email.trim(), password));
      router.replace("/archives");
    } catch (failure) { setError(failure); }
    finally { setBusy(false); }
  }

  return (
    <main className="min-h-screen flex items-center justify-center p-4 bg-workspace text-content-primary">
      <form onSubmit={submit} className="w-full max-w-sm bg-surface border border-border rounded-panel p-6 space-y-4">
        <div>
          <h1 className="text-xl font-semibold">{creating ? "Create your account" : "Sign in to OrionSearch"}</h1>
          <p className="text-xs text-content-muted mt-1">{creating ? "Use a password of 12–128 characters with upper and lower case letters, a digit and a symbol." : "Use the account you registered with."}</p>
        </div>
        <ApiProblem error={error} />
        <Input label="Email" type="email" autoComplete="email" required maxLength={254} value={email} onChange={e => setEmail(e.target.value)} />
        <Input label="Password" type="password" autoComplete={creating ? "new-password" : "current-password"} required minLength={creating ? 12 : undefined} maxLength={128} value={password} onChange={e => setPassword(e.target.value)} />
        <Button type="submit" className="w-full" isLoading={busy}>{creating ? "Create account" : "Sign in"}</Button>
        <div className="flex justify-between text-xs">
          <button type="button" className="text-accent hover:underline" onClick={() => { setCreating(!creating); setError(null); }}>{creating ? "Have an account? Sign in" : "New here? Create an account"}</button>
          <button type="button" className="text-content-muted hover:underline" onClick={() => setMode("demo")}>Try Demo</button>
        </div>
      </form>
    </main>
  );
}
