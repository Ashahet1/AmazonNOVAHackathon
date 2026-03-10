import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  server: {
    // In dev mode, proxy /api/* to the C# backend on port 5174
    proxy: {
      '/api': 'http://localhost:5174',
    },
  },
})
