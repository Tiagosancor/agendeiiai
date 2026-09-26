import { test, expect, type APIRequestContext, type Page } from "@playwright/test";

/**
 * Editar e excluir cadastros pelo painel (seção 7 — ajuste 5), contra a pilha real do
 * `docker compose up`. O arranjo (serviço, profissional, agendamento futuro) é feito pela API
 * do painel; as ações que importam — editar e excluir — pelas telas de verdade.
 */

const API = "http://localhost:5080";
const PAINEL = "http://app.agendeiiai.localhost:3000";

async function tokenAdmin(request: APIRequestContext): Promise<string> {
  const resposta = await request.post(`${API}/painel/auth/login`, { data: { email: "admin@acme.dev", senha: "Admin!123" } });
  expect(resposta.ok()).toBeTruthy();
  return (await resposta.json()).accessToken;
}

async function entrar(page: Page) {
  await page.goto(`${PAINEL}/painel/login`);
  await page.getByLabel("E-mail").fill("admin@acme.dev");
  await page.getByLabel("Senha").fill("Admin!123");
  await page.getByRole("button", { name: "Entrar" }).click();
  await expect(page).toHaveURL(/\/painel$/);
}

test("editar o preço de um serviço e excluir um serviço que nunca foi usado", async ({ page, request }) => {
  const sufixo = Date.now().toString().slice(-8);
  const cabecalhos = { Authorization: `Bearer ${await tokenAdmin(request)}` };
  const categoriaId = await (await request.post(`${API}/painel/categorias`, { headers: cabecalhos, data: { nome: `Cat E2E ${sufixo}` } })).json();
  await request.post(`${API}/painel/servicos`, {
    headers: cabecalhos,
    data: { categoriaId, nome: `Serviço E2E ${sufixo}`, preco: 40, duracaoMinutos: 30, popular: false },
  });

  await entrar(page);
  await page.getByRole("link", { name: "Serviços" }).click();
  const linha = page.getByRole("row", { name: new RegExp(`Serviço E2E ${sufixo}`) });
  await expect(linha).toContainText("40,00");

  await linha.getByRole("button", { name: "Editar" }).click();
  const edicao = page.getByRole("dialog", { name: new RegExp(`Editar Serviço E2E ${sufixo}`) });
  await expect(edicao).toContainText("valem só para agendamentos novos");
  await edicao.getByLabel("Preço (R$)").fill("55,50");
  await edicao.getByRole("button", { name: "Salvar" }).click();
  await expect(linha).toContainText("55,50");

  await linha.getByRole("button", { name: "Excluir" }).click();
  const exclusao = page.getByTestId("modal-exclusao");
  await expect(exclusao).toContainText("Nunca foi agendado");
  await expect(exclusao.getByRole("button", { name: "Excluir" })).toBeDisabled();
  await exclusao.getByRole("checkbox").check();
  await exclusao.getByRole("button", { name: "Excluir" }).click();
  await expect(page.getByRole("row", { name: new RegExp(`Serviço E2E ${sufixo}`) })).toHaveCount(0);
});

test("profissional com agendamento futuro só é excluído depois de resolver o agendamento", async ({ page, request }) => {
  const sufixo = Date.now().toString().slice(-8);
  const cabecalhos = { Authorization: `Bearer ${await tokenAdmin(request)}` };

  const profissionalId = await (
    await request.post(`${API}/painel/profissionais`, { headers: cabecalhos, data: { nome: `Prof E2E ${sufixo}` } })
  ).json();
  await request.put(`${API}/painel/profissionais/${profissionalId}/horarios`, {
    headers: cabecalhos,
    data: [0, 1, 2, 3, 4, 5, 6].map((dia) => ({ diaSemana: dia, inicio: "08:00:00", fim: "20:00:00" })),
  });
  const categoriaId = await (await request.post(`${API}/painel/categorias`, { headers: cabecalhos, data: { nome: `Cat P ${sufixo}` } })).json();
  const servicoId = await (
    await request.post(`${API}/painel/servicos`, {
      headers: cabecalhos,
      data: { categoriaId, nome: `Serviço P ${sufixo}`, preco: 30, duracaoMinutos: 30, popular: false },
    })
  ).json();

  const clienteId = await (
    await request.post(`${API}/painel/clientes`, { headers: cabecalhos, data: { nome: `Cliente E2E ${sufixo}`, telefone: `+557198${sufixo.slice(-7)}` } })
  ).json();

  const amanha = new Date(Date.now() + 24 * 60 * 60 * 1000).toLocaleDateString("en-CA", { timeZone: "America/Sao_Paulo" });
  const agendado = await request.post(`${API}/painel/agendamentos`, {
    headers: cabecalhos,
    data: { profissionalId, clienteId, servicoIds: [servicoId], inicio: `${amanha}T10:00:00-03:00` },
  });
  expect(agendado.status()).toBe(201);

  await entrar(page);
  await page.getByRole("link", { name: "Profissionais" }).click();
  const linha = page.getByRole("row", { name: new RegExp(`Prof E2E ${sufixo}`) });
  await linha.getByRole("button", { name: "Excluir" }).click();

  const exclusao = page.getByTestId("modal-exclusao");
  await expect(exclusao.getByRole("alert")).toContainText("1 agendamento futuro");
  await expect(exclusao.getByRole("button", { name: "Excluir" })).toBeDisabled();

  page.once("dialog", (dialogo) => dialogo.accept());
  await exclusao.getByRole("listitem").getByRole("button", { name: "Cancelar" }).click();

  // Resolvido o agendamento, a exclusão libera — com histórico, fica só o nome.
  await expect(exclusao.getByRole("alert")).toHaveCount(0);
  await expect(exclusao).toContainText("continuam mostrando o nome");
  await exclusao.getByRole("checkbox").check();
  await exclusao.getByRole("button", { name: "Excluir" }).click();
  await expect(page.getByRole("row", { name: new RegExp(`Prof E2E ${sufixo}`) })).toHaveCount(0);

  // Não volta a aparecer nem pela API.
  expect((await request.get(`${API}/painel/profissionais/${profissionalId}`, { headers: cabecalhos })).status()).toBe(404);
  await request.delete(`${API}/painel/servicos/${servicoId}`, { headers: cabecalhos });
});
