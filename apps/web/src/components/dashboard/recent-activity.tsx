"use client";

import React from "react";
import { Activity, Info } from "lucide-react";

export function RecentActivity() {
  return (
    <div className="bg-surface border border-border rounded-panel p-5 mt-8">
      <div className="flex items-center gap-2 pb-4 border-b border-border/80">
        <Activity className="w-4 h-4 text-accent" />
        <h3 className="text-sm font-semibold text-content-primary">Скорошна дейност по проучвания</h3>
      </div>
      <div className="py-8 px-4 text-center max-w-lg mx-auto">
        <div className="w-9 h-9 rounded-full bg-surface-raised border border-border flex items-center justify-center mx-auto text-content-muted mb-3">
          <Info className="w-4 h-4 text-accent" />
        </div>
        <h4 className="text-xs font-semibold text-content-primary">Следете проучванията във всеки архив</h4>
        <p className="text-xs text-content-muted mt-1 leading-relaxed">Отворете раздела „Проучване“ на архив, за да следите изпълнение. Обща емисия с дейност няма.</p>
      </div>
    </div>
  );
}
