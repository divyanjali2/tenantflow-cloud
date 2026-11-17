import { configure } from '@testing-library/react'

// Multi-request integration screens can take longer to render on Windows when
// test files run concurrently. Keep assertions semantic instead of adding sleeps.
configure({ asyncUtilTimeout: 5000 })
