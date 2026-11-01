"use client";

import React, { useState } from "react";
import { useRouter } from "next/navigation";
import { useAppMode } from "@/lib/context/app-mode-context";
import { ApiProblem } from "../ui/api-problem";
import { ApiError } from "@/lib/api/client";
import { parseConfig, splitValues, validateArchiveInput } from "@/lib/archive-validation";
import { CreateArchiveRequest } from "@/types/api";
import {
  normalizeDomain,
  validateDateWindow,
  validateTitle,
} from "@/lib/normalization";
import { Button } from "../ui/button";
import { Input } from "../ui/input";
import { Textarea } from "../ui/textarea";
import { ArchiveCardPreview } from "./archive-card-preview";
import { TemplateSelector } from "./template-selector";
import {
  Check,
  ChevronRight,
  ArrowLeft,
  Calendar,
  Globe,
  Settings,
  Plus,
  X,
  AlertCircle,
  FileCheck,
} from "lucide-react";

export function CreateArchiveStepper() {
  const router = useRouter();
  const { client } = useAppMode();

  const [step, setStep] = useState<number>(1);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [formError, setFormError] = useState<unknown>(null);
  const [extraConfig, setExtraConfig] = useState("{}");

  // Form State
  const [title, setTitle] = useState("");
  const [titleError, setTitleError] = useState<string | null>(null);
  const [description, setDescription] = useState("");
  const [selectedTemplateKey, setSelectedTemplateKey] = useState<string | undefined>();

  const [dateFrom, setDateFrom] = useState("");
  const [dateTo, setDateTo] = useState("");
  const [dateError, setDateError] = useState<string | null>(null);

  const [preferredDomains, setPreferredDomains] = useState<string[]>([]);
  const [domainInput, setDomainInput] = useState("");
  const [domainInputError, setDomainInputError] = useState<string | null>(null);

  const [excludedDomains, setExcludedDomains] = useState<string[]>([]);
  const [excludedDomainInput, setExcludedDomainInput] = useState("");
  const [excludedDomainError, setExcludedDomainError] = useState<string | null>(null);

  const [languageText, setLanguageText] = useState("en");
  const [languages, setLanguages] = useState<string[]>(["en"]);
  const [frequency, setFrequency] = useState<string>("weekly");

  const steps = [
    { num: 1, title: "Основна информация", desc: "Тема и описание" },
    { num: 2, title: "Период", desc: "Граници на датите" },
    { num: 3, title: "Източници", desc: "Предпочитани и изключени домейни" },
    { num: 4, title: "Настройки", desc: "Езици и честота" },
    { num: 5, title: "Преглед", desc: "Потвърждение и създаване" },
  ];

  // Domain addition helper
  const handleAddPreferredDomain = () => {
    if (!domainInput.trim()) return;
    const { domain, error } = normalizeDomain(domainInput);
    if (error || !domain) {
      setDomainInputError(error || "Невалиден домейн");
      return;
    }
    if (preferredDomains.includes(domain)) {
      setDomainInputError("Домейнът вече е добавен");
      return;
    }
    if (excludedDomains.includes(domain)) {
      setDomainInputError("Домейнът не може да е едновременно предпочитан и изключен");
      return;
    }
    if (preferredDomains.length >= 100) { setDomainInputError("Позволени са най-много 100 домейна."); return; }
    setPreferredDomains([...preferredDomains, domain]);
    setDomainInput("");
    setDomainInputError(null);
  };

  const handleAddExcludedDomain = () => {
    if (!excludedDomainInput.trim()) return;
    const { domain, error } = normalizeDomain(excludedDomainInput);
    if (error || !domain) {
      setExcludedDomainError(error || "Невалиден домейн");
      return;
    }
    if (excludedDomains.includes(domain)) {
      setExcludedDomainError("Домейнът вече е в списъка с изключени");
      return;
    }
    if (preferredDomains.includes(domain)) {
      setExcludedDomainError("Домейнът не може да е едновременно предпочитан и изключен");
      return;
    }
    if (excludedDomains.length >= 100) { setExcludedDomainError("Позволени са най-много 100 домейна."); return; }
    setExcludedDomains([...excludedDomains, domain]);
    setExcludedDomainInput("");
    setExcludedDomainError(null);
  };

  // Step validation
  const validateStep = (currentStep: number): boolean => {
    setFormError(null);
    if (currentStep === 1) {
      const err = validateTitle(title);
      if (err) {
        setTitleError(err);
        return false;
      }
      setTitleError(null);
      return true;
    }

    if (currentStep === 2) {
      const err = validateDateWindow(dateFrom || null, dateTo || null);
      if (err) {
        setDateError(err);
        return false;
      }
      setDateError(null);
      return true;
    }

    if (currentStep === 3 && (domainInput.trim() || excludedDomainInput.trim())) { setFormError(new Error("Добавете въведения домейн към списъка или го изчистете, преди да продължите.")); return false; }
    return true;
  };

  const handleNext = () => {
    if (validateStep(step)) {
      setStep((prev) => Math.min(prev + 1, 5));
    }
  };

  const handleBack = () => {
    setStep((prev) => Math.max(prev - 1, 1));
  };

  const handleSubmit = async () => {
    if (!validateStep(1) || !validateStep(2) || !validateStep(3)) return;

    if (isSubmitting) return;
    setFormError(null);

    const payload: CreateArchiveRequest = {
      title: title.trim(),
      description: description.length ? description : null,
      settings: {
        dateFrom: dateFrom || null,
        dateTo: dateTo || null,
        languages,
        preferredDomains,
        excludedDomains,
        config: {
          ...(selectedTemplateKey ? { templateKey: selectedTemplateKey } : {}),
          updateFrequency: frequency,
        },
      },
    };

    try {
      payload.settings!.config = { ...parseConfig(extraConfig), ...payload.settings!.config };
      const errors = validateArchiveInput(payload);
      if (Object.keys(errors).length) throw new ApiError(400, "Проверете конфигурацията на архива.", { errors });
      setIsSubmitting(true);
      const result = await client.createArchive(payload);
      router.push(`/archives/${result.archive.id}`);
    } catch (err: unknown) {
      setIsSubmitting(false);
      setFormError(err);
    }
  };

  return (
    <div className="grid grid-cols-1 lg:grid-cols-12 gap-8 items-start">
      <div className="lg:hidden space-y-3" aria-live="polite">
        <div className="flex items-center justify-between gap-3 text-sm"><span className="font-medium">{steps[step - 1].title}</span><span className="text-content-muted">Стъпка {step} от {steps.length}</span></div>
        <div className="flex gap-1.5" aria-hidden="true">{steps.map(s => <span key={s.num} className={`h-1 flex-1 rounded-full ${s.num <= step ? "bg-accent" : "bg-border"}`} />)}</div>
      </div>
      {/* Left Stepper Indicator (desktop) */}
      <div className="hidden lg:block lg:col-span-3 bg-surface border border-border rounded-panel p-4 space-y-1">
        {steps.map((s) => {
          const isDone = s.num < step;
          const isCurrent = s.num === step;
          return (
            <button
              key={s.num}
              type="button"
              onClick={() => {
                if (isDone) setStep(s.num);
              }}
              disabled={!isDone && !isCurrent}
              className={`w-full flex items-start gap-3 p-3 rounded-control text-left transition-colors ${
                isCurrent
                  ? "bg-accent/15 border border-accent/30 text-content-primary"
                  : isDone
                  ? "hover:bg-surface-raised text-content-secondary cursor-pointer"
                  : "cursor-not-allowed text-content-muted"
              }`}
            >
              <div
                className={`w-6 h-6 rounded-full flex items-center justify-center text-xs font-semibold shrink-0 mt-0.5 ${
                  isCurrent
                    ? "bg-accent text-on-accent"
                    : isDone
                    ? "bg-status-success/20 text-status-success border border-status-success/40"
                    : "bg-surface-raised border border-border text-content-muted"
                }`}
              >
                {isDone ? <Check className="w-3.5 h-3.5" /> : s.num}
              </div>
              <div>
                <div className="text-xs font-semibold">{s.title}</div>
                <div className="text-xs text-content-muted">{s.desc}</div>
              </div>
            </button>
          );
        })}
      </div>

      {/* Main Step Form Area */}
      <div className="lg:col-span-6 bg-surface border border-border rounded-panel p-6 flex flex-col justify-between min-h-[520px]">
        <div>
          <ApiProblem error={formError} />

          {/* STEP 1: Basic Information */}
          {step === 1 && (
            <div className="space-y-5">
              <div>
                <h3 className="text-base font-semibold text-content-primary">Основна информация</h3>
                <p className="text-xs text-content-muted mt-0.5">
                  Задайте заглавие и общ фокус на темата за проучване.
                </p>
              </div>

              <Input
                label="Заглавие на архива"
                required
                maxLength={200}
                placeholder="напр. Изкуствен интелект"
                value={title}
                error={titleError || undefined}
                onChange={(e) => {
                  setTitle(e.target.value);
                  if (titleError) setTitleError(null);
                }}
              />

              <Textarea
                label="Описание"
                placeholder="Опишете какво искате да проучите, конкретни въпроси или обхват…"
                value={description}
                maxChars={10000}
                onChange={(e) => setDescription(e.target.value)}
              />

              <div className="pt-2">
                <TemplateSelector
                  selectedKey={selectedTemplateKey}
                  onSelect={(preset) => {
                    setTitle(preset.title);
                    setDescription(preset.description);
                    setPreferredDomains(preset.preferredDomains);
                    setLanguages(preset.languages);
                    setLanguageText(preset.languages.join(", "));
                    setSelectedTemplateKey(preset.key);
                    if (titleError) setTitleError(null);
                  }}
                />
              </div>
            </div>
          )}

          {/* STEP 2: Time Period */}
          {step === 2 && (
            <div className="space-y-5">
              <div>
                <h3 className="text-base font-semibold text-content-primary">Период</h3>
                <p className="text-xs text-content-muted mt-0.5">
                  Задайте по избор граници на датата на публикуване (UTC).
                </p>
              </div>

              <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                <Input
                  label="От дата"
                  type="date"
                  value={dateFrom}
                  onChange={(e) => {
                    setDateFrom(e.target.value);
                    if (dateError) setDateError(null);
                  }}
                  leftIcon={<Calendar className="w-4 h-4" />}
                />
                <Input
                  label="До дата"
                  type="date"
                  value={dateTo}
                  onChange={(e) => {
                    setDateTo(e.target.value);
                    if (dateError) setDateError(null);
                  }}
                  leftIcon={<Calendar className="w-4 h-4" />}
                />
              </div>

              {dateError && <p className="text-xs text-status-failure">{dateError}</p>}

              {(dateFrom || dateTo) && (
                <button
                  type="button"
                  onClick={() => {
                    setDateFrom("");
                    setDateTo("");
                    setDateError(null);
                  }}
                  className="text-xs text-accent hover:underline flex items-center gap-1"
                >
                  <X className="w-3.5 h-3.5" /> Изчисти периода
                </button>
              )}

              <div className="p-3.5 rounded-control bg-surface-raised border border-border text-xs text-content-muted leading-relaxed">
                Празни дати означават търсене във всички периоди. Източници без дата на публикуване ще бъдат отбелязани като <span className="text-content-primary font-mono text-xs">Неизвестна</span>.
              </div>
            </div>
          )}

          {/* STEP 3: Sources / Domains */}
          {step === 3 && (
            <div className="space-y-6">
              <div>
                <h3 className="text-base font-semibold text-content-primary">Източници и домейни</h3>
                <p className="text-xs text-content-muted mt-0.5">
                  Посочете доверени домейни и домейни, които да се избягват.
                </p>
              </div>

              {/* Preferred Domains */}
              <div className="space-y-2">
                <label className="block text-xs font-medium text-content-secondary">
                  Предпочитани домейни (с приоритет)
                </label>
                <div className="flex gap-2">
                  <Input
                    placeholder="напр. arxiv.org, nature.com"
                    value={domainInput}
                    error={domainInputError || undefined}
                    onChange={(e) => {
                      setDomainInput(e.target.value);
                      if (domainInputError) setDomainInputError(null);
                    }}
                    onKeyDown={(e) => {
                      if (e.key === "Enter") {
                        e.preventDefault();
                        handleAddPreferredDomain();
                      }
                    }}
                    leftIcon={<Globe className="w-4 h-4" />}
                  />
                  <Button
                    type="button"
                    variant="secondary"
                    size="sm"
                    onClick={handleAddPreferredDomain}
                  >
                    <Plus className="w-4 h-4" /> Добави
                  </Button>
                </div>

                <div className="flex flex-wrap gap-1.5 pt-1">
                  {preferredDomains.length === 0 ? (
                    <span className="text-xs text-content-muted">Няма предпочитани домейни.</span>
                  ) : (
                    preferredDomains.map((d) => (
                      <span
                        key={d}
                        className="inline-flex items-center gap-1 bg-accent/15 border border-accent/30 text-accent text-xs px-2.5 py-1 rounded-full font-mono"
                      >
                        {d}
                        <button
                          type="button"
                          onClick={() => setPreferredDomains(preferredDomains.filter((x) => x !== d))}
                          className="hover:text-content-primary p-0.5"
                        >
                          <X className="w-3 h-3" />
                        </button>
                      </span>
                    ))
                  )}
                </div>
              </div>

              {/* Excluded Domains */}
              <div className="space-y-2 pt-2 border-t border-border/80">
                <label className="block text-xs font-medium text-content-secondary">
                  Изключени домейни (игнорират се)
                </label>
                <div className="flex gap-2">
                  <Input
                    placeholder="напр. spam-aggregator.com"
                    value={excludedDomainInput}
                    error={excludedDomainError || undefined}
                    onChange={(e) => {
                      setExcludedDomainInput(e.target.value);
                      if (excludedDomainError) setExcludedDomainError(null);
                    }}
                    onKeyDown={(e) => {
                      if (e.key === "Enter") {
                        e.preventDefault();
                        handleAddExcludedDomain();
                      }
                    }}
                  />
                  <Button
                    type="button"
                    variant="secondary"
                    size="sm"
                    onClick={handleAddExcludedDomain}
                  >
                    <Plus className="w-4 h-4" /> Добави
                  </Button>
                </div>

                <div className="flex flex-wrap gap-1.5 pt-1">
                  {excludedDomains.length === 0 ? (
                    <span className="text-xs text-content-muted">Няма изключени домейни.</span>
                  ) : (
                    excludedDomains.map((d) => (
                      <span
                        key={d}
                        className="inline-flex items-center gap-1 bg-status-failure/15 border border-status-failure/30 text-status-failure text-xs px-2.5 py-1 rounded-full font-mono"
                      >
                        {d}
                        <button
                          type="button"
                          onClick={() => setExcludedDomains(excludedDomains.filter((x) => x !== d))}
                          className="hover:text-content-primary p-0.5"
                        >
                          <X className="w-3 h-3" />
                        </button>
                      </span>
                    ))
                  )}
                </div>
              </div>
            </div>
          )}

          {/* STEP 4: Settings & Cadence */}
          {step === 4 && (
            <div className="space-y-5">
              <div>
                <h3 className="text-base font-semibold text-content-primary">Настройки и предпочитания</h3>
                <p className="text-xs text-content-muted mt-0.5">
                  Задайте езиков обхват и предпочитана честота на обновяване.
                </p>
              </div>

              <Input label="Езици (ISO 639-1)" value={languageText} onChange={e => { setLanguageText(e.target.value); setLanguages(splitValues(e.target.value).map(s => s.toLowerCase())); }} hint="Кодове, разделени със запетая, напр. en, bg, es. Оставете празно за без предпочитание." />
              <Textarea label="Допълнителни предпочитания (JSON обект)" value={extraConfig} onChange={e => setExtraConfig(e.target.value)} hint="Допълнителни предпочитания, до 16 KiB. Избраният шаблон и честота се записват заедно с тях." className="font-mono text-xs" />
              {/* Cadence preference */}
              <div className="pt-2 border-t border-border/80">
                <label className="block text-xs font-medium text-content-secondary mb-1">
                  Предпочитана честота на проучване
                </label>
                <select
                  value={frequency}
                  onChange={(e) => setFrequency(e.target.value)}
                  className="w-full bg-surface-raised border border-border text-content-primary text-xs rounded-control p-2.5 focus:border-accent focus:outline-none"
                >
                  <option value="daily">Ежедневно</option>
                  <option value="weekly">Седмично</option>
                  <option value="monthly">Месечно</option>
                </select>
                <p className="text-xs text-content-muted mt-1.5 leading-relaxed">
                  Записва се като предпочитание в <code className="text-accent text-xs">settings.config</code>. Забележка: автоматично планиране на проучвания засега не е активно; проучването се стартира ръчно.
                </p>
              </div>
            </div>
          )}

          {/* STEP 5: Review & Confirm */}
          {step === 5 && (
            <div className="space-y-4">
              <div>
                <h3 className="text-base font-semibold text-content-primary">Преглед на конфигурацията</h3>
                <p className="text-xs text-content-muted mt-0.5">
                  Потвърдете параметрите, преди да създадете архива.
                </p>
              </div>

              <div className="bg-surface-raised border border-border rounded-control p-4 space-y-3 text-xs">
                <div>
                  <div className="text-content-muted text-xs">Заглавие на темата</div>
                  <div className="font-semibold text-content-primary mt-0.5">{title}</div>
                </div>

                {description && (
                  <div>
                    <div className="text-content-muted text-xs">Описание</div>
                    <div className="text-content-secondary mt-0.5">{description}</div>
                  </div>
                )}

                <div className="grid grid-cols-2 gap-2 pt-2 border-t border-border/60">
                  <div>
                    <div className="text-content-muted text-xs">Период</div>
                    <div className="text-content-secondary mt-0.5">
                      {dateFrom || dateTo ? `${dateFrom || "—"} до ${dateTo || "днес"}` : "Без ограничение"}
                    </div>
                  </div>
                  <div>
                    <div className="text-content-muted text-xs">Езици</div>
                    <div className="text-content-secondary mt-0.5">
                      {languages.join(", ").toUpperCase()}
                    </div>
                  </div>
                </div>

                <div className="pt-2 border-t border-border/60">
                  <div className="text-content-muted text-xs">Предпочитани домейни</div>
                  <div className="text-content-secondary mt-0.5">
                    {preferredDomains.length > 0 ? preferredDomains.join(", ") : "Няма зададени"}
                  </div>
                </div>

                <div className="pt-2 border-t border-border/60">
                  <div className="text-content-muted text-xs">Честота на обновяване</div>
                  <div className="text-content-secondary mt-0.5 capitalize">
                    {({ daily: "ежедневно", weekly: "седмично", monthly: "месечно" } as Record<string, string>)[frequency] || frequency} (засега ръчно изпълнение)
                  </div>
                </div>
              </div>
            </div>
          )}
        </div>

        {/* Bottom Navigation Buttons */}
        <div className="pt-6 border-t border-border/80 flex items-center justify-between gap-3 mt-6">
          {step > 1 ? (
            <Button
              type="button"
              variant="secondary"
              size="sm"
              leftIcon={<ArrowLeft className="w-3.5 h-3.5" />}
              onClick={handleBack}
            >
              Назад
            </Button>
          ) : (
            <Button
              type="button"
              variant="ghost"
              size="sm"
              onClick={() => router.push("/archives")}
            >
              Отказ
            </Button>
          )}

          {step < 5 ? (
            <Button
              type="button"
              variant="primary"
              size="sm"
              rightIcon={<ChevronRight className="w-3.5 h-3.5" />}
              onClick={handleNext}
            >
              Продължи
            </Button>
          ) : (
            <Button
              type="button"
              variant="primary"
              size="sm"
              isLoading={isSubmitting}
              leftIcon={<FileCheck className="w-4 h-4" />}
              onClick={handleSubmit}
            >
              Създай архив
            </Button>
          )}
        </div>
      </div>

      {/* Right Live Preview Column (desktop) */}
      <div className="lg:col-span-3 sticky top-24">
        <ArchiveCardPreview
          title={title}
          description={description}
          templateKey={selectedTemplateKey}
        />
      </div>
    </div>
  );
}
