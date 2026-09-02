# Portfolio Specification — Over Martinez

## Senior Frontend Engineer · Freelance

---

## 1. General Overview

Design a professional single-page portfolio website for a Senior Frontend Engineer with 10+ years of experience. The site should feel modern, clean, and premium — communicating technical expertise and professionalism without being flashy or overdesigned. The target audience is CTOs, tech leads, startup founders, and agencies looking to hire a senior freelance developer.

The portfolio is NOT a template showcase — it's a conversion tool. Every section should guide the visitor toward contacting Over for a project.

---

## 2. Site Structure

Single-page layout with smooth scroll navigation + dedicated pages for each case study.

### Navigation (sticky top bar)

- Logo/Name: "Over Martinez" (text-based, no graphic logo needed)
- Nav links: Work · About · Contact
- CTA button in nav: "Let's Talk" (scrolls to contact section)

---

## 3. Sections & Content

### 3.1 — Hero Section

**Layout:** Full-width, vertically centered content. Clean and spacious with generous whitespace.

**Content:**

- Small label above headline: "Senior Frontend Engineer"
- Main headline: "I build web products that are structured to grow — not just to launch."
- Subtext: "10+ years helping startups, product teams, and agencies build scalable web applications with React, TypeScript, and Next.js."
- Primary CTA button: "See My Work" (scrolls to Work section)
- Secondary CTA link: "Get in Touch" (scrolls to Contact section)

**Design notes:**

- No hero image or background photo needed — let typography and whitespace do the work.
- The headline should be large and bold, the subtext should be lighter in weight.
- Consider a subtle gradient or a minimal geometric accent element for visual interest.

---

### 3.2 — Work Section (Case Studies Grid)

**Section title:** "Selected Work"

**Layout:** Grid of project cards (2 columns on desktop, 1 on mobile). Each card links to a dedicated case study page.

**Project Card 1 — Featured (larger card):**

- Project title: "Digital Document Management & E-Signature Platform"
- Tag/Label: "SaaS Platform · Built from Scratch"
- Short description: "A full-featured platform enabling property brokers to create, manage, and digitally sign documents at scale — with dynamic templates, mass contract generation, and legally validated digital signatures."
- Tech tags: React · Next.js · Zustand · Tailwind CSS
- Thumbnail: Use the dashboard screenshot
- CTA: "View Case Study →"

**Project Card 2:**

- Project title: "Production Asset Management Platform"
- Tag/Label: "Enterprise Platform · NBCUniversal"
- Short description: "A centralized system to organize, track, and reuse production elements across film projects — solving cost overruns caused by lack of asset traceability."
- Tech tags: React · TypeScript · Vite · Tailwind CSS · Storybook
- Thumbnail: Use the project list screenshot (dark UI with data table). This card should have a dark background to match the app's dark theme — creating a nice visual contrast with the light-themed Card 1.
- CTA: "View Case Study →"

**Design notes:**

- Cards should have a subtle hover effect (slight elevation or border change).
- The featured project (card 1) should be visually prominent — larger or with a different layout.
- Tech tags should be small, pill-shaped badges.

---

### 3.3 — About Section

**Section title:** "About"

**Layout:** Two columns — text on one side, optional professional photo placeholder on the other.

**Content:**
"I help startups, product teams, and agencies build web applications that are structured to grow — not just to launch.

Over the past 10+ years I've led frontend architecture on products used by millions of users (including NBCUniversal's platform through Globant), built complete SaaS applications from scratch as a freelancer, and helped teams establish scalable codebases, component libraries, and development standards.

My core strength is taking a product from zero to production — or inheriting a messy codebase and turning it into something maintainable. I specialize in frontend engineering but I'm fully capable on the backend as well, which means I understand the full picture when making architectural decisions."

**Below the text, a horizontal row of key tech icons/labels:**
React · TypeScript · Next.js · Tailwind CSS · Node.js · NestJS · MongoDB · Playwright · Storybook

**Design notes:**

- Keep this section warm and personal but not informal.
- The photo placeholder should be a simple circle or rounded rectangle.
- Tech stack row should use subtle, monochrome icons or just text labels in pill format.

---

### 3.4 — Contact Section

**Section title:** "Let's Build Something"

**Layout:** Centered text with clear contact options.

**Content:**

- Headline: "Have a project in mind?"
- Subtext: "I'm currently available for freelance projects worldwide. Whether you need someone to lead your frontend from day one, modernize an existing application, or build an MVP end-to-end — let's talk."
- Email (prominent): overmartinez@gmail.com
- LinkedIn link: linkedin.com/in/over-martinez
- GitHub link: github.com/overmartinez (or actual username)

**Design notes:**

- The email should be the most prominent element — large, clickable, and visually distinct.
- Social links should be secondary but clearly visible.
- Consider a subtle background color change for this section to visually separate it as the closing CTA.

---

### 3.5 — Footer

**Content:**

- "© 2026 Over Martinez"
- Small links: LinkedIn · GitHub · Email
- Optional: "Built with Next.js & Tailwind CSS" (subtle flex that you practice what you preach)

---

## 4. Case Study Page Template

Each project card links to a dedicated case study page. Design a reusable template with these sections:

### 4.1 — Case Study Header

- Back link: "← Back to Home"
- Project title (large)
- Subtitle / one-line description
- Metadata row: Role · Period · Type of project
- Tech stack pills

### 4.2 — Hero Image

- Full-width or contained screenshot of the main UI
- Presented in a browser mockup frame with rounded corners and subtle shadow
- **IMPORTANT — Screenshot treatment:** ALL screenshots across both case studies must have a subtle Gaussian blur applied to their content (text, data, names, numbers). The blur should be light enough that the overall UI layout, color scheme, and component structure remain clearly visible and recognizable, but strong enough that specific text content is unreadable. This protects client confidentiality while still showcasing the quality of the UI work. Think of it as showing the architecture of the interface without revealing the data. The blur should feel intentional and professional — not like a broken image. A frosted-glass aesthetic works well. This treatment applies to ALL screenshots: dashboard, document list, signer management, project list, project detail, and any other UI capture used in the portfolio.

### 4.3 — The Problem

- Section title: "The Challenge"
- 2-3 paragraphs explaining the business problem and why the project existed
- No screenshots here — just well-written context

### 4.4 — The Solution (Screenshot Gallery)

- Section title: "The Solution"
- Alternating layout: screenshot on one side, description on the other (left-right-left pattern)
- Each module/feature gets its own row:
  - Module name as subtitle
  - 2-3 sentences of description
  - Screenshot in browser mockup frame

**For the E-Signature Platform, the modules are:**

1. Dashboard — Real-time overview of document status
2. Document Management — Full document lifecycle with search, filters, and bulk actions
3. Signer Management — Configure signers, signing modes, and advanced signature with digital certificate
4. Template Engine (no screenshot) — Description only with a flow diagram placeholder: Template with placeholders → Client data injection → Personalized documents → Ready for signature

**For the Production Asset Management Platform (NBCUniversal), the modules are:**

1. Project List — A data-rich table view showing all production projects with assigned users (avatar stacks), creators, timestamps, sorting, search, and pagination. Screenshot available (dark theme UI).
2. Project Detail & Collections — The core of the platform. A complex view with: tree navigation of collections on the left, multiple element columns with searchable/filterable lists in the center, and an element properties panel on the right with editable fields (category, source, state, dates). This view demonstrates the depth of the asset organization system. Screenshot available (dark theme UI).
3. Login — Clean, branded entry point with the AMBER logo. Can be used as a secondary visual or as the hero image for this case study since the dark theme with gold branding is visually striking.

**Design note for NBCUniversal case study:** This app has a dark UI theme (dark navy backgrounds with light text). The case study page should accommodate this — consider using a slightly darker background for the screenshot sections so the dark-themed screenshots don't float awkwardly on a white background. A subtle dark section or a neutral gray backdrop behind these screenshots would look natural.

### 4.5 — Technical Decisions

- Section title: "Technical Approach"
- 3-4 blocks, each with:
  - Technology name (bold)
  - 2-3 sentences explaining why it was chosen
- Layout: Grid of cards or simple stacked blocks with subtle left border accent

### 4.6 — Results

- Section title: "Outcome"
- Short paragraph about current status and impact
- Optional: Key metrics if available

### 4.7 — CTA

- "Interested in working together?" + email link
- Consistent with the main page contact section style

---

## 5. Visual Design Guidelines

### Color Palette

**Primary approach:** Dark, professional, and modern. NOT a typical developer portfolio with neon accents.

- **Background (primary):** #FAFAFA or #F8F9FA (very light warm gray)
- **Background (sections alternate):** #FFFFFF
- **Text (primary):** #1A1A2E (near-black with subtle blue undertone)
- **Text (secondary):** #6B7280 (medium gray for descriptions)
- **Accent color:** #2563EB (professional blue) — used sparingly for CTAs, links, and hover states
- **Accent hover:** #1D4ED8 (darker blue)
- **Tech pills background:** #F1F5F9 with #334155 text
- **Card borders:** #E5E7EB (light gray)
- **Card hover border:** #2563EB (accent blue)

**Dark mode (optional but recommended for a dev portfolio):**

- Background: #0F172A
- Cards: #1E293B
- Text primary: #F1F5F9
- Text secondary: #94A3B8
- Accent: #3B82F6

### Typography

- **Headlines:** Inter or Geist — bold weight, large sizes
- **Body:** Inter or Geist — regular weight, 16-18px
- **Code/Tech:** JetBrains Mono or Fira Code — for tech stack labels

**Hierarchy:**

- Hero headline: 48-64px, bold
- Section titles: 32-40px, semibold
- Card titles: 24px, semibold
- Body text: 16-18px, regular
- Tech pills: 13-14px, medium
- Metadata/labels: 12-14px, medium, uppercase tracking

### Spacing & Layout

- Max content width: 1200px centered
- Generous section padding: 120-160px vertical
- Card gap: 32px
- Border radius on cards: 12-16px
- Border radius on pills/badges: 6-8px
- Screenshots in case studies: 8px border radius with subtle box-shadow

### Visual Effects (keep subtle)

- Cards: subtle shadow on hover, slight translateY(-2px) lift
- Links: color transition on hover
- Section transitions: gentle fade-in on scroll (optional)
- Screenshots: displayed in a minimal browser mockup (thin top bar with dots, no actual chrome)
- NO parallax, NO particle backgrounds, NO animated gradients — keep it professional and fast

---

## 6. Responsive Behavior

- **Desktop (1200px+):** Full layout as described
- **Tablet (768-1199px):** Single column for cards, about section stacks vertically
- **Mobile (< 768px):** Hamburger nav, full-width cards, contact section simplified, hero text sizes reduced proportionally

---

## 7. Key Design Principles

1. **Content-first:** Every visual element serves the content. No decoration for decoration's sake.
2. **Whitespace is your friend:** Let sections breathe. Dense portfolios feel overwhelming.
3. **Hierarchy is everything:** The visitor's eye should flow: headline → description → CTA. On every section.
4. **Screenshots are the proof:** Present them beautifully in browser mockups, not as raw pasted images.
5. **Speed over flash:** No heavy animations. The site should feel instant. This is a developer portfolio — performance matters.
6. **Mobile-first thinking:** Many people will see this from a LinkedIn link on their phone first.

---

## 8. What NOT to Include

- No blog section (will be added later when there's a content strategy)
- No testimonials section (will be added when real quotes are available)
- No skills chart or percentage bars (these are meaningless and look amateur)
- No "services" section with generic icons (the case studies already show what Over does)
- No chatbot or AI widget
- No stock photos
