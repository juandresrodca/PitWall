import { defineConfig } from 'astro/config';
import svelte from '@astrojs/svelte';

// The build lands inside the .NET server project, which embeds it into PitWall.exe.
// `npm run dev` proxies the API to a PitWall running locally (PitWall.exe --headless).
export default defineConfig({
  output: 'static',
  outDir: '../src/PitWall.Server/wwwroot',
  build: { format: 'directory' },
  integrations: [svelte()],
  devToolbar: { enabled: false },
  vite: {
    server: {
      proxy: {
        '/api': { target: 'http://127.0.0.1:5320', changeOrigin: true },
      },
    },
  },
});
