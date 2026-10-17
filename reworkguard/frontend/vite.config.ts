import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': { target: process.env.VITE_API_PROXY_TARGET ?? 'http://localhost:5080', changeOrigin: false },
      '/hubs': { target: process.env.VITE_API_PROXY_TARGET ?? 'http://localhost:5080', changeOrigin: false, ws: true },
      '/hangfire': { target: process.env.VITE_API_PROXY_TARGET ?? 'http://localhost:5080', changeOrigin: false },
    },
  },
  build: {
    rollupOptions: {
      output: {
        manualChunks: {
          react: ['react', 'react-dom', 'react-router-dom'],
          mui: ['@mui/material', '@emotion/react', '@emotion/styled'],
          data: ['@tanstack/react-query', 'react-hook-form', 'zod', '@hookform/resolvers'],
          charts: ['recharts'],
          realtime: ['@microsoft/signalr'],
        },
      },
    },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
    css: false,
    testTimeout: 30_000,
  },
});
