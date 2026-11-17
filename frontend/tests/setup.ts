import { configure } from '@testing-library/react'

// Multi-request integration screens can take longer to render on Windows when
// test files run concurrently. Keep assertions semantic instead of adding sleeps.
configure({ asyncUtilTimeout: 5000 })

// JSDOM has no layout observer; chart initial dimensions provide its test size.
globalThis.ResizeObserver = class implements ResizeObserver {
  observe() {}
  unobserve() {}
  disconnect() {}
}
