export const KNOWN_COVERS: Record<string, string> = {
  "artificial-intelligence": "/images/archives/artificial-intelligence.webp",
  "quantum-computing": "/images/archives/quantum-computing.webp",
  "climate-policy": "/images/archives/climate-policy.webp",
  "space-exploration": "/images/archives/space-exploration.webp",
  "electric-vehicles": "/images/archives/electric-vehicles.webp",
  "bioengineering": "/images/archives/bioengineering.webp",
};

export const TEMPLATE_PRESETS = [
  {
    key: "artificial-intelligence",
    title: "Artificial Intelligence",
    description: "Research and industry developments across AI, machine learning and LLMs.",
    preferredDomains: ["arxiv.org", "openai.com", "anthropic.com"],
    languages: ["en"],
    coverUrl: "/images/archives/artificial-intelligence.webp",
  },
  {
    key: "quantum-computing",
    title: "Quantum Computing",
    description: "Hardware, algorithms, qubit architectures and real-world commercial applications.",
    preferredDomains: ["nature.com", "ibm.com", "phys.org"],
    languages: ["en"],
    coverUrl: "/images/archives/quantum-computing.webp",
  },
  {
    key: "climate-policy",
    title: "Climate Policy",
    description: "Environmental policy, carbon accounting, renewable initiatives and global sustainability.",
    preferredDomains: ["iea.org", "ipcc.ch", "unep.org"],
    languages: ["en"],
    coverUrl: "/images/archives/climate-policy.webp",
  },
  {
    key: "space-exploration",
    title: "Space Exploration",
    description: "Science, deep-space missions, orbital infrastructure and commercial spaceflight discoveries.",
    preferredDomains: ["nasa.gov", "esa.int", "space.com"],
    languages: ["en"],
    coverUrl: "/images/archives/space-exploration.webp",
  },
  {
    key: "electric-vehicles",
    title: "Electric Vehicles",
    description: "Battery technology, powertrain efficiency, manufacturing scale and charging markets.",
    preferredDomains: ["electrek.co", "greencarreports.com"],
    languages: ["en"],
    coverUrl: "/images/archives/electric-vehicles.webp",
  },
  {
    key: "bioengineering",
    title: "Bioengineering",
    description: "Genetics, biomanufacturing breakthroughs and emerging targeted therapeutics.",
    preferredDomains: ["biorxiv.org", "cell.com", "nih.gov"],
    languages: ["en"],
    coverUrl: "/images/archives/bioengineering.webp",
  },
];

/**
 * Deterministically resolve an archive's cover image.
 * Uses exact preset matching or an explicit templateKey in config;
 * falls back to undefined so an elegant neutral CSS fallback can be rendered.
 */
export function getArchiveCoverUrl(title: string, config?: Record<string, unknown>): string | null {
  if (config && typeof config.templateKey === "string" && KNOWN_COVERS[config.templateKey]) {
    return KNOWN_COVERS[config.templateKey];
  }

  const normalized = title.trim().toLowerCase();
  for (const preset of TEMPLATE_PRESETS) {
    if (preset.title.toLowerCase() === normalized) {
      return preset.coverUrl;
    }
  }

  return null;
}

/**
 * Normalize and validate a DNS domain.
 * Rejects schemes (https://), paths, ports, or malformed characters.
 */
export function normalizeDomain(input: string): { domain: string | null; error: string | null } {
  let val = input.trim().toLowerCase();
  if (!val) {
    return { domain: null, error: "Domain cannot be blank" };
  }

  if (val.includes("://") || val.startsWith("//")) {
    return { domain: null, error: "Enter a DNS domain (e.g. example.com), not a full URL with scheme" };
  }

  if (val.includes("/") || val.includes(":") || val.includes("?") || val.includes("#")) {
    return { domain: null, error: "Domain must not contain paths, ports or query strings" };
  }

  if (val.endsWith(".")) {
    val = val.slice(0, -1);
  }

  if (/[\s@%\\]/u.test(val)) return { domain: null, error: "Invalid DNS domain" };
  try { val = new URL(`https://${val}`).hostname; }
  catch { return { domain: null, error: "Invalid DNS domain" }; }
  const labels = val.split(".");
  if (val.length > 253 || labels.length < 2 || labels.some(label => !/^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$/i.test(label)) || /^\d+(?:\.\d+){3}$/.test(val)) {
    return { domain: null, error: "Invalid domain format (e.g. nature.com)" };
  }

  return { domain: val, error: null };
}

/**
 * Validate archive title according to backend rules:
 * 1-200 chars trimmed, no control characters.
 */
export function validateTitle(title: string): string | null {
  const trimmed = title.trim();
  if (trimmed.length === 0) {
    return "Archive title is required.";
  }
  if (trimmed.length > 200) {
    return "Title cannot exceed 200 characters.";
  }
  // Check for control characters
  for (let i = 0; i < trimmed.length; i++) {
    const code = trimmed.charCodeAt(i);
    if ((code >= 0 && code <= 31) || (code >= 127 && code <= 159)) {
      return "Title cannot contain control characters.";
    }
  }
  return null;
}

/**
 * Validate date window: dateFrom <= dateTo
 */
export function validateDateWindow(dateFrom?: string | null, dateTo?: string | null): string | null {
  for (const value of [dateFrom, dateTo]) {
    if (value && (!/^\d{4}-\d{2}-\d{2}$/.test(value) || Number.isNaN(Date.parse(value)) || new Date(value).toISOString().slice(0, 10) !== value)) return "Enter a valid date in YYYY-MM-DD format.";
  }
  if (dateFrom && dateTo) {
    if (new Date(dateFrom) > new Date(dateTo)) {
      return "Start date must be before or equal to end date.";
    }
  }
  return null;
}

/**
 * Relative time formatting (e.g. "Updated 2 hours ago", "yesterday", etc.)
 */
export function formatRelativeTime(dateString: string): string {
  try {
    const date = new Date(dateString);
    const now = new Date();
    const diffMs = now.getTime() - date.getTime();
    if (diffMs < 0) return "Just now";

    const diffMins = Math.floor(diffMs / (1000 * 60));
    if (diffMins < 1) return "Just now";
    if (diffMins < 60) return `${diffMins} min${diffMins > 1 ? "s" : ""} ago`;

    const diffHours = Math.floor(diffMins / 60);
    if (diffHours < 24) return `${diffHours} hour${diffHours > 1 ? "s" : ""} ago`;

    const diffDays = Math.floor(diffHours / 24);
    if (diffDays === 1) return "Yesterday";
    if (diffDays < 7) return `${diffDays} days ago`;

    return date.toLocaleDateString("en-US", { month: "short", day: "numeric", year: "numeric" });
  } catch {
    return dateString;
  }
}
