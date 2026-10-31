import React from "react";
import { TEMPLATE_PRESETS } from "@/lib/normalization";
import { Brain, Atom, Leaf, Rocket, Car, Dna } from "lucide-react";

interface TemplateSelectorProps {
  onSelect: (preset: (typeof TEMPLATE_PRESETS)[0]) => void;
  selectedKey?: string;
}

export function TemplateSelector({ onSelect, selectedKey }: TemplateSelectorProps) {
  const getIcon = (key: string) => {
    switch (key) {
      case "artificial-intelligence":
        return Brain;
      case "quantum-computing":
        return Atom;
      case "climate-policy":
        return Leaf;
      case "space-exploration":
        return Rocket;
      case "electric-vehicles":
        return Car;
      case "bioengineering":
        return Dna;
      default:
        return Brain;
    }
  };

  return (
    <div className="space-y-2">
      <div className="flex items-center justify-between">
        <label className="text-xs font-medium text-content-secondary">
          Topic template (optional)
        </label>
        <span className="text-xs text-content-muted">Use a template to get started quickly</span>
      </div>

      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-1 xl:grid-cols-2 gap-2">
        {TEMPLATE_PRESETS.map((preset) => {
          const Icon = getIcon(preset.key);
          const isSelected = selectedKey === preset.key;

          return (
            <button
              key={preset.key}
              aria-pressed={isSelected}
              type="button"
              onClick={() => onSelect(preset)}
              className={`flex items-start gap-3 p-3 rounded-control border text-left transition-all duration-micro ${
                isSelected
                  ? "bg-accent/15 border-accent text-content-primary"
                  : "bg-surface-raised/60 border-border hover:border-border-light hover:bg-surface-raised"
              }`}
            >
              <div
                className={`p-2 rounded-control shrink-0 ${
                  isSelected ? "bg-accent text-on-accent" : "bg-surface text-content-muted"
                }`}
              >
                <Icon className="w-4 h-4" />
              </div>
              <div className="overflow-hidden">
                <div className="text-xs font-semibold text-content-primary">
                  {preset.title}
                </div>
                <div className="text-xs text-content-muted mt-0.5 line-clamp-1">
                  {preset.description}
                </div>
              </div>
            </button>
          );
        })}
      </div>
    </div>
  );
}
