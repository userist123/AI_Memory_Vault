"use client"

import { useEffect, useRef, useState } from "react"

interface CVDownloadProps {
  size?: "sm" | "md"
}

export default function CVDownload({ size = "sm" }: CVDownloadProps) {
  const [open, setOpen] = useState(false)
  const ref = useRef<HTMLDivElement>(null)

  useEffect(() => {
    function handleClickOutside(e: MouseEvent) {
      if (ref.current && !ref.current.contains(e.target as Node)) {
        setOpen(false)
      }
    }
    document.addEventListener("mousedown", handleClickOutside)
    return () => document.removeEventListener("mousedown", handleClickOutside)
  }, [])

  return (
    <div ref={ref} className="relative">
      <button
        onClick={() => setOpen(prev => !prev)}
        className={`inline-flex cursor-pointer items-center justify-center gap-2 rounded-lg border text-sm font-semibold transition-colors duration-200 hover:bg-gray-50 ${size === "md" ? "px-6 py-3" : "px-4 py-2"}`}
        style={{ color: "var(--text-primary)", borderColor: "var(--card-border)" }}
        aria-expanded={open}
        aria-haspopup="true"
      >
        <svg
          className="h-4 w-4 shrink-0"
          fill="none"
          viewBox="0 0 24 24"
          stroke="currentColor"
          strokeWidth={2}
          aria-hidden
        >
          <path
            strokeLinecap="round"
            strokeLinejoin="round"
            d="M4 16v2a2 2 0 002 2h12a2 2 0 002-2v-2"
          />
          <path strokeLinecap="round" strokeLinejoin="round" d="M12 4v12m0 0l-4-4m4 4l4-4" />
        </svg>
        Download CV
        <svg
          className={`h-3.5 w-3.5 shrink-0 transition-transform duration-200 ${open ? "rotate-180" : ""}`}
          fill="none"
          viewBox="0 0 24 24"
          stroke="currentColor"
          strokeWidth={2.5}
          aria-hidden
        >
          <path strokeLinecap="round" strokeLinejoin="round" d="M19 9l-7 7-7-7" />
        </svg>
      </button>

      {open && (
        <div
          className="absolute left-0 z-50 mt-2 w-44 overflow-hidden rounded-lg border shadow-lg"
          style={{
            backgroundColor: "rgba(250,250,250,0.97)",
            backdropFilter: "blur(12px)",
            borderColor: "var(--card-border)",
          }}
        >
          {[
            {
              label: "English",
              flag: "🇬🇧",
              file: "/cv/Over%20Martinez%20-%20EN.pdf",
              name: "Over Martinez - EN.pdf",
            },
            {
              label: "Español",
              flag: "🇪🇸",
              file: "/cv/Over%20Martinez%20-%20ES.pdf",
              name: "Over Martinez - ES.pdf",
            },
          ].map(({ label, flag, file, name }) => (
            <a
              key={label}
              href={file}
              download={name}
              onClick={() => setOpen(false)}
              className="flex items-center gap-3 px-4 py-3 text-sm font-medium transition-colors duration-150"
              style={{ color: "var(--text-primary)" }}
              onMouseEnter={e => (e.currentTarget.style.backgroundColor = "rgba(37,99,235,0.06)")}
              onMouseLeave={e => (e.currentTarget.style.backgroundColor = "transparent")}
            >
              <span className="text-base leading-none">{flag}</span>
              {label}
            </a>
          ))}
        </div>
      )}
    </div>
  )
}
