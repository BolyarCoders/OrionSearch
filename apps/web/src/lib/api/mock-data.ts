import { ArchiveDetails, SourceSummary, ResearchRunStatus } from "@/types/api";

export const INITIAL_ARCHIVES: ArchiveDetails[] = [
  {
    archive: {
      id: "11111111-1111-4111-8111-111111111111",
      title: "Artificial Intelligence",
      description: "Research and industry developments across AI, machine learning and LLMs.",
      status: "active",
      role: "owner",
      createdAt: new Date(Date.now() - 30 * 86400000).toISOString(),
      updatedAt: new Date(Date.now() - 2 * 3600000).toISOString(),
      sourceCount: 3421,
    },
    settings: {
      dateFrom: "2026-01-01",
      dateTo: null,
      languages: ["en"],
      preferredDomains: ["arxiv.org", "openai.com", "anthropic.com"],
      excludedDomains: [],
      config: {
        templateKey: "artificial-intelligence",
        updateFrequency: "weekly",
      },
      updatedAt: new Date(Date.now() - 2 * 3600000).toISOString(),
    },
  },
  {
    archive: {
      id: "22222222-2222-4222-8222-222222222222",
      title: "Quantum Computing",
      description: "Hardware, algorithms, qubit architectures and real-world commercial applications.",
      status: "active",
      role: "owner",
      createdAt: new Date(Date.now() - 25 * 86400000).toISOString(),
      updatedAt: new Date(Date.now() - 5 * 3600000).toISOString(),
      sourceCount: 1892,
    },
    settings: {
      dateFrom: "2026-01-01",
      dateTo: null,
      languages: ["en"],
      preferredDomains: ["nature.com", "ibm.com", "phys.org"],
      excludedDomains: [],
      config: {
        templateKey: "quantum-computing",
        updateFrequency: "weekly",
      },
      updatedAt: new Date(Date.now() - 5 * 3600000).toISOString(),
    },
  },
  {
    archive: {
      id: "33333333-3333-4333-8333-333333333333",
      title: "Climate Policy",
      description: "Environmental policy, carbon accounting, renewable initiatives and global sustainability.",
      status: "paused",
      role: "owner",
      createdAt: new Date(Date.now() - 40 * 86400000).toISOString(),
      updatedAt: new Date(Date.now() - 86400000).toISOString(),
      sourceCount: 2104,
    },
    settings: {
      dateFrom: "2025-06-01",
      dateTo: "2026-10-01",
      languages: ["en"],
      preferredDomains: ["iea.org", "ipcc.ch", "unep.org"],
      excludedDomains: [],
      config: {
        templateKey: "climate-policy",
        updateFrequency: "monthly",
      },
      updatedAt: new Date(Date.now() - 86400000).toISOString(),
    },
  },
  {
    archive: {
      id: "44444444-4444-4444-8444-444444444444",
      title: "Space Exploration",
      description: "Science, deep-space missions, orbital infrastructure and commercial spaceflight discoveries.",
      status: "active",
      role: "owner",
      createdAt: new Date(Date.now() - 50 * 86400000).toISOString(),
      updatedAt: new Date(Date.now() - 86400000).toISOString(),
      sourceCount: 1776,
    },
    settings: {
      dateFrom: "2026-01-01",
      dateTo: null,
      languages: ["en"],
      preferredDomains: ["nasa.gov", "esa.int", "space.com"],
      excludedDomains: [],
      config: {
        templateKey: "space-exploration",
        updateFrequency: "weekly",
      },
      updatedAt: new Date(Date.now() - 86400000).toISOString(),
    },
  },
  {
    archive: {
      id: "55555555-5555-4555-8555-555555555555",
      title: "Electric Vehicles",
      description: "Battery technology, powertrain efficiency, manufacturing scale and charging markets.",
      status: "active",
      role: "owner",
      createdAt: new Date(Date.now() - 60 * 86400000).toISOString(),
      updatedAt: new Date(Date.now() - 2 * 86400000).toISOString(),
      sourceCount: 1205,
    },
    settings: {
      dateFrom: "2026-01-01",
      dateTo: null,
      languages: ["en"],
      preferredDomains: ["electrek.co", "greencarreports.com"],
      excludedDomains: [],
      config: {
        templateKey: "electric-vehicles",
        updateFrequency: "weekly",
      },
      updatedAt: new Date(Date.now() - 2 * 86400000).toISOString(),
    },
  },
  {
    archive: {
      id: "66666666-6666-4666-8666-666666666666",
      title: "Bioengineering",
      description: "Genetics, biomanufacturing breakthroughs and emerging targeted therapeutics.",
      status: "archived",
      role: "owner",
      createdAt: new Date(Date.now() - 90 * 86400000).toISOString(),
      updatedAt: new Date(Date.now() - 3 * 86400000).toISOString(),
      sourceCount: 1043,
    },
    settings: {
      dateFrom: "2025-01-01",
      dateTo: "2026-01-01",
      languages: ["en"],
      preferredDomains: ["biorxiv.org", "cell.com", "nih.gov"],
      excludedDomains: [],
      config: {
        templateKey: "bioengineering",
        updateFrequency: "monthly",
      },
      updatedAt: new Date(Date.now() - 3 * 86400000).toISOString(),
    },
  },
];

export const INITIAL_SOURCES: Record<string, SourceSummary[]> = {
  "11111111-1111-4111-8111-111111111111": [
    {
      documentId: "d1111111-0001-4000-8000-000000000001",
      canonicalUrl: "https://arxiv.org/abs/2609.12345",
      title: "A Survey of LLM Agent Frameworks in 2026",
      publisherName: "arXiv",
      publisherDomain: "arxiv.org",
      language: "en",
      publishedAt: "2026-09-12T14:32:00Z",
      firstSeenAt: "2026-09-12T15:00:00Z",
      lastSeenAt: "2026-10-20T08:00:00Z",
      relevanceScore: 0.92,
      addedAt: "2026-09-12T15:00:00Z",
    },
    {
      documentId: "d1111111-0002-4000-8000-000000000002",
      canonicalUrl: "https://anthropic.com/research/autonomous-agents-production",
      title: "Autonomous Agents in Production",
      publisherName: "Anthropic",
      publisherDomain: "anthropic.com",
      language: "en",
      publishedAt: "2026-09-11T10:15:00Z",
      firstSeenAt: "2026-09-11T12:00:00Z",
      lastSeenAt: "2026-10-20T08:00:00Z",
      relevanceScore: 0.88,
      addedAt: "2026-09-11T12:00:00Z",
    },
    {
      documentId: "d1111111-0003-4000-8000-000000000003",
      canonicalUrl: "https://openai.com/index/tool-use-planning-models",
      title: "Tool Use and Planning in Large Language Models",
      publisherName: "OpenAI",
      publisherDomain: "openai.com",
      language: "en",
      publishedAt: "2026-09-10T16:00:00Z",
      firstSeenAt: "2026-09-10T18:00:00Z",
      lastSeenAt: "2026-10-20T08:00:00Z",
      relevanceScore: 0.84,
      addedAt: "2026-09-10T18:00:00Z",
    },
    {
      documentId: "d1111111-0004-4000-8000-000000000004",
      canonicalUrl: "https://arxiv.org/abs/2609.09876",
      title: "A Practical Guide to Agent Evaluation and Benchmarking",
      publisherName: "arXiv",
      publisherDomain: "arxiv.org",
      language: "en",
      publishedAt: "2026-09-09T09:00:00Z",
      firstSeenAt: "2026-09-09T11:00:00Z",
      lastSeenAt: "2026-10-20T08:00:00Z",
      relevanceScore: 0.81,
      addedAt: "2026-09-09T11:00:00Z",
    },
    {
      documentId: "d1111111-0005-4000-8000-000000000005",
      canonicalUrl: "https://deepmind.google/discover/blog/multi-agent-collaboration-at-scale",
      title: "Multi-Agent Collaboration at Scale",
      publisherName: "Google DeepMind",
      publisherDomain: "deepmind.google",
      language: "en",
      publishedAt: "2026-09-08T13:00:00Z",
      firstSeenAt: "2026-09-08T14:00:00Z",
      lastSeenAt: "2026-10-20T08:00:00Z",
      relevanceScore: 0.78,
      addedAt: "2026-09-08T14:00:00Z",
    },
    {
      documentId: "d1111111-0006-4000-8000-000000000006",
      canonicalUrl: "https://microsoft.com/research/security-considerations-llm-agents",
      title: "Security Considerations for LLM Agents",
      publisherName: "Microsoft",
      publisherDomain: "microsoft.com",
      language: "en",
      publishedAt: null, // Unknown publication date test case
      firstSeenAt: "2026-09-07T10:00:00Z",
      lastSeenAt: "2026-10-20T08:00:00Z",
      relevanceScore: 0.75,
      addedAt: "2026-09-07T10:00:00Z",
    },
  ],
};

export interface DemoActivityFeedItem {
  id: string;
  archiveId: string;
  archiveTitle: string;
  runTitle: string;
  status: "completed" | "running" | "failed";
  sources: number | null;
  findings: number | null;
  startedAt: string;
}

export const DEMO_RECENT_ACTIVITY: DemoActivityFeedItem[] = [
  {
    id: "act-1",
    archiveId: "11111111-1111-4111-8111-111111111111",
    archiveTitle: "Artificial Intelligence",
    runTitle: "Investigating recent LLM agent frameworks",
    status: "completed",
    sources: 42,
    findings: 18,
    startedAt: new Date(Date.now() - 2 * 3600000).toISOString(),
  },
  {
    id: "act-2",
    archiveId: "22222222-2222-4222-8222-222222222222",
    archiveTitle: "Quantum Computing",
    runTitle: "Error correction developments in 2026",
    status: "running",
    sources: null,
    findings: null,
    startedAt: new Date(Date.now() - 5 * 3600000).toISOString(),
  },
  {
    id: "act-3",
    archiveId: "44444444-4444-4444-8444-444444444444",
    archiveTitle: "Space Exploration",
    runTitle: "Latest lunar mission updates",
    status: "completed",
    sources: 27,
    findings: 11,
    startedAt: new Date(Date.now() - 86400000).toISOString(),
  },
  {
    id: "act-4",
    archiveId: "33333333-3333-4333-8333-333333333333",
    archiveTitle: "Climate Policy",
    runTitle: "EU emissions regulation proposals",
    status: "failed",
    sources: 3,
    findings: 0,
    startedAt: new Date(Date.now() - 2 * 86400000).toISOString(),
  },
];
