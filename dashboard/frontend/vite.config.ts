import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

export default defineConfig({
  plugins: [react()],
  build: {
    // Build straight into the Flask static dir, which PyInstaller then bundles.
    outDir: "../backend/static",
    emptyOutDir: true,
  },
  server: {
    port: 5179,
    proxy: {
      // In dev the Flask API runs separately; in the packaged app Flask serves both.
      "/api": "http://127.0.0.1:5178",
    },
  },
});
