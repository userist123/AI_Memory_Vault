"use client"

import Link from "next/link"
import { useState } from "react"
import CVDownload from "./CVDownload"

interface NavProps {
  variant?: "home" | "case-study"
}

export default function Nav({ variant = "home" }: NavProps) {
  const [menuOpen, setMenuOpen] = useState(false)

  const navLinks =
    variant === "home"
      ? [
          { label: "Work", href: "#work" },
          { label: "About", href: "#about" },
          { label: "Contact", href: "#contact" },
        ]
      : []

  return (
    <header
      className="sticky top-0 z-50 w-full"
      style={{
        backgroundColor: "rgba(250, 250, 250, 0.92)",
        backdropFilter: "blur(12px)",
        WebkitBackdropFilter: "blur(12px)",
        borderBottom: "1px solid var(--card-border)",
      }}
    >
      <div className="mx-auto max-w-6xl px-6 lg:px-8">
        <div className="flex h-16 items-center justify-between">
          <Link
            href="/"
            className="text-base font-semibold tracking-tight transition-colors"
            style={{ color: "var(--text-primary)" }}
          >
            Over Martinez
          </Link>

          {variant === "home" && (
            <>
              <nav className="hidden items-center gap-8 md:flex">
                {navLinks.map(link => (
                  <a
                    key={link.label}
                    href={link.href}
                    className="text-sm font-medium transition-colors hover:opacity-70"
                    style={{ color: "var(--text-secondary)" }}
                  >
                    {link.label}
                  </a>
                ))}
              </nav>

              <div className="flex items-center gap-4">
                <CVDownload />
                <a
                  href="#contact"
                  className="hidden items-center justify-center rounded-lg px-4 py-2 text-sm font-medium text-white transition-colors md:inline-flex"
                  style={{ backgroundColor: "var(--accent)" }}
                  onMouseEnter={e =>
                    (e.currentTarget.style.backgroundColor = "var(--accent-hover)")
                  }
                  onMouseLeave={e => (e.currentTarget.style.backgroundColor = "var(--accent)")}
                >
                  Let&apos;s Talk
                </a>

                <button
                  className="rounded-md p-2 md:hidden"
                  style={{ color: "var(--text-primary)" }}
                  onClick={() => setMenuOpen(!menuOpen)}
                  aria-label="Toggle menu"
                >
                  {menuOpen ? (
                    <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                      <path
                        strokeLinecap="round"
                        strokeLinejoin="round"
                        strokeWidth={2}
                        d="M6 18L18 6M6 6l12 12"
                      />
                    </svg>
                  ) : (
                    <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                      <path
                        strokeLinecap="round"
                        strokeLinejoin="round"
                        strokeWidth={2}
                        d="M4 6h16M4 12h16M4 18h16"
                      />
                    </svg>
                  )}
                </button>
              </div>
            </>
          )}
        </div>

        {variant === "home" && menuOpen && (
          <div
            className="border-t pt-2 pb-4 md:hidden"
            style={{ borderColor: "var(--card-border)" }}
          >
            {navLinks.map(link => (
              <a
                key={link.label}
                href={link.href}
                className="block py-2.5 text-sm font-medium"
                style={{ color: "var(--text-secondary)" }}
                onClick={() => setMenuOpen(false)}
              >
                {link.label}
              </a>
            ))}
            <div className="mt-3">
              <CVDownload />
            </div>
            <a
              href="#contact"
              className="mt-3 inline-flex items-center justify-center rounded-lg px-4 py-2 text-sm font-medium text-white"
              style={{ backgroundColor: "var(--accent)" }}
              onClick={() => setMenuOpen(false)}
            >
              Let&apos;s Talk
            </a>
          </div>
        )}
      </div>
    </header>
  )
}
