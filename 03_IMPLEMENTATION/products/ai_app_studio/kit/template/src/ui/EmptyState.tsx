import type { ReactNode } from "react";
import { cx } from "./cx";

export interface EmptyStateProps {
  title: string;
  description?: ReactNode;
  icon?: ReactNode;
  /** usually one primary Button that fixes the state ("Add your first item", "Try again") */
  action?: ReactNode;
  tone?: "empty" | "error";
}

/** Empty and error states: say what happened and what to do next. Errors are announced (role="alert"). */
export function EmptyState({ title, description, icon, action, tone = "empty" }: EmptyStateProps) {
  return (
    <section className={cx("ui-empty", tone === "error" && "ui-empty--error")} role={tone === "error" ? "alert" : undefined}>
      {icon ? (
        <span className="ui-empty__icon" aria-hidden="true">
          {icon}
        </span>
      ) : null}
      <h2 className="ui-empty__title">{title}</h2>
      {description ? <p className="ui-empty__desc">{description}</p> : null}
      {action ? <div className="ui-empty__action">{action}</div> : null}
    </section>
  );
}
