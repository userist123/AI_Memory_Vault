/** Test-only Playwright re-exports for suites under tests/ui/e2e. */
export { test, expect, type Page, type BrowserContext } from "@playwright/test";
export * from "./mocks";
