import { describe, it, expect } from "vitest";
import React from "react";
import { render, screen } from "@testing-library/react";
import { renderMarkdown, splitReport } from "@/lib/markdown";

describe("report markdown", () => {
  it("escapes HTML and renders structure", () => {
    const { container } = render(<div>{renderMarkdown("## Тема\n<script>alert(1)</script> **важно** [1]\n\n- едно\n- две")}</div>);
    expect(container.querySelector("script")).toBeNull();
    expect(container.querySelector("h5")?.textContent).toBe("Тема");
    expect(container.querySelector("strong")?.textContent).toBe("важно");
    expect(container.querySelectorAll("li")).toHaveLength(2);
    expect(container.textContent).toContain("<script>");
  });
  it("only links http(s)", () => {
    const { container } = render(<div>{renderMarkdown("[x](javascript:alert(1)) [ok](https://a.example/p)")}</div>);
    const links = container.querySelectorAll("a");
    expect(links).toHaveLength(1);
    expect(links[0].getAttribute("href")).toBe("https://a.example/p");
  });
  it("splits bibliography", () => {
    const r = splitReport("Текст [1]\n\nИзточници (2):\n[1] A — https://a.example/1\n[2] B — C — https://b.example/2");
    expect(r.body).toBe("Текст [1]");
    expect(r.sources.map(s => s.url)).toEqual(["https://a.example/1", "https://b.example/2"]);
  });
});
