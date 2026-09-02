import Link from "next/link"
import { type CaseStudy } from "@/lib/case-studies"

interface ProjectCardCompactProps {
  study: CaseStudy
}

export default function ProjectCardCompact({ study }: ProjectCardCompactProps) {
  return (
    <Link href={`/work/${study.id}`} className="group block">
      <article
        className="flex flex-col gap-3 rounded-xl border px-6 py-5 transition-all duration-200 hover:-translate-y-0.5 hover:shadow-md sm:flex-row sm:items-center sm:justify-between"
        style={{
          backgroundColor: "var(--bg-primary)",
          borderColor: "var(--card-border)",
        }}
      >
        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
            <span
              className="text-xs font-semibold tracking-widest uppercase"
              style={{ color: "var(--accent)" }}
            >
              {study.tag}
            </span>
            <span className="text-xs" style={{ color: "var(--text-secondary)" }}>
              {study.period}
            </span>
          </div>
          <h3 className="mt-1 text-base font-semibold" style={{ color: "var(--text-primary)" }}>
            {study.title}
          </h3>
          <p
            className="mt-1 line-clamp-2 text-sm leading-relaxed"
            style={{ color: "var(--text-secondary)" }}
          >
            {study.subtitle}
          </p>
          <div className="mt-3 flex flex-wrap gap-1.5">
            {study.tech.slice(0, 6).map(t => (
              <span
                key={t}
                className="inline-flex items-center rounded-md px-2 py-0.5 font-mono text-xs font-medium"
                style={{
                  backgroundColor: "var(--pill-bg)",
                  color: "var(--pill-text)",
                }}
              >
                {t}
              </span>
            ))}
            {study.tech.length > 6 && (
              <span
                className="inline-flex items-center rounded-md px-2 py-0.5 font-mono text-xs font-medium"
                style={{
                  backgroundColor: "var(--pill-bg)",
                  color: "var(--pill-text)",
                }}
              >
                +{study.tech.length - 6}
              </span>
            )}
          </div>
        </div>
        <div
          className="flex shrink-0 items-center gap-1 text-sm font-semibold sm:pl-6"
          style={{ color: "var(--accent)" }}
        >
          View Case Study
          <svg
            className="h-4 w-4 transition-transform duration-200 group-hover:translate-x-1"
            fill="none"
            viewBox="0 0 24 24"
            stroke="currentColor"
          >
            <path
              strokeLinecap="round"
              strokeLinejoin="round"
              strokeWidth={2}
              d="M17 8l4 4m0 0l-4 4m4-4H3"
            />
          </svg>
        </div>
      </article>
    </Link>
  )
}
