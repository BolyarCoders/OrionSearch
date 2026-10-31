import React from "react";

export interface CardProps extends React.HTMLAttributes<HTMLDivElement> {
  variant?: "default" | "raised" | "interactive";
}

export function Card({ className = "", variant = "default", children, ...props }: CardProps) {
  const variantStyles = {
    default: "bg-surface border-border",
    raised: "bg-surface-raised border-border",
    interactive:
      "bg-surface border-border hover:border-border-light hover:bg-surface/90 transition-all duration-micro cursor-pointer",
  };

  return (
    <div
      className={`rounded-panel border ${variantStyles[variant]} p-5 ${className}`}
      {...props}
    >
      {children}
    </div>
  );
}
