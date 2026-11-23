import type { HTMLAttributes, ReactNode } from "react";
import { cx } from "./cx";

/** A plain <ul> for ListItem rows; give it an aria-label when there is no visible heading. */
export function List({ className, children, ...rest }: HTMLAttributes<HTMLUListElement>) {
  return (
    <ul {...rest} className={cx("ui-list", className)}>
      {children}
    </ul>
  );
}

export interface ListItemProps {
  title: ReactNode;
  subtitle?: ReactNode;
  leading?: ReactNode;
  trailing?: ReactNode;
  /** makes the row a button (at least 56 px high) */
  onClick?: () => void;
  /** the selected row in a list-detail layout */
  "aria-current"?: "true" | "page";
  /** shows a chevron on rows that open a detail */
  chevron?: boolean;
  className?: string;
}

export function ListItem({ title, subtitle, leading, trailing, onClick, chevron = false, className, ...aria }: ListItemProps) {
  const current = aria["aria-current"];
  const content = (
    <>
      {leading ? (
        <span className="ui-list-item__leading" aria-hidden="true">
          {leading}
        </span>
      ) : null}
      <span className="ui-list-item__text">
        <span className="ui-list-item__title">{title}</span>
        {subtitle ? <span className="ui-list-item__subtitle">{subtitle}</span> : null}
      </span>
      {trailing ? <span className="ui-list-item__trailing">{trailing}</span> : null}
      {chevron ? (
        <span className="ui-list-item__chevron" aria-hidden="true">
          {"\u203a"}
        </span>
      ) : null}
    </>
  );
  return (
    <li className={cx("ui-list-item", className)}>
      {onClick ? (
        <button type="button" className="ui-list-item__row ui-list-item__row--action" aria-current={current} onClick={onClick}>
          {content}
        </button>
      ) : (
        <div className="ui-list-item__row" aria-current={current}>
          {content}
        </div>
      )}
    </li>
  );
}
