import { ArchiveSettings, CreateArchiveRequest, PatchArchiveRequest } from "@/types/api";
import { normalizeDomain, validateDateWindow, validateTitle } from "./normalization";

export const LANGUAGE_CODES = new Set("aa ab ae af ak am an ar as av ay az ba be bg bh bi bm bn bo br bs ca ce ch co cr cs cu cv cy da de dv dz ee el en eo es et eu fa ff fi fj fo fr fy ga gd gl gn gu gv ha he hi ho hr ht hu hy hz ia id ie ig ii ik io is it iu ja jv ka kg ki kj kk kl km kn ko kr ks ku kv kw ky la lb lg li ln lo lt lu lv mg mh mi mk ml mn mr ms mt my na nb nd ne ng nl nn no nr nv ny oc oj om or os pa pi pl ps pt qu rm rn ro ru rw sa sc sd se sg si sk sl sm sn so sq sr ss st su sv sw ta te tg th ti tk tl tn to tr ts tt tw ty ug uk ur uz ve vi vo wa wo xh yi yo za zh zu".split(" "));
export const splitValues = (text: string) => text.split(/[,\n]/).map(s => s.trim()).filter(Boolean);
export function parseConfig(text: string): Record<string, unknown> {
  const value: unknown = JSON.parse(text);
  if (!value || Array.isArray(value) || typeof value !== "object") throw new Error("Config must be a JSON object.");
  if (new TextEncoder().encode(JSON.stringify(value)).length > 16384) throw new Error("Config must be at most 16 KiB in UTF-8.");
  function depth(x: unknown, level: number) {
    if (level > 63) throw new Error("Config exceeds the JSON depth limit.");
    if (x && typeof x === "object") Object.values(x).forEach(v => depth(v, level + 1));
  }
  depth(value, 0);
  return value as Record<string, unknown>;
}
export function validateArchiveInput(request: CreateArchiveRequest): Record<string, string[]> {
  const errors: Record<string, string[]> = {};
  const titleError = validateTitle(request.title);
  if (titleError) errors.title = [titleError];
  if ((request.description?.length || 0) > 10000) errors.description = ["Description cannot exceed 10000 characters."];
  const s = request.settings;
  if (!s) return errors;
  const dateError = validateDateWindow(s.dateFrom, s.dateTo);
  if (dateError) errors["settings.dateFrom"] = [dateError];
  const languages = (s.languages || []).map(v => v.trim().toLowerCase());
  if (languages.length > 100 || languages.some(v => !LANGUAGE_CODES.has(v)) || new Set(languages).size !== languages.length) errors["settings.languages"] = ["Use up to 100 unique ISO 639-1 language codes."];
  const domains: Record<string, string[]> = {};
  for (const field of ["preferredDomains", "excludedDomains"] as const) {
    const values = s[field] || [];
    const normalized = values.map(v => normalizeDomain(v));
    domains[field] = normalized.flatMap(v => v.domain ? [v.domain] : []);
    if (values.length > 100 || normalized.some(v => v.error) || new Set(domains[field]).size !== values.length) errors[`settings.${field}`] = ["Use up to 100 unique DNS domains, without URLs, paths or ports."];
  }
  if (domains.preferredDomains.some(v => domains.excludedDomains.includes(v))) errors["settings.excludedDomains"] = ["Preferred and excluded domains cannot overlap."];
  try { parseConfig(JSON.stringify(s.config || {})); } catch (error) { errors["settings.config"] = [(error as Error).message]; }
  return errors;
}
export function sparseSettingsPatch(original: { title: string; description: string | null; settings: ArchiveSettings }, next: CreateArchiveRequest): PatchArchiveRequest {
  const patch: PatchArchiveRequest = {};
  if (next.title !== original.title) patch.title = next.title;
  if (next.description !== original.description) patch.description = next.description;
  const settings: NonNullable<PatchArchiveRequest["settings"]> = {};
  for (const field of ["dateFrom", "dateTo", "languages", "preferredDomains", "excludedDomains", "config"] as const) {
    if (JSON.stringify(next.settings?.[field]) !== JSON.stringify(original.settings[field])) {
      Object.assign(settings, { [field]: next.settings?.[field] });
    }
  }
  if (Object.keys(settings).length) patch.settings = settings;
  return patch;
}
