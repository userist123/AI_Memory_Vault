import type { ButtonHTMLAttributes, ReactNode } from "react";
import { cx } from "./cx";

export type ButtonVariant = "primary" | "secondary" | "ghost" | "danger";

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant;
  size?: "md" | "lg";
  /** full width */
  block?: boolean;
  /** shows a spinner, sets aria-busy and blocks clicks */
  loading?: boolean;
  icon?: ReactNode;
}

/** Min 44x44 px, visible focus ring, pressed feedback. Defaults to type="button" (never submits by accident). */
export function Button({ variant = "primary", size = "md", block = false, loading = false, icon, className, children,
  disabled, type = "button", ...rest }: ButtonProps) {
  return (
    <button
      {...rest}
      type={type}
      className={cx("ui-btn", `ui-btn--${variant}`, size === "lg" && "ui-btn--lg", block && "ui-btn--block", className)}
      disabled={disabled || loading}
      aria-busy={loading || undefined}
    >
      {loading ? (
        <span className="ui-spinner" aria-hidden="true" />
      ) : icon ? (
        <span className="ui-btn__icon" aria-hidden="true">
          {icon}
        </span>
      ) : null}
      {children !== undefined && children !== null ? <span className="ui-btn__label">{children}</span> : null}
    </button>
  );
}
