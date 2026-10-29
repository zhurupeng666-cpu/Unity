import { defineConfig } from "@playwright/test";

export default defineConfig({
  testDir: "./tests",
  fullyParallel: false,
  workers: 1,
  use: {
    baseURL: "http://127.0.0.1:5173",
    screenshot: "only-on-failure",
    trace: "retain-on-failure",
  },
  webServer: [
    {
      command: "go run . -addr 127.0.0.1:8080 -data ../work/e2e-issues.json",
      cwd: "../backend",
      url: "http://127.0.0.1:8080/health",
      reuseExistingServer: false,
      timeout: 120000,
    },
    {
      command: "npm run dev",
      url: "http://127.0.0.1:5173",
      reuseExistingServer: false,
    },
  ],
});
