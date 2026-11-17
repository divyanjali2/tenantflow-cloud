import { defineConfig } from 'vitest/config'

export default defineConfig({
  test: { environment: 'jsdom', setupFiles: ['./tests/setup.ts'], fileParallelism: false, restoreMocks: true, testTimeout: 15_000, include: ['tests/**/*.test.tsx'] },
})
