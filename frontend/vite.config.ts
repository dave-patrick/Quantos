import { defineConfig } from 'vite';
import { svelte } from '@sveltejs/vite-plugin-svelte';

// Dev server proxies the API to a running backend. Start the backend first
// (dotnet run --project src/Quantos.Backend -- --open), then `npm run dev`.
const backend = process.env.QUANTOS_BACKEND ?? 'http://127.0.0.1:47821';

export default defineConfig({
  plugins: [svelte()],
  base: '/',
  build: {
    outDir: '../src/Quantos.Backend/wwwroot',
    emptyOutDir: true,
    target: 'es2022',
    chunkSizeWarningLimit: 1500,
    sourcemap: false
  },
  server: {
    // Help shows the guides from documentation/ (one folder up), and About the LICENSE beside it.
    fs: { allow: ['..'] },
    port: 5173,
    strictPort: true,
    proxy: {
      '/api': {
        target: backend,
        changeOrigin: true,
        configure: (proxy) => {
          // The backend rejects unexpected origins; the dev proxy is same-machine, so drop it.
          proxy.on('proxyReq', (req) => req.removeHeader('origin'));
        }
      },
      '/auth': { target: backend, changeOrigin: true },
      '/plugin-host': { target: backend, changeOrigin: true }
    }
  }
});
