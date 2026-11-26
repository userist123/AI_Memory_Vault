import { cx } from "./cx";

export interface SkeletonProps {
  /** number of text-line bars */
  lines?: number;
  width?: number | string;
  height?: number | string;
  circle?: boolean;
  className?: string;
}

/**
 * Loading placeholder shaped like the content. Decorative (aria-hidden): mark the loading container with
 * aria-busy="true" and give it a label such as "Loading".
 */
export function Skeleton({ lines = 1, width, height, circle = false, className }: SkeletonProps) {
  const bars = Array.from({ length: Math.max(1, lines) }, (_, i) => i);
  return (
    <span className={cx("ui-skeleton-group", className)} aria-hidden="true">
      {bars.map((i) => (
        <span
          key={i}
          className={cx("ui-skeleton", circle && "ui-skeleton--circle")}
          style={{ width: lines > 1 && i === bars.length - 1 ? "60%" : width, height }}
        />
      ))}
    </span>
  );
}
