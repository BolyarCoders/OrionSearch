import React from "react";

export interface InputProps extends React.InputHTMLAttributes<HTMLInputElement> {
  label?: string;
  error?: string;
  hint?: string;
  leftIcon?: React.ReactNode;
  rightIcon?: React.ReactNode;
}

export const Input = React.forwardRef<HTMLInputElement, InputProps>(
  ({ className = "", label, error, hint, leftIcon, rightIcon, id, ...props }, ref) => {
    const generatedId = React.useId();
    const inputId = id || generatedId;

    return (
      <div className="w-full">
        {label && (
          <label htmlFor={inputId} className="block text-xs font-medium text-content-secondary mb-1.5">
            {label}
            {props.required && <span className="text-status-failure ml-1">*</span>}
          </label>
        )}
        <div className="relative flex items-center">
          {leftIcon && (
            <span className="absolute left-3 text-content-muted pointer-events-none flex items-center">
              {leftIcon}
            </span>
          )}
          <input
            ref={ref}
            id={inputId}
            aria-invalid={error ? true : undefined}
            className={`w-full bg-surface-raised text-content-primary placeholder-content-muted text-sm rounded-control border ${
              error ? "border-status-failure focus:border-status-failure" : "border-border focus:border-accent"
            } ${leftIcon ? "pl-9" : "pl-3.5"} ${
              rightIcon ? "pr-9" : "pr-3.5"
            } py-2 transition-colors duration-micro focus:outline-none focus:ring-1 ${
              error ? "focus:ring-status-failure" : "focus:ring-accent"
            } disabled:opacity-50 disabled:bg-surface ${className}`}
            {...props}
          />
          {rightIcon && (
            <span className="absolute right-3 text-content-muted flex items-center">{rightIcon}</span>
          )}
        </div>
        {error ? (
          <p className="mt-1.5 text-xs text-status-failure">{error}</p>
        ) : hint ? (
          <p className="mt-1.5 text-xs text-content-muted">{hint}</p>
        ) : null}
      </div>
    );
  }
);

Input.displayName = "Input";
