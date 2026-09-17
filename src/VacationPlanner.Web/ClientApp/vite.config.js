import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      // dev: SPA на :5173, API на :5000
      '/api': 'http://localhost:5000'
    }
  },
  build: {
    outDir: '../wwwroot', // продакшн-сборка уходит в static API
    emptyOutDir: true
  }
});