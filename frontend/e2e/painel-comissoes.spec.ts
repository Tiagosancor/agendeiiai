import { test, expect } from "@playwright/test";

/**
 * Comissões (seção 7, item 6): o Administrador define o percentual na ficha do profissional e
 * vê a equipe na tela de comissões. O cálculo em si (cupom, arredondamento, reabrir, fuso) é
 * coberto pelos testes de integração do backend.
 */

const PAINEL_BASE = "http://app.agendeiiai.localhost:3000";
const API = "http://localhost:5080";

test("administrador define a comissão na ficha e vê a equipe em Comissões", async ({ page, request }) => {
  const login = await request.post(`${API}/painel/auth/login`, { data: { email: "admin@acme.dev", senha: "Admin!123" } });
  const { accessToken } = await login.json();
  const profissionais = await (
    await request.get(`${API}/painel/profissionais`, { headers: { Authorization: `Bearer ${accessToken}` } })
  ).json();
  const profissional = profissionais.find((p: { ativo: boolean }) => p.ativo);
  expect(profissional).toBeTruthy();

  await page.goto(`${PAINEL_BASE}/painel/login`);
  await page.getByLabel("E-mail").fill("admin@acme.dev");
  await page.getByLabel("Senha").fill("Admin!123");
  await page.getByRole("button", { name: "Entrar" }).click();
  await expect(page).toHaveURL(/\/painel$/);

  // Ficha do profissional: percentual inválido é recusado, válido é salvo.
  await page.goto(`${PAINEL_BASE}/painel/profissionais/${profissional.id}`);
  const campo = page.getByLabel("Comissão (%)");
  await campo.fill("150");
  await page.getByRole("button", { name: "Salvar comissão" }).click();
  await expect(page.getByText("A comissão precisa estar entre 0 e 100%.")).toBeVisible();

  await campo.fill("12,5");
  await page.getByRole("button", { name: "Salvar comissão" }).click();
  await expect(page.getByText("Comissão salva.")).toBeVisible();

  await page.reload();
  await expect(page.getByLabel("Comissão (%)")).toHaveValue("12,5");

  // Tela de comissões: aba da equipe mostra o profissional com o percentual atual.
  await page.goto(`${PAINEL_BASE}/painel/comissoes`);
  await page.getByRole("button", { name: "Mês anterior" }).click();
  await page.getByRole("tab", { name: "Equipe" }).click();
  const linha = page.getByRole("button", { name: new RegExp(profissional.nome) });
  await expect(linha).toContainText("12,5%");

  await linha.click();
  await expect(page.getByText("Percentual atual:")).toContainText("12,5%");
  await expect(page.getByText("Comissão no período")).toBeVisible();
});
