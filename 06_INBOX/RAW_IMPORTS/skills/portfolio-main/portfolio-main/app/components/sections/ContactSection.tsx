export default function ContactSection() {
  return (
    <section
      id="contact"
      className="relative overflow-hidden"
      style={{ backgroundColor: "#0a1628" }}
    >
      {/* Subtle radial glow */}
      <div
        className="pointer-events-none absolute inset-0"
        aria-hidden
        style={{
          background:
            "radial-gradient(ellipse at 60% 50%, rgba(37,99,235,0.18) 0%, transparent 65%)",
        }}
      />

      <div className="relative mx-auto max-w-5xl px-6 py-28 lg:px-8 lg:py-36">
        <div className="max-w-3xl">
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
            className="mb-6 text-4xl leading-[1.1] font-bold tracking-tight sm:text-5xl lg:text-6xl"
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
              className="inline-flex items-center gap-1.5 text-sm font-medium transition-opacity hover:opacity-60"
              style={{ color: "#94a3b8" }}
            >
              LinkedIn ↗
            </a>
            <a
              href="https://github.com/oversio"
              target="_blank"
              rel="noopener noreferrer"
              className="inline-flex items-center gap-1.5 text-sm font-medium transition-opacity hover:opacity-60"
              style={{ color: "#94a3b8" }}
            >
              GitHub ↗
            </a>
          </div>
        </div>
      </div>
    </section>
  )
}
