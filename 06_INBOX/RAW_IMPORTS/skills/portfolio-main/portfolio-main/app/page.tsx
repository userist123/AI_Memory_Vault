import Nav from "./components/Nav"
import Hero from "./components/sections/Hero"
import WorkSection from "./components/sections/WorkSection"
import AboutSection from "./components/sections/AboutSection"
import ContactSection from "./components/sections/ContactSection"
import Footer from "./components/sections/Footer"
import { getAllCaseStudies } from "@/lib/case-studies"

export default function Home() {
  const caseStudies = getAllCaseStudies()

  return (
    <div className="min-h-screen" style={{ backgroundColor: "var(--bg-primary)" }}>
      <Nav variant="home" />
      <Hero />
      <WorkSection studies={caseStudies} />
      <AboutSection />
      <ContactSection />
      <Footer />
    </div>
  )
}
