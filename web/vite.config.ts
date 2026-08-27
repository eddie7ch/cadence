import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// Port 5173 is fixed by the contract's port table so this app does not collide
// with the other services on the machine.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    strictPort: true,
  },
  preview: {
    port: 5173,
    strictPort: true,
  },
  build: {
    outDir: 'dist',
    sourcemap: true,
    // Leaflet and Recharts together dwarf the app code; splitting them keeps the
    // entry chunk small enough to parse before the first paint.
    rollupOptions: {
      output: {
        manualChunks: {
          leaflet: ['leaflet', 'react-leaflet'],
          charts: ['recharts'],
        },
      },
    },
  },
});
