import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// Build output goes into the .NET host's wwwroot, which it serves and bundles into
// the published exe. Relative base so assets resolve from the loopback origin.
//
// In dev, proxy /api to the headless .NET host (run: `dotnet run -- --headless`, :5177),
// which in turn forwards to the Azure Functions app with the function key attached
// server-side. Override the target with VITE_API_TARGET if needed.
const target = process.env.VITE_API_TARGET ?? "http://127.0.0.1:5177";

export default defineConfig({
  plugins: [react()],
  base: "./",
  build: {
    outDir: "../SnipeSync.Dashboard/wwwroot",
    emptyOutDir: true,
    sourcemap: false,
  },
  server: {
    proxy: {
      "/api": { target, changeOrigin: true },
    },
  },
});
