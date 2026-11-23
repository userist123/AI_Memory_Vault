import { useEffect, type ReactNode } from "react";
import { cx } from "./cx";

export interface ToastProps {
  open: boolean;
  message: ReactNode;
  tone?: "neutral" | "success" | "danger";
  onDismiss: () => void;
  /** ms before it hides itself; 0 keeps it until dismissed */
  duration?: number;
  action?: ReactNode;
}

/** Short feedback after an action. The live region is always in the page so screen readers announce new messages. */
export function Toast({ open, message, tone = "neutral", onDismiss, duration = 4000, action }: ToastProps) {
  useEffect(() => {
    if (!open || duration <= 0) return;
    const t = setTimeout(onDismiss, duration);
    return () => clearTimeout(t);
  }, [open, duration, onDismiss]);
  const urgent = tone === "danger";
  return (
    <div className="ui-toast-region" role={urgent ? "alert" : "status"} aria-live={urgent ? "assertive" : "polite"}>
      {open ? (
        <div className={cx("ui-toast", `ui-toast--${tone}`)}>
          <span className="ui-toast__message">{message}</span>
          {action}
          <button type="button" className="ui-icon-btn ui-toast__close" aria-label="Dismiss" onClick={onDismiss}>
            {"\u00d7"}
          </button>
        </div>
      ) : null}
    </div>
  );
}
