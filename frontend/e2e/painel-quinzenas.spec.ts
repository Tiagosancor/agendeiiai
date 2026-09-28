import { test, expect } from "@playwright/test";

/**
 * Quinzenas de comissão (seção 7, item 7): o Administrador liga o acerto por quinzena na ficha,
 * cria uma quinzena, fecha, reabre com motivo e exclui. As regras finas (sobreposição, fuso,
 * travamento, pendentes) estão nos testes de integração do backend.
 *
 * A quinzena usa um mês bem no passado, escolhido pelo relógio, para não esbarrar em quinzenas
 * de execuções anteriores (o banco do docker compose persiste entre execuções).
 */

const PAINEL_BASE = "http://app.agendeiiai.localhost:3000";
const API = "http://localhost:5080";

test("administrador cria, fecha, reabre e exclui uma quinzena", async ({ page, request }) => {
  const login = await request.post(`${API}/painel/auth/login`, { data: { email: "admin@acme.dev", senha: "Admin!123" } });
  const { accessToken } = await login.json();
  const profissionais = await (
    await request.get(`${API}/painel/profissionais`, { headers: { Authorization: `Bearer ${accessToken}` } })
  ).json();
  const profissional = profissionais.find((p: { ativo: boolean }) => p.ativo);

  const minuto = Math.floor(Date.now() / 60000);
  const ano = 1990 + (minuto % 30);
  const mes = String((Math.floor(minuto / 30) % 12) + 1).padStart(2, "0");
  const inicio = `${ano}-${mes}-01`;
  const fim = `${ano}-${mes}-15`;

  await page.goto(`${PAINEL_BASE}/painel/login`);
  await page.getByLabel("E-mail").fill("admin@acme.dev");
  await page.getByLabel("Senha").fill("Admin!123");
  await page.getByRole("button", { name: "Entrar" }).click();
  await expect(page).toHaveURL(/\/painel$/);

  // Ficha: liga o acerto por quinzena.
  await page.goto(`${PAINEL_BASE}/painel/profissionais/${profissional.id}`);
  await page.getByLabel("Acerto por quinzena").check();
  await page.getByRole("button", { name: "Salvar comissão" }).click();
  await expect(page.getByText("Comissão salva.")).toBeVisible();

  // Cria a quinzena.
  await page.goto(`${PAINEL_BASE}/painel/comissoes`);
  await page.getByRole("tab", { name: "Quinzenas" }).click();
  await expect(page.getByLabel("Início")).not.toHaveValue(""); // espera a sugestão chegar antes de digitar
  await page.getByLabel("Início").fill(inicio);
  await page.getByLabel("Fim").fill(fim);
  await page.getByRole("button", { name: "Criar quinzena" }).click();
  await expect(page.getByText("Quinzena criada.")).toBeVisible();

  const rotulo = `01/${mes}/${ano} a 15/${mes}/${ano}`;
  await page.getByRole("button", { name: new RegExp(rotulo) }).click();
  await expect(page.getByText("Valores parciais")).toBeVisible();
  await expect(page.getByRole("cell", { name: profissional.nome })).toBeVisible();

  // Fecha, reabre com motivo e exclui.
  await page.getByRole("button", { name: "Fechar quinzena" }).click();
  await expect(page.getByText("Estes valores não mudam mais")).toBeVisible();

  await page.getByRole("button", { name: "Reabrir quinzena" }).click();
  await page.getByLabel("Motivo da reabertura (obrigatório)").fill("Teste automatizado");
  page.once("dialog", (dialogo) => dialogo.accept());
  await page.getByRole("button", { name: "Reabrir", exact: true }).click();
  await expect(page.getByText("Valores parciais")).toBeVisible();

  page.once("dialog", (dialogo) => dialogo.accept());
  await page.getByRole("button", { name: "Excluir" }).click();
  await expect(page.getByRole("button", { name: new RegExp(rotulo) })).toHaveCount(0);
});
