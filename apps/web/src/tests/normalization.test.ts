import { describe, it, expect } from "vitest";
import {
  normalizeDomain,
  validateTitle,
  validateDateWindow,
  getArchiveCoverUrl,
  formatRelativeTime,
} from "@/lib/normalization";

describe("Domain Normalization & Validation", () => {
  it("accepts valid lowercase domains", () => {
    const res = normalizeDomain("nature.com");
    expect(res.error).toBeNull();
    expect(res.domain).toBe("nature.com");
  });

  it("normalizes uppercase and trailing dot", () => {
    const res = normalizeDomain("ARXIV.ORG.");
    expect(res.error).toBeNull();
    expect(res.domain).toBe("arxiv.org");
  });

  it("rejects schemes like https://", () => {
    const res = normalizeDomain("https://nature.com");
    expect(res.domain).toBeNull();
    expect(res.error).toContain("Enter a DNS domain");
  });

  it("rejects paths and ports", () => {
    const res1 = normalizeDomain("nature.com/path");
    expect(res1.domain).toBeNull();

    const res2 = normalizeDomain("nature.com:8080");
    expect(res2.domain).toBeNull();
  });

  it("rejects empty or whitespace domain", () => {
    const res = normalizeDomain("   ");
    expect(res.domain).toBeNull();
    expect(res.error).toContain("cannot be blank");
  });
});

describe("Title Validation", () => {
  it("accepts valid 1-200 character titles", () => {
    expect(validateTitle("Artificial Intelligence")).toBeNull();
    expect(validateTitle("Quantum Computing & Sensors")).toBeNull();
  });

  it("rejects empty or whitespace-only titles", () => {
    expect(validateTitle("")).toBe("Archive title is required.");
    expect(validateTitle("   ")).toBe("Archive title is required.");
  });

  it("rejects titles longer than 200 characters", () => {
    const longTitle = "A".repeat(201);
    expect(validateTitle(longTitle)).toBe("Title cannot exceed 200 characters.");
  });

  it("rejects control characters", () => {
    const badTitle = "AI\x00Research";
    expect(validateTitle(badTitle)).toBe("Title cannot contain control characters.");
  });
});

describe("Date Window Validation", () => {
  it("allows null or empty dates", () => {
    expect(validateDateWindow(null, null)).toBeNull();
    expect(validateDateWindow("2026-01-01", null)).toBeNull();
    expect(validateDateWindow(null, "2026-10-20")).toBeNull();
  });

  it("allows dateFrom <= dateTo", () => {
    expect(validateDateWindow("2026-01-01", "2026-10-20")).toBeNull();
    expect(validateDateWindow("2026-05-01", "2026-05-01")).toBeNull();
  });

  it("rejects dateFrom > dateTo", () => {
    expect(validateDateWindow("2026-10-20", "2026-01-01")).toBe(
      "Start date must be before or equal to end date."
    );
  });
});

describe("Cover Image Resolution", () => {
  it("matches preset titles deterministically", () => {
    expect(getArchiveCoverUrl("Artificial Intelligence")).toBe(
      "/images/archives/artificial-intelligence.webp"
    );
    expect(getArchiveCoverUrl("climate policy")).toBe(
      "/images/archives/climate-policy.webp"
    );
  });

  it("matches explicit templateKey in config", () => {
    expect(
      getArchiveCoverUrl("Custom Title", { templateKey: "bioengineering" })
    ).toBe("/images/archives/bioengineering.webp");
  });

  it("returns null for unmatched custom titles to trigger neutral fallback", () => {
    expect(getArchiveCoverUrl("Unknown Research Project 2026")).toBeNull();
  });
});

describe("Relative Time Formatting", () => {
  it("formats recent dates accurately", () => {
    const now = new Date();
    expect(formatRelativeTime(now.toISOString())).toBe("Just now");

    const twoHoursAgo = new Date(now.getTime() - 2 * 3600000).toISOString();
    expect(formatRelativeTime(twoHoursAgo)).toBe("2 hours ago");
  });
});
