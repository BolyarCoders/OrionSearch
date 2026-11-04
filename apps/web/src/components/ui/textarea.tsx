import React from "react";

export interface TextareaProps extends React.TextareaHTMLAttributes<HTMLTextAreaElement> {
  label?: string;
  error?: string;
  hint?: string;
  maxChars?: number;
}

export const Textarea = React.forwardRef<HTMLTextAreaElement, TextareaProps>(
  ({ className = "", label, error, hint, maxChars, value, id, ...props }, ref) => {
    const generatedId = React.useId();
    const inputId = id || generatedId;
    const currentLength = typeof value === "string" ? value.length : 0;

    return (
      <div className="w-full">
        <div className="flex items-center justify-between mb-1.5">
          {label && (
            <label htmlFor={inputId} className="block text-xs font-medium text-content-secondary">
              {label}
              {props.required && <span className="text-status-failure ml-1">*</span>}
            </label>
          )}
          {maxChars && (
            <span className="text-xs text-content-muted">
              {currentLength} / {maxChars}
            </span>
          )}
        </div>
        <textarea
          ref={ref}
          id={inputId}
          aria-invalid={error ? true : undefined}
          value={value}
          maxLength={maxChars}
          className={`w-full bg-surface-raised text-content-primary placeholder-content-muted text-sm rounded-control border ${
            error ? "border-status-failure focus:border-status-failure" : "border-border focus:border-accent"
          } p-3 transition-colors duration-micro focus:outline-none focus:ring-1 ${
            error ? "focus:ring-status-failure" : "focus:ring-accent"
          } disabled:opacity-50 min-h-[90px] resize-y ${className}`}
          {...props}
        />
        {error ? (
          <p className="mt-1.5 text-xs text-status-failure">{error}</p>
        ) : hint ? (
          <p className="mt-1.5 text-xs text-content-muted">{hint}</p>
        ) : null}
      </div>
    );
  }
);

Textarea.displayName = "Textarea";
