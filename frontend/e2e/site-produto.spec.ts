import { test, expect } from "@playwright/test";

/**
 * Site do produto no domínio raiz (seção 6.4, ajuste 4e), contra a pilha real do
 * `docker compose up`. Os preços vêm do banco (seed dos planos) — o teste confere que o
 * alternador troca os valores e que nada da marca do produto vaza para a página do negócio.
 */

const RAIZ = "http://agendeiiai.localhost:3000";

test("site mostra as seções, os planos do banco e o alternador mensal/anual", async ({ page }) => {
  await page.goto(RAIZ);

  await expect(page.getByRole("heading", { level: 1 })).toContainText("Sua agenda cheia");
  await expect(page.getByText("Sem cartão de crédito.").first()).toBeVisible();
  // Cada link do cabeçalho aponta para uma seção que existe na página.
  for (const id of ["como-funciona", "recursos", "planos", "duvidas"]) {
    await expect(page.locator(`header a[href="#${id}"]`)).toBeAttached();
    await expect(page.locator(`section#${id}`)).toBeAttached();
  }

  const planos = page.locator("#planos");
  const cards = planos.getByTestId("card-plano");
  await expect(cards).toHaveCount(3);
  await expect(cards.filter({ hasText: "Mais escolhido" })).toHaveCount(1);
  await expect(cards.filter({ hasText: "Começo" })).toContainText("49,90");
  await expect(planos.getByText(/economize R\$\s132 por ano/)).toBeVisible();

  await planos.getByRole("radio", { name: "Anual" }).click();
  const comeco = cards.filter({ hasText: "Começo" });
  await expect(comeco).toContainText("38,90");
  await expect(comeco).toContainText("466,80 cobrados por ano");
  await expect(comeco.getByRole("link", { name: "Começar teste grátis" })).toHaveAttribute("href", /periodicidade=Anual/);

  await expect(planos.getByText(/Mais de 12 profissionais\?/)).toBeVisible();

  // Dúvidas abrem e fecham sem JavaScript próprio (<details>).
  await page.getByText("Meu cliente precisa baixar algum aplicativo?").click();
  await expect(page.getByText(/abre o link do seu negócio no navegador/)).toBeVisible();
});

test("SEO e Open Graph do produto só no domínio raiz, nunca na página do negócio", async ({ page, request }) => {
  await page.goto(RAIZ);
  await expect(page).toHaveTitle(/agendamento online para barbearias/);
  await expect(page.locator('meta[property="og:image"]')).toHaveAttribute("content", /\/site\/og\.png$/);
  await expect(page.locator('meta[name="description"]')).toHaveAttribute("content", /Teste grátis por 30 dias/);
  expect((await request.get(`${RAIZ}/site/og.png`)).ok()).toBeTruthy();

  await page.goto("http://acme.agendeiiai.localhost:3000/");
  await expect(page.locator('meta[property="og:image"]')).toHaveCount(0);
  await expect(page.getByTestId("card-plano")).toHaveCount(0);
});

test("Termos e Privacidade do produto aparecem marcados como rascunho", async ({ page }) => {
  await page.goto(RAIZ);
  await page.getByRole("contentinfo").getByRole("link", { name: "Termos de Uso" }).click();
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Termos de Uso");
  await expect(page.getByTestId("aviso-rascunho")).toContainText("RASCUNHO — revisar antes de divulgar");

  await page.goto(`${RAIZ}/privacidade`);
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Política de Privacidade");
  await expect(page.getByTestId("aviso-rascunho")).toBeVisible();

  // Na página de um negócio, a privacidade continua sendo a do negócio (seção 8.4).
  await page.goto("http://acme.agendeiiai.localhost:3000/privacidade");
  await expect(page.getByTestId("aviso-rascunho")).toHaveCount(0);
  await expect(page.getByText(/descreve como Acme Barbearia.* trata os dados/)).toBeVisible();
});
