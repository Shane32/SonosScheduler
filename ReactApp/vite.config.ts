import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { visualizer } from "rollup-plugin-visualizer";
import { compression } from "vite-plugin-compression2";

// https://vite.dev/config/
export default defineConfig({
  base: process.env.APP_BASE_PATH || "/",
  plugins: [
    react(),
    visualizer(),
    compression({
      algorithms: ["gzip", "brotliCompress"],
    }),
  ],
  build: {
    assetsDir: "static",
  },
  server: {
    // The ASP.NET development proxy always connects to this port.
    port: 5173,
    strictPort: true,
  },
});
