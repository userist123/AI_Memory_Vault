import type { NextConfig } from "next"

const nextConfig: NextConfig = {
  images: {
    qualities: [75, 90],
  },
  allowedDevOrigins: ["127.0.0.1"],
}

export default nextConfig
