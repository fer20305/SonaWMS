import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
export default defineConfig({
  plugins: [react()],
  server: { proxy: { '/api': { target: 'http://127.0.0.1:18080', ws: true } } },
  // el build va directo a la carpeta que el servidor sirve como wwwroot (servidor/panel-web)
  build: { outDir: '../servidor/panel-web', emptyOutDir: true, chunkSizeWarningLimit: 900 },
});
