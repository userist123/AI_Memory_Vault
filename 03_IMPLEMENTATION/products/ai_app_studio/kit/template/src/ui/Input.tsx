import { useId, type InputHTMLAttributes } from "react";
import { cx } from "./cx";

export interface InputProps extends Omit<InputHTMLAttributes<HTMLInputElement>, "size"> {
  /** visible label (required: a placeholder is not a label) */
  label: string;
  hint?: string;
  /** error text; marks the field aria-invalid and announces the message */
  error?: string;
}

export function Input({ label, hint, error, id, className, ...rest }: InputProps) {
  const auto = useId();
  const inputId = id ?? `in-${auto}`;
  const hintId = `${inputId}-hint`;
  const errorId = `${inputId}-error`;
  const describedBy = [hint ? hintId : "", error ? errorId : ""].filter(Boolean).join(" ") || undefined;
  return (
    <div className={cx("ui-field", error && "ui-field--error", className)}>
      <label className="ui-field__label" htmlFor={inputId}>
        {label}
      </label>
      <input {...rest} id={inputId} className="ui-input" aria-invalid={error ? true : undefined} aria-describedby={describedBy} />
      {hint ? (
        <p id={hintId} className="ui-field__hint">
          {hint}
        </p>
      ) : null}
      {error ? (
        <p id={errorId} className="ui-field__error" role="alert">
          {error}
        </p>
      ) : null}
    </div>
  );
}
