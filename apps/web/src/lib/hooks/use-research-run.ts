"use client";
import { useEffect, useRef, useState } from "react";
import { useAppMode } from "../context/app-mode-context";
import { ApiError } from "../api/client";
import { ResearchRunStatus } from "@/types/api";
export const terminalRun = (job: ResearchRunStatus) => ["completed", "failed", "cancelled"].includes(job.status);
export function useResearchRun(initial: ResearchRunStatus, onCompleted?: () => void, onUpdated?: (job: ResearchRunStatus) => void) {
  const { client } = useAppMode();
  const [job, setJob] = useState(initial);
  const [error, setError] = useState<unknown>(null);
  const [retry, setRetry] = useState(0);
  const callbacks = useRef({ onCompleted, onUpdated });
  callbacks.current = { onCompleted, onUpdated };
  useEffect(() => {
    let disposed = false;
    let timer: ReturnType<typeof setTimeout>;
    let failures = 0;
    setJob(initial); setError(null);
    async function poll() {
      try {
        const fresh = await client.getResearchJob(initial.id);
        if (disposed) return;
        setJob(fresh); setError(null); failures = 0;
        callbacks.current.onUpdated?.(fresh);
        if (terminalRun(fresh)) {
          if (fresh.status === "completed") callbacks.current.onCompleted?.();
          return;
        }
      } catch (failure) {
        if (disposed) return;
        setError(failure); failures++;
        if (failure instanceof ApiError && [400, 401, 403, 404].includes(failure.status)) return;
      }
      timer = setTimeout(poll, Math.min(30000, 2000 * 2 ** Math.min(failures, 4)));
    }
    if (!terminalRun(initial)) timer = setTimeout(poll, 2000);
    return () => { disposed = true; clearTimeout(timer); };
    // The same run's parent status updates must not restart an in-flight poll.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [client, initial.id, initial.status, retry]);
  return { job, error, retry: () => setRetry(value => value + 1) };
}
