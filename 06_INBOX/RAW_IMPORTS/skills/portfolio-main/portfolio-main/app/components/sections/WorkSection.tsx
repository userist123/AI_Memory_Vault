import ProjectCard from "@/app/components/ProjectCard"
import ProjectCardCompact from "@/app/components/ProjectCardCompact"
import { type CaseStudy } from "@/lib/case-studies"

interface WorkSectionProps {
  studies: CaseStudy[]
}

export default function WorkSection({ studies }: WorkSectionProps) {
  const featured = studies.filter(s => s.cardThumbnail !== null)
  const compact = studies.filter(s => s.cardThumbnail === null)

  return (
    <section id="work" style={{ backgroundColor: "var(--bg-section)" }}>
      <div className="mx-auto max-w-6xl px-6 py-24 lg:px-8 lg:py-32">
        <div className="mb-12">
          <p
            className="mb-3 text-xs font-semibold tracking-widest uppercase"
            style={{ color: "var(--text-secondary)" }}
          >
            Portfolio
          </p>
          <h2
            className="text-3xl font-semibold lg:text-4xl"
            style={{ color: "var(--text-primary)" }}
          >
            Selected Work
          </h2>
        </div>
        <div className="grid grid-cols-1 gap-8 lg:grid-cols-2">
          {featured.map(study => (
            <ProjectCard key={study.id} study={study} />
          ))}
        </div>
        {compact.length > 0 && (
          <div className="mt-16">
            <p
              className="mb-6 text-xs font-semibold tracking-widest uppercase"
              style={{ color: "var(--text-secondary)" }}
            >
              More Work
            </p>
            <div className="flex flex-col gap-3">
              {compact.map(study => (
                <ProjectCardCompact key={study.id} study={study} />
              ))}
            </div>
          </div>
        )}
      </div>
    </section>
  )
}
