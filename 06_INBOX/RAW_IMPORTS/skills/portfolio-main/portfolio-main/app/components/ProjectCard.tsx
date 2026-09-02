import Image from "next/image"
import Link from "next/link"
import { type CaseStudy } from "@/lib/case-studies"

interface ProjectCardProps {
  study: CaseStudy
  featured?: boolean
}

export default function ProjectCard({ study, featured = false }: ProjectCardProps) {
  return (
    <Link href={`/work/${study.id}`} className="group block h-full">
      <article
        className="h-full overflow-hidden rounded-2xl border transition-all duration-300 hover:-translate-y-1 hover:shadow-xl"
        style={{
          backgroundColor: study.darkTheme ? "#0f172a" : "#ffffff",
          borderColor: "var(--card-border)",
        }}
      >
        <div className="relative aspect-video overflow-hidden">
          {study.cardThumbnail && (
            <Image
              src={study.cardThumbnail}
              alt={study.title}
              fill
              className="screenshot-blurred object-cover object-top transition-transform duration-500 group-hover:scale-105"
              sizes="(max-width: 768px) 100vw, (max-width: 1200px) 60vw, 50vw"
            />
          )}
        </div>
        <div className={`${featured ? "p-8" : "p-6"}`}>
          <span
            className="text-xs font-semibold tracking-widest uppercase"
            style={{ color: study.darkTheme ? "#94a3b8" : "var(--accent)" }}
          >
            {study.tag}
          </span>
          <h3
            className="mt-2 text-xl leading-snug font-semibold"
            style={{
              color: study.darkTheme ? "#f1f5f9" : "var(--text-primary)",
            }}
          >
            {study.title}
          </h3>
          <p
            className="mt-2 text-sm leading-relaxed"
            style={{
              color: study.darkTheme ? "#94a3b8" : "var(--text-secondary)",
            }}
          >
            {study.subtitle}
          </p>
          <div className="mt-4 flex flex-wrap gap-2">
            {study.tech.map(t => (
              <span
                key={t}
                className="inline-flex items-center rounded-md px-2.5 py-1 font-mono text-xs font-medium"
                style={{
                  backgroundColor: study.darkTheme ? "#1e293b" : "var(--pill-bg)",
                  color: study.darkTheme ? "#94a3b8" : "var(--pill-text)",
                }}
              >
                {t}
              </span>
            ))}
          </div>
          <div
            className="mt-5 flex items-center gap-1 text-sm font-semibold"
            style={{ color: study.darkTheme ? "#60a5fa" : "var(--accent)" }}
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
        </div>
      </article>
    </Link>
  )
}
