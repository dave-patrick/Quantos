/// <reference types="vitest/config" />
import { defineConfig } from 'vite';
import { svelte } from '@sveltejs/vite-plugin-svelte';
import { readFileSync } from 'node:fs';

// Dev server proxies the API to a running backend. Start the backend first
// (dotnet run --project src/Quantos.Backend -- --open), then `npm run dev`.
const backend = process.env.QUANTOS_BACKEND ?? 'http://127.0.0.1:47821';

export default defineConfig({
  plugins: [svelte()],
  base: '/',
  // The build mark names the commit a package was built from. It is only set for a staging copy
  // (build/build.ps1 -Mark); a released build has none.
  define: {
    __QUANTOS_BUILD__: JSON.stringify(process.env.QUANTOS_BUILD ?? ''),
    __QUANTOS_BUILD_ID__: JSON.stringify(readFileSync(new URL('../build/build-id.txt', import.meta.url), 'utf8').trim())
  },
  build: {
    outDir: '../src/Quantos.Backend/wwwroot',
    emptyOutDir: true,
    target: 'es2022',
    chunkSizeWarningLimit: 1500,
    // Fonts stay files: the backend's content security policy allows fonts only from the app itself
    // (font-src 'self'), so a small font inlined as a data: URL (KaTeX's) would be blocked.
    assetsInlineLimit: (file) => (/\.(woff2?|ttf|otf|eot)$/i.test(file) ? false : undefined),
    sourcemap: false
  },
  test: {
    setupFiles: ['./tests/setup.ts']
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
