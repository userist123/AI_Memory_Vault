import CVDownload from "../CVDownload"

export default function Hero() {
  return (
    <section className="relative overflow-hidden">
      <div className="pointer-events-none absolute inset-0 -z-10" aria-hidden>
        <div
          className="absolute top-0 right-0 h-full w-1/2 opacity-[0.04]"
          style={{
            background: "radial-gradient(ellipse at 80% 40%, #2563eb 0%, transparent 70%)",
          }}
        />
        <div
          className="absolute top-24 right-48 h-80 w-80 rounded-full"
          style={{ border: "1.5px solid rgba(37,99,235,0.12)" }}
        />
        <div
          className="absolute top-48 right-16 h-56 w-56 rounded-full"
          style={{ border: "1px solid rgba(37,99,235,0.08)" }}
        />
      </div>

      <div className="mx-auto max-w-6xl px-6 pt-28 pb-32 lg:px-8 lg:pt-36 lg:pb-40">
        <div className="max-w-3xl">
          <p
            className="mb-5 text-xs font-semibold tracking-[0.2em] uppercase"
            style={{ color: "var(--accent)" }}
          >
            Senior Frontend Engineer
          </p>
          <h1
            className="text-4xl leading-[1.1] font-bold tracking-tight sm:text-5xl lg:text-6xl"
            style={{ color: "var(--text-primary)" }}
          >
            I build web products that are{" "}
            <span
              style={{
                background: "linear-gradient(135deg, #2563eb 0%, #7c3aed 100%)",
                WebkitBackgroundClip: "text",
                WebkitTextFillColor: "transparent",
                backgroundClip: "text",
              }}
            >
              structured to grow
            </span>{" "}
            — not just to launch.
          </h1>
          <p
            className="mt-6 text-xl font-medium tracking-tight"
            style={{ color: "var(--text-primary)" }}
          >
            I build solutions. The technology follows.
          </p>
          <p
            className="mt-4 max-w-xl text-lg leading-relaxed"
            style={{ color: "var(--text-secondary)" }}
          >
            10+ years helping startups, product teams, and agencies build scalable web applications
            with React, TypeScript, and Next.js.
          </p>
          <div className="mt-10 flex flex-wrap gap-4">
            <a
              href="#work"
              className="inline-flex items-center justify-center rounded-lg bg-blue-600 px-6 py-3 text-sm font-semibold text-white transition-colors duration-200 hover:bg-blue-700"
            >
              See My Work
            </a>
            <a
              href="#contact"
              className="inline-flex items-center justify-center rounded-lg border px-6 py-3 text-sm font-semibold transition-colors duration-200 hover:bg-gray-50"
              style={{
                color: "var(--text-primary)",
                borderColor: "var(--card-border)",
              }}
            >
              Get in Touch
            </a>
            <CVDownload size="md" />
          </div>
        </div>
      </div>
    </section>
  )
}
