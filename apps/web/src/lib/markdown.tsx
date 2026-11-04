import React from "react";

/**
 * Minimal, safe Markdown renderer for AI research briefings.
 * Output is built only from React elements (never raw HTML), so any markup in
 * the source text stays escaped. Links are restricted to http(s).
 */
const INLINE = /(\*\*[^*]+\*\*|\*[^*\s][^*]*\*|`[^`]+`|\[[^\]]+\]\(https?:\/\/[^\s)]+\)|https?:\/\/[^\s<>)]+)/g;

function inline(text: string, keyPrefix: string): React.ReactNode[] {
  return text.split(INLINE).map((part, i) => {
    const key = `${keyPrefix}-${i}`;
    if (!part) return null;
    if (part.startsWith("**") && part.endsWith("**") && part.length > 4)
      return <strong key={key}>{part.slice(2, -2)}</strong>;
    if (part.startsWith("`") && part.endsWith("`") && part.length > 2)
      return <code key={key} className="px-1 rounded bg-surface-raised text-[0.85em]">{part.slice(1, -1)}</code>;
    if (part.startsWith("*") && part.endsWith("*") && part.length > 2)
      return <em key={key}>{part.slice(1, -1)}</em>;
    const link = /^\[([^\]]+)\]\((https?:\/\/[^\s)]+)\)$/.exec(part);
    if (link) return <ExternalLink key={key} href={link[2]}>{link[1]}</ExternalLink>;
    if (/^https?:\/\//.test(part)) return <ExternalLink key={key} href={part}>{part}</ExternalLink>;
    return <React.Fragment key={key}>{part}</React.Fragment>;
  });
}

function ExternalLink({ href, children }: { href: string; children: React.ReactNode }) {
  return (
    <a href={href} target="_blank" rel="noopener noreferrer nofollow" className="underline underline-offset-2 break-words text-accent hover:text-accent-hover">
      {children}
    </a>
  );
}

export function renderMarkdown(source: string): React.ReactNode[] {
  const lines = source.replace(/\r\n?/g, "\n").split("\n");
  const out: React.ReactNode[] = [];
  let para: string[] = [];
  let list: { ordered: boolean; items: string[] } | null = null;

  const flushPara = () => {
    if (para.length) {
      out.push(<p key={`p${out.length}`}>{inline(para.join(" "), `p${out.length}`)}</p>);
      para = [];
    }
  };
  const flushList = () => {
    if (!list) return;
    const Tag = list.ordered ? "ol" : "ul";
    const k = `l${out.length}`;
    out.push(
      <Tag key={k} className={`${list.ordered ? "list-decimal" : "list-disc"} pl-5 space-y-1`}>
        {list.items.map((item, i) => <li key={i}>{inline(item, `${k}-${i}`)}</li>)}
      </Tag>
    );
    list = null;
  };

  for (const raw of lines) {
    const line = raw.trimEnd();
    const heading = /^(#{1,6})\s+(.*)$/.exec(line);
    const bullet = /^\s*[-*•]\s+(.*)$/.exec(line);
    const numbered = /^\s*\d+[.)]\s+(.*)$/.exec(line);
    if (!line.trim()) { flushPara(); flushList(); continue; }
    if (heading) {
      flushPara(); flushList();
      const level = heading[1].length;
      const cls = level <= 2
        ? "text-base font-semibold text-content-primary pt-2"
        : "text-sm font-semibold text-content-primary pt-1";
      out.push(React.createElement(level <= 2 ? "h5" : "h6", { key: `h${out.length}`, className: cls }, inline(heading[2], `h${out.length}`)));
      continue;
    }
    if (bullet || numbered) {
      flushPara();
      const ordered = !bullet;
      if (list && list.ordered !== ordered) flushList();
      if (!list) list = { ordered, items: [] };
      list.items.push((bullet ?? numbered)![1]);
      continue;
    }
    flushList();
    para.push(line.trim());
  }
  flushPara(); flushList();
  return out;
}

/** Splits the mechanically appended "Източници (N):" bibliography from the prose. */
export function splitReport(text: string): { body: string; sources: { index: number; title: string; url: string }[] } {
  const match = /\n+Източници \(\d+\):\n/.exec(text);
  if (!match) return { body: text, sources: [] };
  const body = text.slice(0, match.index);
  const sources = text.slice(match.index + match[0].length).split("\n").flatMap(line => {
    const m = /^\[(\d+)\]\s+(.*?)\s+[—-]\s+(https?:\/\/\S+)$/.exec(line.trim());
    return m ? [{ index: Number(m[1]), title: m[2], url: m[3] }] : [];
  });
  return { body, sources };
}
