"use client";
import React, { useState } from "react";
import { ArchiveDetails, CreateArchiveRequest } from "@/types/api";
import { useAppMode } from "@/lib/context/app-mode-context";
import { Button } from "../ui/button";
import { Input } from "../ui/input";
import { Textarea } from "../ui/textarea";
import { ApiProblem } from "../ui/api-problem";
import { ApiError } from "@/lib/api/client";
import { parseConfig, sparseSettingsPatch, splitValues, validateArchiveInput } from "@/lib/archive-validation";
import { normalizeDomain } from "@/lib/normalization";
import { Save } from "lucide-react";

export function SettingsPanel({ details, onUpdate }: { details: ArchiveDetails; onUpdate: (updated: ArchiveDetails) => void }) {
  const { client } = useAppMode();
  const { archive, settings } = details;
  const [title, setTitle] = useState(archive.title);
  const [description, setDescription] = useState(archive.description || "");
  const [dateFrom, setDateFrom] = useState(settings.dateFrom || "");
  const [dateTo, setDateTo] = useState(settings.dateTo || "");
  const [languages, setLanguages] = useState(settings.languages.join(", "));
  const [preferred, setPreferred] = useState(settings.preferredDomains.join(", "));
  const [excluded, setExcluded] = useState(settings.excludedDomains.join(", "));
  const [config, setConfig] = useState(JSON.stringify(settings.config, null, 2));
  const [isSaving, setIsSaving] = useState(false);
  const [success, setSuccess] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const readOnly = archive.role === "viewer";
  async function save(event: React.FormEvent) {
    event.preventDefault();
    if (readOnly || isSaving) return;
    setError(null); setSuccess(false);
    try {
      const request: CreateArchiveRequest = { title: title.trim(), description: description.length ? description : null, settings: { dateFrom: dateFrom || null, dateTo: dateTo || null, languages: splitValues(languages).map(s => s.toLowerCase()), preferredDomains: splitValues(preferred), excludedDomains: splitValues(excluded), config: parseConfig(config) } };
      const errors = validateArchiveInput(request);
      if (Object.keys(errors).length) throw new ApiError(400, "Проверете настройките на архива.", { errors });
      request.settings!.preferredDomains = request.settings!.preferredDomains!.map(s => normalizeDomain(s).domain!);
      request.settings!.excludedDomains = request.settings!.excludedDomains!.map(s => normalizeDomain(s).domain!);
      const patch = sparseSettingsPatch({ title: archive.title, description: archive.description, settings }, request);
      if (!Object.keys(patch).length) { setSuccess(true); return; }
      setIsSaving(true);
      onUpdate(await client.patchArchive(archive.id, patch));
      setSuccess(true);
    } catch (err) { setError(err); }
    finally { setIsSaving(false); }
  }
  return <div className="bg-surface border border-border rounded-panel p-5 sm:p-6 max-w-3xl">
    <form onSubmit={save} className="space-y-5" onChange={() => setSuccess(false)}>
      <div><h3 className="text-base font-semibold">Настройки на архива</h3><p className="text-xs text-content-muted mt-1">{readOnly ? "Имате достъп само за четене до този архив." : "Променете данните на архива и предпочитанията за бъдещи проучвания."}</p></div>
      <ApiProblem error={error} />
      {success && <p role="status" className="text-sm text-status-success">Настройките са запазени.</p>}
      <fieldset disabled={readOnly || isSaving} className="space-y-5 disabled:opacity-70">
        <Input label="Заглавие на архива" required maxLength={200} value={title} onChange={e => setTitle(e.target.value)} />
        <Textarea label="Описание на темата" maxChars={10000} value={description} onChange={e => setDescription(e.target.value)} hint="Изчистете полето, за да премахнете описанието." />
        <div className="grid sm:grid-cols-2 gap-3"><Input type="date" label="Проучване от" value={dateFrom} onChange={e => setDateFrom(e.target.value)} /><Input type="date" label="Проучване до" value={dateTo} onChange={e => setDateTo(e.target.value)} /></div>
        <Input label="Езици (ISO 639-1)" value={languages} onChange={e => setLanguages(e.target.value)} hint="Двубуквени кодове, разделени със запетая, напр. en, bg. Оставете празно за без предпочитание." />
        <Textarea label="Предпочитани домейни" value={preferred} onChange={e => setPreferred(e.target.value)} hint="DNS домейни, разделени със запетая или нов ред; до 100." />
        <Textarea label="Изключени домейни" value={excluded} onChange={e => setExcluded(e.target.value)} hint="Само DNS домейни. Предпочитаните и изключените не могат да се припокриват." />
        <Textarea label="Допълнителни предпочитания (JSON обект)" value={config} onChange={e => setConfig(e.target.value)} className="font-mono text-xs" hint="До 16 KiB. При редакция обектът се заменя изцяло; запазете другите предпочитания, които искате. Записана честота не включва планиране." />
      </fieldset>
      {!readOnly && <div className="flex justify-end border-t border-border pt-4"><Button type="submit" isLoading={isSaving} leftIcon={<Save className="w-4 h-4" />}>Запази настройките</Button></div>}
    </form>
  </div>;
}
