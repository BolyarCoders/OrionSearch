"use client";
import { ApiError } from "@/lib/api/client";
import { Button } from "./button";
export function ApiProblem({ error, onRetry }: { error: unknown; onRetry?: () => void }) {
  if (!error) return null;
  const api = error instanceof ApiError ? error : null;
  const help: Record<number, string> = { 400: "Проверете полетата и коригирайте заявката.", 401: "Влезте отново, за да продължите.", 403: "Ролята ви в архива не позволява това действие.", 404: "Ресурсът не е наличен или нямате достъп.", 409: "Заявката е в конфликт със състоянието на архива или с предишна заявка. Прегледайте я, преди да опитате пак.", 500: "Неочаквана грешка на сървъра. Запазете идентификатора за проследяване при подаване на сигнал.", 503: "Услугата не е достъпна. Проверете връзката и опитайте отново." };
  return <div role="alert" className="p-4 rounded-control bg-status-failure/10 border border-status-failure/30 text-xs space-y-2">
    <p className="font-semibold text-status-failure">{error instanceof Error ? error.message : String(error)}</p>
    {api && <p className="text-content-secondary">{help[api.status] || `Заявката неуспешна (${api.status}).`}</p>}
    {api?.problem?.errors && <ul className="text-content-secondary space-y-1">{Object.entries(api.problem.errors).map(([field, messages]) => <li key={field}><strong>{field}: </strong>{messages.join(" ")}</li>)}</ul>}
    {api?.problem?.traceId && <p className="text-content-muted break-all font-mono">Идентификатор за проследяване: {api.problem.traceId}</p>}
    {onRetry && <Button variant="secondary" size="sm" onClick={onRetry}>Опитай отново</Button>}
  </div>;
}
