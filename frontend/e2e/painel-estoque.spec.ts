import { test, expect } from "@playwright/test";

/**
 * Estoque (seção 7, item 12 a/b): produto novo com estoque inicial, ajuste que o leva para "Estoque baixo"
 * (com o indicador na tela inicial) e a entrada que o tira da lista.
 */

const PAINEL_BASE = "http://app.agendeiiai.localhost:3000";

test("administrador cadastra produto, ajusta para o mínimo, vê o alerta e repõe", async ({ page }) => {
  test.setTimeout(90_000);
  const nome = `Pomada ${Date.now().toString().slice(-6)}`;

  await page.goto(`${PAINEL_BASE}/painel/login`);
  await page.getByLabel("E-mail").fill("admin@acme.dev");
  await page.getByLabel("Senha").fill("Admin!123");
  await page.getByRole("button", { name: "Entrar" }).click();
  await expect(page).toHaveURL(/\/painel$/);

  await page.goto(`${PAINEL_BASE}/painel/estoque`);
  await page.getByRole("button", { name: "Novo produto" }).click();
  let modal = page.getByRole("dialog", { name: "Novo produto" });
  await modal.getByLabel("Nome").fill(nome);
  await modal.getByLabel("Preço de custo (R$)").fill("12.5");
  await modal.getByLabel("Preço de venda (R$)").fill("35");
  await modal.getByLabel("Quantidade inicial").fill("6");
  await expect(modal.getByLabel("Alerta com (quantidade mínima)")).toHaveValue("3");
  await modal.getByRole("button", { name: "Criar" }).click();
  await expect(modal).toBeHidden();

  const linha = page.getByRole("row", { name: new RegExp(nome) });
  await expect(linha.getByText("Em estoque", { exact: true })).toBeVisible();

  // Balanço: sobraram 3 — entra em "Estoque baixo".
  await linha.getByRole("button", { name: "Ajuste" }).click();
  modal = page.getByRole("dialog", { name: `Ajuste — ${nome}` });
  await modal.getByLabel("Quantidade contada").fill("3");
  await modal.getByLabel("Motivo (obrigatório)").fill("Balanço do mês");
  await modal.getByRole("button", { name: "Ajustar" }).click();
  await expect(modal).toBeHidden();
  await expect(page.getByRole("region", { name: "Estoque baixo" })).toContainText(`${nome} (3, mínimo 3)`);

  await page.goto(`${PAINEL_BASE}/painel`);
  await expect(page.getByRole("status")).toContainText("com estoque baixo");

  // Reposição tira o produto da lista; o histórico guarda tudo.
  await page.goto(`${PAINEL_BASE}/painel/estoque`);
  await page.getByRole("row", { name: new RegExp(nome) }).getByRole("button", { name: "Entrada" }).click();
  modal = page.getByRole("dialog", { name: `Entrada — ${nome}` });
  await modal.getByLabel("Quantidade").fill("10");
  await modal.getByRole("button", { name: "Lançar entrada" }).click();
  await expect(modal).toBeHidden();
  await expect(page.getByRole("row", { name: new RegExp(nome) }).getByRole("cell", { name: "13 mínimo 3" })).toBeVisible();
  await expect(page.getByRole("region", { name: "Estoque baixo" }).getByText(nome)).toHaveCount(0);

  await page.getByRole("row", { name: new RegExp(nome) }).getByRole("button", { name: "Histórico" }).click();
  modal = page.getByRole("dialog", { name: `Histórico — ${nome}` });
  await expect(modal.getByText("Entrada +10")).toBeVisible();
  await expect(modal.getByText("Ajuste -3")).toBeVisible();
  await expect(modal.getByText("Balanço do mês")).toBeVisible();
});
