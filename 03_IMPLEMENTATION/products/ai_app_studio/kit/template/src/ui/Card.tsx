import type { HTMLAttributes, ReactNode } from "react";
import { cx } from "./cx";

export interface CardProps extends Omit<HTMLAttributes<HTMLElement>, "title"> {
  title?: ReactNode;
  /** small text under the title */
  subtitle?: ReactNode;
  /** buttons shown at the bottom */
  actions?: ReactNode;
}

export function Card({ title, subtitle, actions, className, children, ...rest }: CardProps) {
  return (
    <section {...rest} className={cx("ui-card", className)}>
      {title ? (
        <header className="ui-card__header">
          <h3 className="ui-card__title">{title}</h3>
          {subtitle ? <p className="ui-card__subtitle">{subtitle}</p> : null}
        </header>
      ) : null}
      {children !== undefined && children !== null ? <div className="ui-card__body">{children}</div> : null}
      {actions ? <footer className="ui-card__actions">{actions}</footer> : null}
    </section>
  );
}
