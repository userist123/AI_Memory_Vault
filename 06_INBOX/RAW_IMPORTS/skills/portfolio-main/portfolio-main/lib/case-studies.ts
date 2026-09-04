import lenosoftData from "@/data/case-studies/lenosoft.json"
import amberData from "@/data/case-studies/amber.json"
import uplannerData from "@/data/case-studies/uplanner.json"

export interface CaseStudyModule {
  name: string
  description: string
  screenshot: string | null
  isFlow?: boolean
  flow?: string[]
}

export interface TechnicalApproachItem {
  technology: string
  description: string
}

export interface CaseStudy {
  id: string
  title: string
  subtitle: string
  tag: string
  role: string
  period: string
  projectType: string
  tech: string[]
  cardThumbnail: string | null
  heroImage: string | null
  darkTheme: boolean
  challenge: string[]
  solution: {
    modules: CaseStudyModule[]
  }
  technicalApproach: TechnicalApproachItem[]
  outcome: string
}

const caseStudies: CaseStudy[] = [
  lenosoftData as CaseStudy,
  amberData as CaseStudy,
  uplannerData as CaseStudy,
]

export function getCaseStudy(id: string): CaseStudy | null {
  return caseStudies.find(cs => cs.id === id) ?? null
}

export function getAllCaseStudies(): CaseStudy[] {
  return caseStudies
}
