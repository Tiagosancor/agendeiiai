import { defineConfig, devices } from "@playwright/test";

/**
 * Roda contra o ambiente do `docker compose up` (seção 11, Sprint 3: "fluxo completo
 * coberto por teste ponta a ponta"). Não sobe o próprio servidor — o e2e só faz sentido
 * contra a pilha real (API + Postgres + frontend), então rode `docker compose up -d`
 * antes de `npx playwright test`.
 */
export default defineConfig({
  testDir: "./e2e",
  fullyParallel: false,
  retries: 0,
  workers: 1,
  reporter: "list",
  use: {
    baseURL: "http://acme.agendei.localhost:3000",
    trace: "retain-on-failure",
  },
  projects: [
    {
      name: "chromium",
      use: { ...devices["Desktop Chrome"] },
    },
  ],
});
