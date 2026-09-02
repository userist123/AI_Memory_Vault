import Image from "next/image"
import TechPill from "@/app/components/TechPill"

const techStack = [
  "React",
  "TypeScript",
  "Next.js",
  "Tailwind CSS",
  "Node.js",
  "NestJS",
  "MongoDB",
  "SQL",
  "Playwright",
  "Storybook",
  "Hexagonal Architecture",
  "Domain-Driven Design (DDD)",
]

export default function AboutSection() {
  return (
    <section id="about" style={{ backgroundColor: "var(--bg-primary)" }}>
      <div className="mx-auto max-w-6xl px-6 py-24 lg:px-8 lg:py-32">
        <div className="grid grid-cols-1 items-start gap-16 lg:grid-cols-[3fr_2fr]">
          <div>
            <p
              className="mb-3 text-xs font-semibold tracking-widest uppercase"
              style={{ color: "var(--text-secondary)" }}
            >
              About
            </p>
            <h2
              className="mb-8 text-3xl font-semibold lg:text-4xl"
              style={{ color: "var(--text-primary)" }}
            >
              About
            </h2>
            <blockquote
              className="mb-8 border-l-4 pl-5 text-base leading-relaxed lg:text-[17px]"
              style={{
                borderColor: "var(--accent)",
                color: "var(--text-primary)",
              }}
            >
              A great developer&apos;s measure isn&apos;t the technologies they master — it&apos;s
              the value they deliver. I bring 10+ years of frontend expertise to every project, but
              my real commitment is to your outcome, not my comfort zone.
            </blockquote>
            <div
              className="space-y-4 text-base leading-relaxed lg:text-[17px]"
              style={{ color: "var(--text-secondary)" }}
            >
              <p>
                I help startups, product teams, and agencies build web applications that are
                structured to grow — not just to launch.
              </p>
              <p>
                Over the past 10+ years I&apos;ve led frontend architecture on products used by
                millions of users (including NBCUniversal&apos;s platform through Globant), built
                complete SaaS applications from scratch as a freelancer, and helped teams establish
                scalable codebases, component libraries, and development standards.
              </p>
              <p>
                My core strength is taking a product from zero to production — or inheriting a messy
                codebase and turning it into something maintainable. I specialize in frontend
                engineering but I&apos;m fully capable on the backend as well, which means I
                understand the full picture when making architectural decisions.
              </p>
            </div>
            <p
              className="mt-10 mb-4 text-sm font-medium"
              style={{ color: "var(--text-secondary)" }}
            >
              I don&apos;t lead with technology — I lead with your problem. But solving it well
              requires genuine depth across the stack. After 10+ years, that means:
            </p>
            <div className="flex flex-wrap gap-2">
              {techStack.map(tech => (
                <TechPill key={tech} label={tech} />
              ))}
            </div>
          </div>
          <div className="flex justify-center lg:justify-end lg:pt-12">
            <div
              className="relative h-64 w-64 shrink-0 overflow-hidden rounded-2xl lg:h-72 lg:w-72"
              style={{
                boxShadow: "0 4px 6px -1px rgba(0,0,0,0.07), 0 20px 50px -10px rgba(0,0,0,0.12)",
              }}
            >
              <Image
                src="/me.jpg"
                alt="Over Martinez"
                fill
                sizes="(max-width: 768px) 256px, 288px"
                className="object-cover object-top"
                priority
              />
            </div>
          </div>
        </div>
      </div>
    </section>
  )
}
