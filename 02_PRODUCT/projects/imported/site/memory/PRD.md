# CrissCustoms & WobArt - Car Wrapping Studio Website

## Project Overview
A premium car wrapping studio website with dark theme and neon pink/red accents. The website showcases services, portfolio, pricing, and includes a complete authentication system with user and admin dashboards.

**Slogan:** "Where Light Meets Art"

## Features Implemented

### 1. Landing Page Sections
- **Hero Section**: Full-screen hero with background image, animated particles, gradient text, and CTA buttons
- **About Section**: Company overview with stats (500+ projects, 400+ clients, 8+ years experience)
- **Services Section**: 6 service cards (Full Wrap, Partial, Reclama, Faruri/Stopuri, PPF, Custom) with hover effects
- **Portfolio Section**: Filterable gallery with lightbox, like functionality, and category filters
- **Process Section**: 6-step process visualization (Consultanta → Design → Aprobare → Pregătire → Colantare → Livrare)
- **Pricing Section**: 3 pricing tiers (Partial Wrap, Full Wrap, PPF Premium)
- **Testimonials Section**: Client reviews with ratings and carousel on mobile
- **FAQ Section**: Accordion-style FAQ
- **Contact Section**: Contact form with validation and contact info cards
- **Footer**: Quick links, services, social media, and contact details

### 2. Authentication System
- Login page with form validation
- Registration page with full validation
- Session management via localStorage
- Protected routes for authenticated users
- Role-based access (admin/user)

### 3. User Dashboard
- Project progress tracking with 8 stages
- Project statistics (active projects, total invested)
- Before/during/after photo tracking
- Message and invoice sections (UI ready)

### 4. Admin Panel
- Revenue and statistics overview
- Project management with status tracking
- Customer management
- Quote management with pending count
- Review moderation (approve/reject)
- Quick action buttons

## Tech Stack
- **Frontend**: React 19, React Router v7
- **Styling**: Tailwind CSS, shadcn/ui components
- **Icons**: Lucide React
- **State Management**: React Context (AuthContext)
- **Notifications**: Sonner toast library
- **Animations**: Custom CSS animations, scroll-triggered effects

## Design System
- **Primary Color**: Neon Pink (HSL 328 100% 54%)
- **Accent Color**: Neon Red (HSL 348 100% 50%)
- **Gold**: HSL 51 100% 50%
- **Background**: Dark (HSL 0 0% 3%)
- **Typography**: Montserrat (headings), Roboto (body)

## Mock Authentication Credentials
- **Admin**: admin@crisscustoms.ro / admin123
- **Client**: client@test.ro / client123

## File Structure
```
/app/frontend/src/
├── components/
│   ├── ui/              # shadcn components
│   ├── Navbar.jsx
│   ├── HeroSection.jsx
│   ├── AboutSection.jsx
│   ├── ServicesSection.jsx
│   ├── PortfolioSection.jsx
│   ├── ProcessSection.jsx
│   ├── PricingSection.jsx
│   ├── TestimonialsSection.jsx
│   ├── FAQSection.jsx
│   ├── ContactSection.jsx
│   └── Footer.jsx
├── pages/
│   ├── HomePage.jsx
│   ├── LoginPage.jsx
│   ├── RegisterPage.jsx
│   ├── DashboardPage.jsx
│   └── AdminPage.jsx
├── contexts/
│   └── AuthContext.js
├── hooks/
│   └── useScrollAnimation.js
├── App.js
├── index.css           # Design system tokens
└── tailwind.config.js
```

## Important Notes
- **MOCK DATA**: This is a frontend prototype. All data is mocked:
  - Authentication uses localStorage
  - Contact form submission is simulated
  - Dashboard projects are hardcoded
  - Admin stats are static mock data
- Backend API integration is ready via `process.env.REACT_APP_BACKEND_URL`

## Next Steps for Full Implementation
1. Connect to real backend API
2. Implement MongoDB database for users, projects, quotes
3. Add real payment integration (Stripe recommended)
4. Implement file upload for project photos
5. Add email notifications (SendGrid)
6. Implement real-time updates (Socket.IO)
