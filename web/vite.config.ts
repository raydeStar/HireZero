import { defineConfig } from "vite";
export default defineConfig({
  publicDir: false,
  build: { outDir: "../src/HireZero.Host/wwwroot", emptyOutDir: true },
});
