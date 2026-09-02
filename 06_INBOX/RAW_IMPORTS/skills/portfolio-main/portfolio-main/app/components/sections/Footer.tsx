export default function Footer() {
  return (
    <footer className="bg-bg-section border-card-border border-t">
      <div className="mx-auto max-w-6xl px-6 py-8 lg:px-8">
        <div className="flex flex-col items-center justify-between gap-4 sm:flex-row">
          <p className="text-sm" style={{ color: "var(--text-secondary)" }}>
            © 2026 Over Martinez
          </p>
          <div className="flex items-center gap-6">
            <a
              href="https://linkedin.com/in/over-martinez"
              target="_blank"
              rel="noopener noreferrer"
              className="text-sm transition-opacity hover:opacity-60"
              style={{ color: "var(--text-secondary)" }}
            >
              LinkedIn
            </a>
            <a
              href="https://github.com/oversio"
              target="_blank"
              rel="noopener noreferrer"
              className="text-sm transition-opacity hover:opacity-60"
              style={{ color: "var(--text-secondary)" }}
            >
              GitHub
            </a>
            <a
              href="mailto:contact@overmartinez.com"
              className="text-sm transition-opacity hover:opacity-60"
              style={{ color: "var(--text-secondary)" }}
            >
              Email
            </a>
          </div>
          <p className="text-text-secondary text-xs">Built with Next.js &amp; Tailwind CSS</p>
        </div>
      </div>
    </footer>
  )
}
