import React from "react";

export function Skeleton({ className = "" }: { className?: string }) {
  return <div className={`animate-pulse bg-surface-raised/80 rounded-control ${className}`} />;
}
