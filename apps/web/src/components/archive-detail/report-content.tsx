"use client";

import React from "react";

/** Safe, intentionally small Markdown renderer for AI briefing text.
 * Never render model-produced HTML or trust raw links.
 */
type Source = { index: number; title: string; url: string };
type Block =
  | { kind: "heading" | "paragraph" | "bullet"; text: string; level?: number }
  | { kind: "ordered"; start: number; items: string[] };

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
  let orderedOpen = false;

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
    if (!line) { flush(); orderedOpen = false; continue; }
    if (!/^\d+\.\s+/.test(line)) orderedOpen = false;
    const heading = line.match(/^(#{1,4})\s+(.+)$/);
    if (heading) {
      flush();
      blocks.push({ kind: "heading", text: heading[2], level: heading[1].length });
    } else if (/^\d+\.\s+/.test(line)) {
      flush();
      const match = line.match(/^(\d+)\.\s+(.+)$/)!;
      const number = Number(match[1]);
      const previous = blocks[blocks.length - 1];
      if (orderedOpen && previous?.kind === "ordered") previous.items.push(match[2]);
      else blocks.push({ kind: "ordered", start: number, items: [match[2]] });
      orderedOpen = true;
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

function InlineContent({ text, links, prefix }: {
  text: string; links: Map<number, Source>; prefix: string;
}) {
  const parts = text.split(/(`[^`\n]+`|\*\*[^*\n]+\*\*|__[^_\n]+__|\*[^*\n]+\*|_[^_\n]+_|\[\d+\])/g);
  return <>{parts.map((part, i) => {
    if (part.startsWith("`") && part.endsWith("`") && part.length > 2) return <code key={i}>{part.slice(1, -1)}</code>;
    if (/^(?:\*\*[^*\n]+\*\*|__[^_\n]+__)$/.test(part)) {
      return <strong key={i}><InlineContent text={part.slice(2, -2)} links={links} prefix={prefix} /></strong>;
    }
    if (/^(?:\*[^*\n]+\*|_[^_\n]+_)$/.test(part)) {
      return <em key={i}><InlineContent text={part.slice(1, -1)} links={links} prefix={prefix} /></em>;
    }
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
        if (block.kind === "ordered") {
          return <ol key={i} start={block.start} className="list-decimal pl-6 space-y-1">
            {block.items.map((item, index) => <li key={index}><InlineContent text={item} links={sourceMap} prefix={prefix} /></li>)}
          </ol>;
        }
        const content = <InlineContent text={block.text} links={sourceMap} prefix={prefix} />;
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
