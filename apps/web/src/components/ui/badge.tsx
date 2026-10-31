import React from "react";

export interface BadgeProps extends React.HTMLAttributes<HTMLSpanElement> {
  variant?: "neutral" | "accent" | "success" | "warning" | "failure" | "info";
  size?: "sm" | "md";
}

export function Badge({
  className = "",
  variant = "neutral",
  size = "md",
  children,
  ...props
}: BadgeProps) {
  const variantStyles = {
    neutral: "bg-surface-raised text-content-secondary border-border",
    accent: "bg-accent/15 text-accent border-accent/30",
    success: "bg-status-success/15 text-status-success border-status-success/30",
    warning: "bg-status-warning/15 text-status-warning border-status-warning/30",
    failure: "bg-status-failure/15 text-status-failure border-status-failure/30",
    info: "bg-status-info/15 text-status-info border-status-info/30",
  };

  const sizeStyles = {
    sm: "text-xs px-1.5 py-0.5",
    md: "text-xs px-2.5 py-0.5",
  };

  return (
    <span
      className={`inline-flex items-center font-medium rounded-full border ${variantStyles[variant]} ${sizeStyles[size]} ${className}`}
      {...props}
    >
      {children}
    </span>
  );
}
