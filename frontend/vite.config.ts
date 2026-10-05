import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  build: {
    // build/build.sh copies this into src/Zuil.Host/wwwroot, where the csproj
    // picks it up as embedded resources — that is what keeps the deliverable a
    // single binary rather than an exe plus a folder of assets.
    outDir: 'dist',
    emptyOutDir: true,
    // Fingerprinted filenames so the long cache lifetime set by the host is safe.
    assetsDir: 'assets',
  },
  server: {
    port: 5173,
    // During development the SPA runs on Vite and the API on Kestrel, so the
    // whole app works on a laptop with no Pi attached.
    proxy: {
      '/api': 'http://localhost:5000',
      '/hub': { target: 'http://localhost:5000', ws: true },
    },
  },
});
