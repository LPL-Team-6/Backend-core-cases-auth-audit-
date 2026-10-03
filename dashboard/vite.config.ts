import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// Port 4200 matches the API's dev CORS policy, but the /api proxy means the browser never
// makes a cross-origin call anyway. Point API_TARGET at the Docker container (port 8080) or
// a deployed API as needed.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 4200,
    proxy: {
      "/api": process.env.API_TARGET ?? "http://localhost:5020",
    },
  },
});
