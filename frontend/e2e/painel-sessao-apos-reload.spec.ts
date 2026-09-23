import { test, expect } from "@playwright/test";

/**
 * Regressão do bug documentado em CLAUDE.md ("Bugs conhecidos") e corrigido logo em
 * seguida: o cookie de refresh do painel (`SameSite=Strict`) nunca voltava numa chamada
 * cross-site (front em app.agendeiiai.localhost:3000, API em localhost:5080), então um
 * reload completo da página — ou o access token expirando sozinho em 15 min — derrubava
 * a sessão. Corrigido roteando login/renovar/logout pelo proxy same-origin
 * `/painel/auth/*` (`app/painel/auth/[...caminho]/route.ts`).
 *
 * Este teste reproduz o cenário mínimo que expõe o bug: logar e, em seguida, dar um
 * reload de página inteira (não uma navegação por <Link>) numa rota protegida — antes da
 * correção, isso voltava pra tela de login.
 */

const PAINEL_BASE = "http://app.agendeiiai.localhost:3000";
const ADMIN_EMAIL = "admin@acme.dev";
const ADMIN_SENHA = "Admin!123";

test("sessão do painel sobrevive a um reload completo da página", async ({ page }) => {
  await page.goto(`${PAINEL_BASE}/painel/login`);
  await page.getByLabel("E-mail").fill(ADMIN_EMAIL);
  await page.getByLabel("Senha").fill(ADMIN_SENHA);
  await page.getByRole("button", { name: "Entrar" }).click();
  await expect(page).toHaveURL(/\/painel$/);

  // Reload completo (não client-side): reinicia o React e depende do cookie de refresh
  // sobreviver à chamada de renovação — exatamente o que quebrava antes da correção.
  await page.reload();

  await expect(page).toHaveURL(/\/painel$/);
  await expect(page.getByRole("heading", { name: "Entrar no painel" })).not.toBeVisible();
  await expect(page.getByText("Agendeiiai")).toBeVisible();
});
