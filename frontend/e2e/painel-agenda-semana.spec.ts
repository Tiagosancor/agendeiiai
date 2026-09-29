import { test, expect, type APIRequestContext } from "@playwright/test";

/**
 * Agenda (seção 7): visão "Semana" de um profissional, "Remarcar" pelo painel e a agenda do Profissional logado (sem
 * "gerenciar agenda": só a própria, dia e semana).
 */

const PAINEL_BASE = "http://app.agendeiiai.localhost:3000";
const API = "http://localhost:5080";

async function autenticar(request: APIRequestContext) {
  const login = await request.post(`${API}/painel/auth/login`, { data: { email: "admin@acme.dev", senha: "Admin!123" } });
  return { Authorization: `Bearer ${(await login.json()).accessToken}` };
}

test("semana do profissional e remarcar um atendimento para outro horário livre", async ({ page, request }) => {
  test.setTimeout(120_000);
  const auth = await autenticar(request);
  const sufixo = Date.now().toString().slice(-6);

  const profissional = (await (await request.get(`${API}/painel/profissionais`, { headers: auth })).json()).find(
    (p: { ativo: boolean }) => p.ativo,
  );
  const servico = (await (await request.get(`${API}/painel/profissionais/${profissional.id}/servicos`, { headers: auth })).json())[0];

  // Um dia com pelo menos dois horários livres, daqui a algumas semanas.
  let livres: string[] = [];
  let data = "";
  for (let dias = 35; livres.length < 2 && dias < 90; dias++) {
    data = new Date(Date.now() + dias * 86400000).toISOString().slice(0, 10);
    livres = await (
      await request.get(`${API}/painel/profissionais/${profissional.id}/horarios-livres?data=${data}&duracaoMinutos=${servico.duracaoMinutos}`, {
        headers: auth,
      })
    ).json();
  }
  expect(livres.length).toBeGreaterThanOrEqual(2);
  const cliente = `Cliente semana ${sufixo}`;
  const encaixe = await request.post(`${API}/painel/encaixes`, {
    headers: auth,
    data: {
      profissionalId: profissional.id,
      clienteId: null,
      novoCliente: { nome: cliente, telefone: null },
      servicoIds: [servico.servicoId],
      inicio: livres[0],
      iniciarAtendimento: false,
      clienteAutorizouMensagens: false,
    },
  });
  expect(encaixe.status(), await encaixe.text()).toBe(201);
  const { agendamentoId } = await encaixe.json();

  await page.goto(`${PAINEL_BASE}/painel/login`);
  await page.getByLabel("E-mail").fill("admin@acme.dev");
  await page.getByLabel("Senha").fill("Admin!123");
  await page.getByRole("button", { name: "Entrar" }).click();
  await expect(page).toHaveURL(/\/painel$/);

  // Semana: o atendimento aparece no dia dele; clicar no dia abre a lista daquele dia.
  await page.goto(`${PAINEL_BASE}/painel/agenda`);
  await page.getByRole("tab", { name: "Semana" }).click();
  await page.getByLabel("Profissional").selectOption(profissional.id);
  await page.getByLabel("Data").fill(data);
  const semana = page.getByRole("list", { name: "Semana" });
  await expect(semana.locator(":scope > section")).toHaveCount(7);
  const [, mes, dia] = data.split("-");
  const colunaDoDia = semana.getByRole("listitem", { name: new RegExp(`${dia}/${mes}$`) });
  await expect(colunaDoDia).toContainText(cliente);
  await colunaDoDia.getByRole("button", { name: new RegExp(`${dia}/${mes}`) }).click();
  await expect(page.getByRole("tab", { name: "Por profissional" })).toHaveAttribute("aria-selected", "true");

  // Remarcar para o último horário livre que a tela oferece no mesmo dia (o próprio horário ocupa os vizinhos).
  const linha = page.locator("div.flex.flex-wrap.items-center.justify-between").filter({ hasText: cliente });
  await linha.getByRole("button", { name: "Remarcar" }).click();
  const modal = page.getByRole("dialog", { name: "Remarcar" });
  const opcoes = modal.getByLabel("Novo horário").locator("option");
  await expect(opcoes.nth(1)).toBeAttached();
  const novoInicio = (await opcoes.last().getAttribute("value"))!;
  await modal.getByLabel("Novo horário").selectOption(novoInicio);
  await modal.getByRole("button", { name: "Remarcar" }).click();
  await expect(modal).toBeHidden();

  const lista = await (await request.get(`${API}/painel/agenda?profissionalId=${profissional.id}&data=${data}`, { headers: auth })).json();
  const remarcado = lista.find((a: { id: string }) => a.id === agendamentoId);
  expect(new Date(remarcado.inicio).getTime()).toBe(new Date(novoInicio).getTime());
  expect(new Date(remarcado.inicio).getTime()).not.toBe(new Date(livres[0]).getTime());

  await request.post(`${API}/painel/agendamentos/${agendamentoId}/cancelar`, { headers: auth });
});

test("profissional logado sem gerenciar agenda vê só a própria agenda, dia e semana", async ({ page, request }) => {
  test.setTimeout(90_000);
  const auth = await autenticar(request);
  const sufixo = Date.now().toString().slice(-6);
  const nome = `Zz Minha agenda ${sufixo}`;
  const email = `zz-agenda-${sufixo}@teste.dev`;
  const senha = `Senha!${sufixo}Aa`;

  const profissionalId: string = await (await request.post(`${API}/painel/profissionais`, { headers: auth, data: { nome } })).json();
  const acesso = await request.post(`${API}/painel/profissionais/${profissionalId}/acesso`, {
    headers: auth,
    data: { email, senha, enviarConvite: false },
  });
  expect(acesso.ok(), await acesso.text()).toBeTruthy();

  await page.goto(`${PAINEL_BASE}/painel/login`);
  await page.getByLabel("E-mail").fill(email);
  await page.getByLabel("Senha").fill(senha);
  await page.getByRole("button", { name: "Entrar" }).click();
  await expect(page).toHaveURL(/\/painel$/);

  await page.goto(`${PAINEL_BASE}/painel/agenda`);
  await expect(page.getByRole("tab", { name: "Dia", exact: true })).toBeVisible();
  await expect(page.getByRole("tab", { name: "Dia (todos)" })).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Novo agendamento" })).toHaveCount(0);
  await expect(page.getByText("Nada na sua agenda neste dia.")).toBeVisible();

  await page.getByRole("tab", { name: "Semana" }).click();
  await expect(page.getByRole("list", { name: "Semana" }).locator(":scope > section")).toHaveCount(7);
  await expect(page.getByText(nome)).toBeVisible();

  // Limpa: excluir o profissional (sem histórico) desativa o usuário vinculado de perfil Profissional.
  await request.delete(`${API}/painel/profissionais/${profissionalId}`, { headers: auth });
});
