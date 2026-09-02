import Link from "next/link"
import { notFound } from "next/navigation"
import { getCaseStudy, getAllCaseStudies } from "@/lib/case-studies"
import Nav from "@/app/components/Nav"
import BrowserMockup from "@/app/components/BrowserMockup"
import CaseStudyHeroBanner from "@/app/components/CaseStudyHeroBanner"
import TechPill from "@/app/components/TechPill"

export async function generateStaticParams() {
  return getAllCaseStudies().map(cs => ({ id: cs.id }))
}

export async function generateMetadata({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params
  const study = getCaseStudy(id)
  if (!study) return {}
  return {
    title: `${study.title} — Over Martinez`,
    description: study.subtitle,
  }
}

export default async function CaseStudyPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params
  const study = getCaseStudy(id)

  if (!study) notFound()

  const dark = study.darkTheme

  return (
    <div
      className="min-h-screen"
      style={{ backgroundColor: dark ? "#0a1628" : "var(--bg-primary)" }}
    >
      <Nav variant="case-study" />

      {/* ── Case Study Header ────────────────────────────────────────────── */}
      <header
        style={{
          backgroundColor: dark ? "#0a1628" : "var(--bg-section)",
          borderBottom: dark ? "1px solid #1e293b" : "1px solid var(--card-border)",
        }}
      >
        <div className="mx-auto max-w-5xl px-6 py-16 lg:px-8 lg:py-20">
          <Link
            href="/"
            className="mb-10 flex w-fit items-center gap-2 text-sm font-medium transition-opacity hover:opacity-60"
            style={{ color: dark ? "#94a3b8" : "var(--text-secondary)" }}
          >
            <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                strokeWidth={2}
                d="M19 12H5m7-7l-7 7 7 7"
              />
            </svg>
            Back to Home
          </Link>

          <span
            className="text-xs font-semibold tracking-widest uppercase"
            style={{ color: dark ? "#60a5fa" : "var(--accent)" }}
          >
            {study.tag}
          </span>

          <h1
            className="mt-3 text-3xl leading-tight font-bold tracking-tight sm:text-4xl lg:text-5xl"
            style={{ color: dark ? "#f1f5f9" : "var(--text-primary)" }}
          >
            {study.title}
          </h1>

          <p
            className="mt-4 max-w-3xl text-lg leading-relaxed"
            style={{ color: dark ? "#94a3b8" : "var(--text-secondary)" }}
          >
            {study.subtitle}
          </p>

          <div className="mt-8 flex flex-wrap gap-x-8 gap-y-3">
            {[
              { label: "Role", value: study.role },
              { label: "Period", value: study.period },
              { label: "Type", value: study.projectType },
            ].map(item => (
              <div key={item.label}>
                <span
                  className="mb-1 block text-xs font-semibold tracking-widest uppercase"
                  style={{ color: dark ? "#475569" : "#9ca3af" }}
                >
                  {item.label}
                </span>
                <span
                  className="text-sm font-medium"
                  style={{ color: dark ? "#e2e8f0" : "var(--text-primary)" }}
                >
                  {item.value}
                </span>
              </div>
            ))}
          </div>

          <div className="mt-6 flex flex-wrap gap-2">
            {study.tech.map(t => (
              <span
                key={t}
                className="inline-flex items-center rounded-md px-2.5 py-1 font-mono text-xs font-medium"
                style={{
                  backgroundColor: dark ? "#1e293b" : "var(--pill-bg)",
                  color: dark ? "#94a3b8" : "var(--pill-text)",
                }}
              >
                {t}
              </span>
            ))}
          </div>
        </div>
      </header>

      {/* ── Hero Image ───────────────────────────────────────────────────── */}
      <section
        style={{
          backgroundColor: dark ? "#0d1f35" : "#f8fafc",
          borderBottom: dark ? "1px solid #1e293b" : "1px solid var(--card-border)",
        }}
      >
        <div className="mx-auto max-w-5xl px-6 py-16 lg:px-8">
          {study.heroImage ? (
            <BrowserMockup
              src={study.heroImage}
              alt={`${study.title} — hero screenshot`}
              darkChrome={dark}
              priority
            />
          ) : (
            <CaseStudyHeroBanner
              title={study.title}
              role={study.role}
              period={study.period}
              tag={study.tag}
              dark={dark}
            />
          )}
        </div>
      </section>

      {/* ── The Challenge ────────────────────────────────────────────────── */}
      <section
        style={{
          backgroundColor: dark ? "#0a1628" : "var(--bg-section)",
          borderBottom: dark ? "1px solid #1e293b" : "1px solid var(--card-border)",
        }}
      >
        <div className="mx-auto max-w-5xl px-6 py-20 lg:px-8 lg:py-24">
          <div className="max-w-3xl">
            <span
              className="mb-3 block text-xs font-semibold tracking-widest uppercase"
              style={{ color: dark ? "#60a5fa" : "var(--accent)" }}
            >
              Background
            </span>
            <h2
              className="mb-8 text-2xl font-semibold lg:text-3xl"
              style={{ color: dark ? "#f1f5f9" : "var(--text-primary)" }}
            >
              The Challenge
            </h2>
            <div
              className="space-y-5 text-base leading-relaxed lg:text-[17px]"
              style={{ color: dark ? "#94a3b8" : "var(--text-secondary)" }}
            >
              {study.challenge.map((paragraph, i) => (
                <p key={i}>{paragraph}</p>
              ))}
            </div>
          </div>
        </div>
      </section>

      {/* ── The Solution ─────────────────────────────────────────────────── */}
      <section
        style={{
          backgroundColor: dark ? "#0d1f35" : "var(--bg-primary)",
        }}
      >
        <div className="mx-auto max-w-5xl px-6 py-20 lg:px-8 lg:py-24">
          <span
            className="mb-3 block text-xs font-semibold tracking-widest uppercase"
            style={{ color: dark ? "#60a5fa" : "var(--accent)" }}
          >
            What I Built
          </span>
          <h2
            className="mb-16 text-2xl font-semibold lg:text-3xl"
            style={{ color: dark ? "#f1f5f9" : "var(--text-primary)" }}
          >
            The Solution
          </h2>

          <div className="space-y-24">
            {study.solution.modules.map((module, index) => {
              const isEven = index % 2 === 0

              if (module.isFlow && module.flow) {
                return (
                  <div key={module.name}>
                    <div
                      className="mb-4"
                      style={{
                        borderLeft: `3px solid ${dark ? "#3b82f6" : "var(--accent)"}`,
                        paddingLeft: "16px",
                      }}
                    >
                      <h3
                        className="mb-3 text-xl font-semibold"
                        style={{
                          color: dark ? "#f1f5f9" : "var(--text-primary)",
                        }}
                      >
                        {module.name}
                      </h3>
                      <p
                        className="text-base leading-relaxed"
                        style={{
                          color: dark ? "#94a3b8" : "var(--text-secondary)",
                        }}
                      >
                        {module.description}
                      </p>
                    </div>
                    <div className="mt-8 flex flex-wrap items-center gap-3">
                      {module.flow.map((step, si) => (
                        <div key={step} className="flex items-center gap-3">
                          <div
                            className="rounded-xl px-5 py-3 text-center text-sm font-medium"
                            style={{
                              backgroundColor: dark ? "#1e293b" : "#f1f5f9",
                              color: dark ? "#e2e8f0" : "var(--text-primary)",
                              border: dark ? "1px solid #334155" : "1px solid var(--card-border)",
                            }}
                          >
                            {step}
                          </div>
                          {si < module.flow!.length - 1 && (
                            <svg
                              className="h-5 w-5 shrink-0"
                              fill="none"
                              viewBox="0 0 24 24"
                              stroke="currentColor"
                              style={{
                                color: dark ? "#3b82f6" : "var(--accent)",
                              }}
                            >
                              <path
                                strokeLinecap="round"
                                strokeLinejoin="round"
                                strokeWidth={2}
                                d="M17 8l4 4m0 0l-4 4m4-4H3"
                              />
                            </svg>
                          )}
                        </div>
                      ))}
                    </div>
                  </div>
                )
              }

              return (
                <div
                  key={module.name}
                  className={`grid grid-cols-1 items-center gap-10 lg:grid-cols-2 lg:gap-16 ${
                    isEven ? "" : "lg:[&>*:first-child]:order-2"
                  }`}
                >
                  <div>
                    <div
                      style={{
                        borderLeft: `3px solid ${dark ? "#3b82f6" : "var(--accent)"}`,
                        paddingLeft: "16px",
                      }}
                    >
                      <h3
                        className="mb-3 text-xl font-semibold"
                        style={{
                          color: dark ? "#f1f5f9" : "var(--text-primary)",
                        }}
                      >
                        {module.name}
                      </h3>
                      <p
                        className="text-base leading-relaxed"
                        style={{
                          color: dark ? "#94a3b8" : "var(--text-secondary)",
                        }}
                      >
                        {module.description}
                      </p>
                    </div>
                  </div>
                  {module.screenshot && (
                    <div
                      style={{
                        backgroundColor: dark ? "#0a1628" : "#f8fafc",
                        borderRadius: "16px",
                        padding: "16px",
                        border: dark ? "1px solid #1e293b" : "1px solid var(--card-border)",
                      }}
                    >
                      <BrowserMockup
                        src={module.screenshot}
                        alt={`${module.name} screenshot`}
                        darkChrome={dark}
                      />
                    </div>
                  )}
                </div>
              )
            })}
          </div>
        </div>
      </section>

      {/* ── Technical Approach ───────────────────────────────────────────── */}
      <section
        style={{
          backgroundColor: dark ? "#0a1628" : "var(--bg-section)",
          borderTop: dark ? "1px solid #1e293b" : "1px solid var(--card-border)",
          borderBottom: dark ? "1px solid #1e293b" : "1px solid var(--card-border)",
        }}
      >
        <div className="mx-auto max-w-5xl px-6 py-20 lg:px-8 lg:py-24">
          <span
            className="mb-3 block text-xs font-semibold tracking-widest uppercase"
            style={{ color: dark ? "#60a5fa" : "var(--accent)" }}
          >
            Engineering
          </span>
          <h2
            className="mb-12 text-2xl font-semibold lg:text-3xl"
            style={{ color: dark ? "#f1f5f9" : "var(--text-primary)" }}
          >
            Technical Approach
          </h2>
          <div className="grid grid-cols-1 gap-6 sm:grid-cols-2">
            {study.technicalApproach.map(item => (
              <div
                key={item.technology}
                className="rounded-xl p-6"
                style={{
                  backgroundColor: dark ? "#0d1f35" : "#f8fafc",
                  border: dark ? "1px solid #1e293b" : "1px solid var(--card-border)",
                  borderLeft: `4px solid ${dark ? "#3b82f6" : "var(--accent)"}`,
                }}
              >
                <h3
                  className="mb-2 font-mono text-base font-semibold"
                  style={{ color: dark ? "#e2e8f0" : "var(--text-primary)" }}
                >
                  {item.technology}
                </h3>
                <p
                  className="text-sm leading-relaxed"
                  style={{ color: dark ? "#94a3b8" : "var(--text-secondary)" }}
                >
                  {item.description}
                </p>
              </div>
            ))}
          </div>
        </div>
      </section>

      {/* ── Outcome ──────────────────────────────────────────────────────── */}
      <section
        style={{
          backgroundColor: dark ? "#0d1f35" : "var(--bg-primary)",
        }}
      >
        <div className="mx-auto max-w-5xl px-6 py-20 lg:px-8 lg:py-24">
          <div className="max-w-3xl">
            <span
              className="mb-3 block text-xs font-semibold tracking-widest uppercase"
              style={{ color: dark ? "#60a5fa" : "var(--accent)" }}
            >
              Impact
            </span>
            <h2
              className="mb-6 text-2xl font-semibold lg:text-3xl"
              style={{ color: dark ? "#f1f5f9" : "var(--text-primary)" }}
            >
              Outcome
            </h2>
            <p
              className="text-base leading-relaxed lg:text-[17px]"
              style={{ color: dark ? "#94a3b8" : "var(--text-secondary)" }}
            >
              {study.outcome}
            </p>
          </div>
        </div>
      </section>

      {/* ── CTA ──────────────────────────────────────────────────────────── */}
      <section className="relative overflow-hidden" style={{ backgroundColor: "#0a1628" }}>
        <div
          className="pointer-events-none absolute inset-0"
          aria-hidden
          style={{
            background:
              "radial-gradient(ellipse at 60% 50%, rgba(37,99,235,0.18) 0%, transparent 65%)",
          }}
        />
        <div className="relative mx-auto max-w-5xl px-6 py-28 lg:px-8 lg:py-36">
          <div className="max-w-2xl">
            <span
              className="mb-6 inline-flex items-center gap-2 text-xs font-semibold tracking-widest uppercase"
              style={{ color: "#60a5fa" }}
            >
              <span
                className="inline-block h-2 w-2 rounded-full"
                style={{ backgroundColor: "#22c55e" }}
              />
              Available for Projects
            </span>
            <h2
              className="mb-6 text-4xl leading-[1.1] font-bold tracking-tight sm:text-5xl"
              style={{ color: "#f1f5f9" }}
            >
              Have a project
              <br />
              <span style={{ color: "#3b82f6" }}>in mind?</span>
            </h2>
            <p className="mb-12 max-w-xl text-lg leading-relaxed" style={{ color: "#94a3b8" }}>
              I&apos;m available for freelance projects worldwide — from leading your frontend from
              day one, to modernizing an existing app, to building an MVP end-to-end.
            </p>
            <div className="flex flex-wrap items-center gap-4">
              <a
                href="mailto:contact@overmartinez.com"
                className="inline-flex items-center gap-2 rounded-lg px-7 py-4 text-base font-semibold text-white transition-colors duration-200"
                style={{ backgroundColor: "#2563eb" }}
              >
                contact@overmartinez.com
                <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                  <path
                    strokeLinecap="round"
                    strokeLinejoin="round"
                    strokeWidth={2}
                    d="M17 8l4 4m0 0l-4 4m4-4H3"
                  />
                </svg>
              </a>
              <a
                href="https://linkedin.com/in/over-martinez"
                target="_blank"
                rel="noopener noreferrer"
                className="text-sm font-medium transition-opacity hover:opacity-60"
                style={{ color: "#94a3b8" }}
              >
                LinkedIn ↗
              </a>
              <a
                href="https://github.com/oversio"
                target="_blank"
                rel="noopener noreferrer"
                className="text-sm font-medium transition-opacity hover:opacity-60"
                style={{ color: "#94a3b8" }}
              >
                GitHub ↗
              </a>
            </div>
          </div>
        </div>
      </section>

      {/* ── Footer ───────────────────────────────────────────────────────── */}
      <footer
        style={{
          backgroundColor: dark ? "#060f1e" : "var(--bg-section)",
          borderTop: dark ? "1px solid #1e293b" : "1px solid var(--card-border)",
        }}
      >
        <div className="mx-auto max-w-5xl px-6 py-8 lg:px-8">
          <div className="flex flex-col items-center justify-between gap-4 sm:flex-row">
            <Link
              href="/"
              className="text-sm font-medium transition-opacity hover:opacity-60"
              style={{ color: dark ? "#94a3b8" : "var(--text-secondary)" }}
            >
              ← Over Martinez
            </Link>
            <p className="text-sm" style={{ color: dark ? "#475569" : "var(--text-secondary)" }}>
              © 2026 Over Martinez
            </p>
            <div className="flex items-center gap-6">
              <a
                href="https://linkedin.com/in/over-martinez"
                target="_blank"
                rel="noopener noreferrer"
                className="text-sm transition-opacity hover:opacity-60"
                style={{ color: dark ? "#94a3b8" : "var(--text-secondary)" }}
              >
                LinkedIn
              </a>
              <a
                href="https://github.com/oversio"
                target="_blank"
                rel="noopener noreferrer"
                className="text-sm transition-opacity hover:opacity-60"
                style={{ color: dark ? "#94a3b8" : "var(--text-secondary)" }}
              >
                GitHub
              </a>
            </div>
          </div>
        </div>
      </footer>
    </div>
  )
}
