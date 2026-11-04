import type { Config } from "tailwindcss";

const config: Config = {
  content: [
    "./src/pages/**/*.{js,ts,jsx,tsx,mdx}",
    "./src/components/**/*.{js,ts,jsx,tsx,mdx}",
    "./src/app/**/*.{js,ts,jsx,tsx,mdx}",
  ],
  darkMode: "class",
  theme: {
    extend: {
      colors: {"workspace": "rgb(var(--canvas) / <alpha-value>)","sidebar": "rgb(var(--sidebar) / <alpha-value>)","surface": {"DEFAULT": "rgb(var(--surface) / <alpha-value>)","raised": "rgb(var(--raised) / <alpha-value>)","active": "rgb(var(--active) / <alpha-value>)","hover": "rgb(var(--hover) / <alpha-value>)"},"border": {"DEFAULT": "rgb(var(--rule) / <alpha-value>)","subtle": "rgb(var(--rule) / <alpha-value>)","light": "rgb(var(--control) / <alpha-value>)"},"content": {"primary": "rgb(var(--ink) / <alpha-value>)","secondary": "rgb(var(--secondary) / <alpha-value>)","muted": "rgb(var(--muted) / <alpha-value>)"},"accent": {"DEFAULT": "rgb(var(--accent) / <alpha-value>)","hover": "rgb(var(--accent-hover) / <alpha-value>)","subtle": "rgb(var(--accent-soft) / <alpha-value>)","soft": "rgb(var(--accent-soft) / <alpha-value>)"},"on-accent": "rgb(var(--on-accent) / <alpha-value>)","status": {"success": "rgb(var(--success) / <alpha-value>)","warning": "rgb(var(--warning) / <alpha-value>)","failure": "rgb(var(--failure) / <alpha-value>)","info": "rgb(var(--info) / <alpha-value>)"}},
      borderRadius: {
        panel: "8px",
        control: "6px",
      },
      fontSize: { xs: ["0.8125rem", "1.25rem"], sm: ["0.875rem", "1.375rem"], base: ["0.9375rem", "1.5rem"] },
      fontFamily: {
        sans: ["system-ui", "-apple-system", "sans-serif"],
      },
      transitionDuration: {
        micro: "160ms",
      },
    },
  },
  plugins: [],
};

export default config;
