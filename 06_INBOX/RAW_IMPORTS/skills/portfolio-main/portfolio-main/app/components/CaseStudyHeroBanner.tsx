interface CaseStudyHeroBannerProps {
  title: string
  role: string
  period: string
  tag: string
  dark?: boolean
}

export default function CaseStudyHeroBanner({
  title,
  role,
  period,
  tag,
  dark = false,
}: CaseStudyHeroBannerProps) {
  const accent = dark ? "#3b82f6" : "var(--accent)"
  const textPrimary = dark ? "#f1f5f9" : "var(--text-primary)"
  const textSecondary = dark ? "#475569" : "#d1d5db"
  const bg = dark ? "#0d1f35" : "#f8fafc"
  const border = dark ? "#1e293b" : "var(--card-border)"

  return (
    <div
      className="relative w-full overflow-hidden rounded-2xl"
      style={{ backgroundColor: bg, border: `1px solid ${border}` }}
    >
      {/* Grid pattern */}
      <svg
        className="pointer-events-none absolute inset-0 h-full w-full"
        aria-hidden
        xmlns="http://www.w3.org/2000/svg"
      >
        <defs>
          <pattern id="hero-grid" x="0" y="0" width="40" height="40" patternUnits="userSpaceOnUse">
            <path
              d="M 40 0 L 0 0 0 40"
              fill="none"
              stroke={dark ? "rgba(255,255,255,0.04)" : "rgba(0,0,0,0.05)"}
              strokeWidth="1"
            />
          </pattern>
        </defs>
        <rect width="100%" height="100%" fill="url(#hero-grid)" />
      </svg>

      {/* Gradient glow */}
      <div
        className="pointer-events-none absolute inset-0"
        aria-hidden
        style={{
          background: dark
            ? "radial-gradient(ellipse at 30% 50%, rgba(37,99,235,0.15) 0%, transparent 60%)"
            : "radial-gradient(ellipse at 30% 50%, rgba(37,99,235,0.07) 0%, transparent 60%)",
        }}
      />

      {/* Content */}
      <div className="relative px-10 py-16 lg:px-16 lg:py-20">
        <span
          className="mb-4 block text-xs font-semibold tracking-widest uppercase"
          style={{ color: accent }}
        >
          {tag}
        </span>

        <h2
          className="text-4xl leading-tight font-bold tracking-tight sm:text-5xl lg:text-6xl"
          style={{ color: textPrimary }}
        >
          {title}
        </h2>

        <div className="mt-8 flex flex-wrap gap-x-10 gap-y-4">
          {[
            { label: "Role", value: role },
            { label: "Period", value: period },
          ].map(item => (
            <div key={item.label}>
              <span
                className="mb-1 block text-xs font-semibold tracking-widest uppercase"
                style={{ color: textSecondary }}
              >
                {item.label}
              </span>
              <span className="text-base font-medium" style={{ color: textPrimary }}>
                {item.value}
              </span>
            </div>
          ))}
        </div>
      </div>
    </div>
  )
}
