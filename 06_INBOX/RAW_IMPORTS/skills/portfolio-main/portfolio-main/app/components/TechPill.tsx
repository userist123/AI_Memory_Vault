interface TechPillProps {
  label: string
  mono?: boolean
}

export default function TechPill({ label, mono = false }: TechPillProps) {
  return (
    <span
      className={`inline-flex items-center rounded-md px-2.5 py-1 text-xs font-medium ${
        mono ? "font-mono" : ""
      }`}
      style={{
        backgroundColor: "var(--pill-bg)",
        color: "var(--pill-text)",
        fontSize: "13px",
      }}
    >
      {label}
    </span>
  )
}
