import React from "react";

export interface TabItem {
  id: string;
  label: string;
  count?: number;
  disabled?: boolean;
}

export interface TabsProps {
  tabs: TabItem[];
  activeTab: string;
  onChange: (id: string) => void;
  className?: string;
}

export function Tabs({ tabs, activeTab, onChange, className = "" }: TabsProps) {
  return (
    <div role="tablist" aria-label="Archive sections" className={`flex items-center gap-1 border-b border-border max-w-full min-w-0 overflow-x-auto ${className}`}>
      {tabs.map((tab) => {
        const isActive = tab.id === activeTab;
        return (
          <button
            key={tab.id}
            type="button"
            role="tab"
            aria-selected={isActive}
            tabIndex={isActive ? 0 : -1}
            onKeyDown={event => {
              if (!["ArrowLeft", "ArrowRight", "Home", "End"].includes(event.key)) return;
              event.preventDefault();
              const available = tabs.filter(t => !t.disabled);
              const index = available.findIndex(t => t.id === tab.id);
              const next = event.key === "Home" ? 0 : event.key === "End" ? available.length - 1 : (index + (event.key === "ArrowLeft" ? -1 : 1) + available.length) % available.length;
              onChange(available[next].id);
              const buttons = event.currentTarget.parentElement?.querySelectorAll<HTMLButtonElement>('[role="tab"]:not(:disabled)');
              buttons?.[next]?.focus();
            }}
            disabled={tab.disabled}
            onClick={() => !tab.disabled && onChange(tab.id)}
            className={`relative shrink-0 px-4 py-3 text-sm font-medium transition-colors duration-micro flex items-center gap-2 border-b-2 -mb-px disabled:opacity-40 disabled:cursor-not-allowed ${
              isActive
                ? "border-accent text-content-primary"
                : "border-transparent text-content-secondary hover:text-content-primary hover:border-border-light"
            }`}
          >
            <span>{tab.label}</span>
            {tab.count !== undefined && (
              <span
                className={`text-xs px-1.5 py-0.2 rounded-full font-semibold ${
                  isActive ? "bg-accent/20 text-accent" : "bg-surface-raised text-content-muted"
                }`}
              >
                {tab.count}
              </span>
            )}
          </button>
        );
      })}
    </div>
  );
}
