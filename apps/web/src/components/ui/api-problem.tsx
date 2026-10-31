"use client";
import { ApiError } from "@/lib/api/client";
import { Button } from "./button";
export function ApiProblem({ error, onRetry }: { error: unknown; onRetry?: () => void }) {
  if (!error) return null;
  const api = error instanceof ApiError ? error : null;
  const help: Record<number, string> = { 400: "Check the fields below and correct your request.", 401: "Sign-in is required. The API's authentication provider must be configured before live access is available.", 403: "Your archive role does not allow this action.", 404: "This resource is unavailable or you do not have access.", 409: "The request conflicts with the archive state or an earlier request. Review it before trying again.", 500: "An unexpected server error occurred. Keep the trace ID when reporting it.", 503: "The service is unavailable. Check your connection and try again." };
  return <div role="alert" className="p-4 rounded-control bg-status-failure/10 border border-status-failure/30 text-xs space-y-2">
    <p className="font-semibold text-status-failure">{error instanceof Error ? error.message : String(error)}</p>
    {api && <p className="text-content-secondary">{help[api.status] || `Request failed (${api.status}).`}</p>}
    {api?.problem?.errors && <ul className="text-content-secondary space-y-1">{Object.entries(api.problem.errors).map(([field, messages]) => <li key={field}><strong>{field}: </strong>{messages.join(" ")}</li>)}</ul>}
    {api?.problem?.traceId && <p className="text-content-muted break-all font-mono">Trace ID: {api.problem.traceId}</p>}
    {onRetry && <Button variant="secondary" size="sm" onClick={onRetry}>Try again</Button>}
  </div>;
}
