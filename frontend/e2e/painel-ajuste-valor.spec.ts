import { test, expect, type APIRequestContext } from "@playwright/test";

/**
 * Iniciar atendimento e ajuste de valor (seção 7, item 8), contra a pilha real. O Administrador
 * inicia o atendimento, dá um desconto e vê o total mudar; uma Recepcionista (sem a permissão, que
 * vem desligada) não vê o botão. As regras finas estão nos testes de integração do backend.
 */

const PAINEL_BASE = "http://app.agendeiiai.localhost:3000";
const API = "http://localhost:5080";

async function tokenAdmin(request: APIRequestContext): Promise<string> {
  const login = await request.post(`${API}/painel/auth/login`, { data: { email: "admin@acme.dev", senha: "Admin!123" } });
  return (await login.json()).accessToken;
}

async function entrar(page: import("@playwright/test").Page, email: string, senha: string) {
  await page.goto(`${PAINEL_BASE}/painel/login`);
  await page.getByLabel("E-mail").fill(email);
  await page.getByLabel("Senha").fill(senha);
  await page.getByRole("button", { name: "Entrar" }).click();
  await expect(page).toHaveURL(/\/painel$/);
}

test("administrador inicia o atendimento e ajusta o valor; recepcionista não vê o ajuste", async ({ page, browser, request }) => {
  test.setTimeout(90_000);
  const token = await tokenAdmin(request);
  const auth = { Authorization: `Bearer ${token}` };

  // Um profissional ativo, num dia útil daqui a algumas semanas.
  const profissional = (await (await request.get(`${API}/painel/profissionais`, { headers: auth })).json()).find(
    (p: { ativo: boolean }) => p.ativo,
  );
  const servicos = await (await request.get(`${API}/painel/profissionais/${profissional.id}/servicos`, { headers: auth })).json();
  const servico = servicos[0];
  const sufixo = Date.now().toString().slice(-8);
  const cliente = await request.post(`${API}/painel/clientes`, {
    headers: auth,
    data: { nome: `Cliente Ajuste ${sufixo}`, telefone: `719${sufixo}` },
  });
  const clienteId = await cliente.json();

  let horario: string | undefined;
  let data = "";
  for (let dias = 20; !horario && dias < 60; dias++) {
    const dia = new Date(Date.now() + dias * 86400000);
    data = dia.toISOString().slice(0, 10);
    const livres = await (
      await request.get(`${API}/painel/profissionais/${profissional.id}/horarios-livres?data=${data}&duracaoMinutos=${servico.duracaoMinutos}`, {
        headers: auth,
      })
    ).json();
    horario = livres[0];
  }
  expect(horario).toBeTruthy();
  const criado = await request.post(`${API}/painel/agendamentos`, {
    headers: auth,
    data: { profissionalId: profissional.id, clienteId, servicoIds: [servico.servicoId], inicio: horario },
  });
  expect(criado.ok()).toBeTruthy();

  await entrar(page, "admin@acme.dev", "Admin!123");
  await page.goto(`${PAINEL_BASE}/painel/agenda`);
  await page.getByLabel("Profissional").selectOption(profissional.id);
  await page.getByLabel("Data").fill(data);
  const linha = page.locator("div.flex-wrap", { hasText: `Cliente Ajuste ${sufixo}` }).first();
  await linha.getByRole("button", { name: "Iniciar atendimento" }).click();
  await expect(linha.getByText("Em atendimento")).toBeVisible();

  await linha.getByRole("button", { name: "Ajustar valor" }).click();
  const modal = page.getByRole("dialog");
  await modal.getByLabel("Valor (R$)").fill("5");
  await expect(modal.getByText("Valor final:")).toBeVisible();
  await modal.getByLabel("Motivo (obrigatório)").fill("Cliente fiel");
  await modal.getByRole("button", { name: "Confirmar ajuste" }).click();
  await expect(modal.getByText("“Cliente fiel”")).toBeVisible();

  // Recepcionista: vê a agenda, mas sem o botão de ajuste (permissão desligada por padrão).
  const emailRecepcao = `recepcao-ajuste-${sufixo}@teste.dev`;
  const usuario = await request.post(`${API}/painel/usuarios`, {
    headers: auth,
    data: { nome: "Recepção Ajuste", email: emailRecepcao, senha: "SenhaRecepcao1", perfil: "Recepcionista" },
  });
  expect(usuario.ok()).toBeTruthy();

  const contexto = await browser.newContext();
  const outra = await contexto.newPage();
  await entrar(outra, emailRecepcao, "SenhaRecepcao1");
  await outra.goto(`${PAINEL_BASE}/painel/agenda`);
  await outra.getByLabel("Profissional").selectOption(profissional.id);
  await outra.getByLabel("Data").fill(data);
  const linhaRecepcao = outra.locator("div.flex-wrap", { hasText: `Cliente Ajuste ${sufixo}` }).first();
  await expect(linhaRecepcao.getByText("Em atendimento")).toBeVisible();
  await expect(linhaRecepcao.getByRole("button", { name: "Ajustar valor" })).toHaveCount(0);
  await contexto.close();
});
