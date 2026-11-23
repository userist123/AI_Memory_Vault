/**
 * Test-only re-exports so suites under tests/ui (outside this package) resolve their tooling from here.
 * Never imported by src/.
 */
export { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
export { render, screen, cleanup, act, fireEvent } from "@testing-library/react";
export { createElement } from "react";
