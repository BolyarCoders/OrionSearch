"use client";

import React from "react";

/** Safe, intentionally small Markdown renderer for AI briefing text.
 * Never render model-produced HTML or trust raw links.
 */
type Source = { index: number; title: string; url: string };
type Block = { kind: "heading" | "paragraph" | "bullet"; text: string; level?: number };

function safeHttpUrl(raw: string): string | null {
  try {
    const url = new URL(raw);
    return (url.protocol === "https:" || url.protocol === "http:") && url.hostname
      ? url.href : null;
  } catch { return null; }
}

function parseBriefing(value: string): { blocks: Block[]; sources: Source[] } {
  const lines = value.replace(/\r\n/g, "\n").split("\n");
  const blocks: Block[] = [];
  const sources: Source[] = [];
  let paragraph: string[] = [];
  let sourceSection = false;

  const flush = () => {
    if (paragraph.length) blocks.push({ kind: "paragraph", text: paragraph.join(" ") });
    paragraph = [];
  };

  for (const rawLine of lines) {
    const line = rawLine.trim();
    if (/^(?:Източници|Sources)\s*\(\d+\)\s*:/i.test(line) || /^(?:Източници|Sources):$/i.test(line)) {
      flush();
      sourceSection = true;
      continue;
    }
    if (sourceSection) {
      const match = line.match(/^\[(\d+)\]\s+(.+?)\s+[—-]\s+(https?:\/\/\S+)$/);
      if (match && safeHttpUrl(match[3])) {
        sources.push({ index: Number(match[1]), title: match[2], url: match[3] });
      }
      continue;
    }
    if (!line) { flush(); continue; }
    const heading = line.match(/^(#{1,4})\s+(.+)$/);
    if (heading) {
      flush();
      blocks.push({ kind: "heading", text: heading[2], level: heading[1].length });
    } else if (/^[-*]\s+/.test(line)) {
      flush();
      blocks.push({ kind: "bullet", text: line.replace(/^[-*]\s+/, "") });
    } else {
      paragraph.push(line);
    }
  }
  flush();
  return { blocks, sources };
}

function InlineCitations({ text, links, prefix }: {
  text: string; links: Map<number, Source>; prefix: string;
}) {
  const parts = text.split(/(\[\d+\])/g);
  return <>{parts.map((part, i) => {
    const match = part.match(/^\[(\d+)\]$/);
    const source = match ? links.get(Number(match[1])) : undefined;
    return source ? (
      <a
        key={i}
        href={`#${prefix}-source-${source.index}`}
        className="text-accent underline underline-offset-2 hover:opacity-80"
        title={source.title}
        aria-label={`Източник ${source.index}: ${source.title}`}
      >{part}</a>
    ) : <React.Fragment key={i}>{part}</React.Fragment>;
  })}</>;
}

export function ReportContent({ text, runId }: { text: string; runId: string }) {
  const { blocks, sources } = parseBriefing(text);
  const sourceMap = new Map(sources.map(source => [source.index, source]));
  const prefix = `report-${runId}`;

  return (
    <article className="space-y-4 text-sm leading-7 text-content-secondary" aria-label="AI обзор на проучването">
      {blocks.map((block, i) => {
        const content = <InlineCitations text={block.text} links={sourceMap} prefix={prefix} />;
        if (block.kind === "heading") {
          return block.level === 1
            ? <h2 key={i} className="text-lg sm:text-xl font-semibold text-content-primary pt-3">{content}</h2>
            : <h3 key={i} className="text-base font-semibold text-content-primary pt-2">{content}</h3>;
        }
        if (block.kind === "bullet") {
          return <p key={i} className="pl-5 relative before:content-['•'] before:absolute before:left-1">{content}</p>;
        }
        return <p key={i}>{content}</p>;
      })}
      {sources.length > 0 && (
        <section className="pt-5 mt-6 border-t border-border/80 space-y-3" aria-label="Цитирани източници">
          <h3 className="font-semibold text-content-primary">Източници ({sources.length})</h3>
          <ol className="space-y-2">
            {sources.map(source => {
              const href = safeHttpUrl(source.url);
              if (!href) return null;
              return (
                <li key={source.index} id={`${prefix}-source-${source.index}`} className="scroll-mt-20 text-xs sm:text-sm">
                  <span className="text-content-muted mr-2">[{source.index}]</span>
                  <a href={href} target="_blank" rel="noopener noreferrer" className="text-accent hover:underline break-words">
                    {source.title}
                  </a>
                </li>
              );
            })}
          </ol>
        </section>
      )}
    </article>
  );
}
