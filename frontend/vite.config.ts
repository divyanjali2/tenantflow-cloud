import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  // Prebundle the lazy dashboard dependency before serving modules to the browser.
  optimizeDeps: { include: ['recharts'] },
  server: {
    host: 'localhost', port: 5173, strictPort: true,
    proxy: {
      '/api': {
        target: 'https://localhost:7102', changeOrigin: true,
        // Local development only: ASP.NET's self-signed development certificate.
        secure: false,
      },
    },
  },
})
