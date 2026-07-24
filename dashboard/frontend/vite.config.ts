import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// In dev, proxy /api to a locally running Functions host (`func start`, :7071),
// or any target via VITE_API_TARGET. Same-origin from the browser's view, so no
// CORS config and no function key are needed for local development.
const target = process.env.VITE_API_TARGET ?? "http://localhost:7071";

export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      "/api": { target, changeOrigin: true },
    },
  },
});
