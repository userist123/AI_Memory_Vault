import Image from "next/image"

interface BrowserMockupProps {
  src: string
  alt: string
  darkChrome?: boolean
  priority?: boolean
}

export default function BrowserMockup({
  src,
  alt,
  darkChrome = false,
  priority = false,
}: BrowserMockupProps) {
  return (
    <div
      className="w-full overflow-hidden rounded-xl"
      style={{
        boxShadow: "0 4px 6px -1px rgba(0,0,0,0.08), 0 20px 50px -10px rgba(0,0,0,0.15)",
        border: darkChrome ? "1px solid #2d3748" : "1px solid #e5e7eb",
      }}
    >
      <div
        className="flex items-center gap-1.5 px-4 py-3"
        style={{
          backgroundColor: darkChrome ? "#1e293b" : "#f1f5f9",
          borderBottom: darkChrome ? "1px solid #334155" : "1px solid #e5e7eb",
        }}
      >
        <span className="h-3 w-3 rounded-full bg-red-400 opacity-80" />
        <span className="h-3 w-3 rounded-full bg-yellow-400 opacity-80" />
        <span className="h-3 w-3 rounded-full bg-green-400 opacity-80" />
      </div>
      <div className="relative overflow-hidden">
        <Image
          src={src}
          alt={alt}
          width={1200}
          height={750}
          className="screenshot-blurred block h-auto w-full"
          style={{ display: "block" }}
          quality={90}
          priority={priority}
        />
      </div>
    </div>
  )
}
