import { defineConfig } from 'vitest/config'

export default defineConfig({
  test: {
    // jsdom for the one component test; everything else is pure and does not need a DOM.
    environment: 'jsdom',
    include: ['src/**/*.test.ts', 'src/**/*.test.tsx'],
  },
})
