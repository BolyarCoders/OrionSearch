import path from "node:path";
import { fileURLToPath } from "node:url";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

/** @type {import('next').NextConfig} */
const nextConfig = {
  reactStrictMode: true,
  images: {
    unoptimized: true,
  },
  // Browser calls go to same-origin /backend and Next forwards them to the API,
  // so the hosted API's CORS allow-list does not need to include this origin.
  async rewrites() {
    const api = (process.env.NEXT_PUBLIC_API_URL || "https://orionsearch-api.onrender.com").replace(/\/+$/, "");
    return [{ source: "/backend/:path*", destination: `${api}/:path*` }];
  },
  webpack: (config) => {
    config.resolve.alias["@"] = path.resolve(__dirname, "src");
    return config;
  },
};

export default nextConfig;
