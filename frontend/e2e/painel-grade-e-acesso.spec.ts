import { test, expect, type Page } from "@playwright/test";

/**
 * Item 11 da seção 14: a agenda abre na grade do dia (profissionais em coluna, horários em linha) e clicar
 * num horário livre abre a criação já com o profissional e o horário; e os caminhos de acesso do
 * profissional — "Novo usuário, perfil Profissional" e "Dar acesso ao sistema" na ficha.
 */

const PAINEL_BASE = "http://app.agendeiiai.localhost:3000";
const API = "http://localhost:5080";

async function entrar(page: Page) {
  await page.goto(`${PAINEL_BASE}/painel/login`);
  await page.getByLabel("E-mail").fill("admin@acme.dev");
  await page.getByLabel("Senha").fill("Admin!123");
  await page.getByRole("button", { name: "Entrar" }).click();
  await expect(page).toHaveURL(/\/painel$/);
}

test("clicar num horário livre da grade abre a criação com profissional e horário preenchidos", async ({ page, request }) => {
  test.setTimeout(90_000);
  const login = await request.post(`${API}/painel/auth/login`, { data: { email: "admin@acme.dev", senha: "Admin!123" } });
  const auth = { Authorization: `Bearer ${(await login.json()).accessToken}` };

  const profissional = (await (await request.get(`${API}/painel/profissionais`, { headers: auth })).json()).find(
    (p: { ativo: boolean }) => p.ativo,
  );
  const servico = (await (await request.get(`${API}/painel/profissionais/${profissional.id}/servicos`, { headers: auth })).json())[0];
  const nomeCliente = `Grade ${Date.now().toString().slice(-6)}`;
  const clienteId: string = await (
    await request.post(`${API}/painel/clientes`, {
      headers: auth,
      data: { nome: nomeCliente, telefone: `71${Math.floor(900000000 + Math.random() * 99999999)}` },
    })
  ).json();

  // Primeiro horário livre de um dia bem à frente.
  let horario: string | undefined;
  let data = "";
  const primeiroDia = 90 + Math.floor(Math.random() * 60);
  for (let dias = primeiroDia; !horario && dias < primeiroDia + 30; dias++) {
    data = new Date(Date.now() + dias * 86400000).toISOString().slice(0, 10);
    horario = (
      await (
        await request.get(`${API}/painel/profissionais/${profissional.id}/horarios-livres?data=${data}&duracaoMinutos=${servico.duracaoMinutos}`, {
          headers: auth,
        })
      ).json()
    )[0];
  }
  expect(horario).toBeTruthy();
  const hora = new Date(horario!).toLocaleTimeString("pt-BR", { hour: "2-digit", minute: "2-digit" });

  await entrar(page);
  await page.goto(`${PAINEL_BASE}/painel/agenda`);
  await expect(page.getByRole("tab", { name: "Dia (todos)" })).toHaveAttribute("aria-selected", "true");
  await page.getByLabel("Data").fill(data);

  const grade = page.getByRole("table", { name: "Agenda do dia" });
  await expect(grade.getByRole("columnheader", { name: profissional.nome })).toBeVisible();
  await grade.getByRole("button", { name: `Livre: ${profissional.nome} às ${hora}` }).click();

  const modal = page.getByRole("dialog");
  await expect(modal.getByText(`Com ${profissional.nome}`)).toBeVisible();
  await expect(modal.getByLabel(/^Horário/)).toHaveValue(horario!);
  await modal.getByLabel(new RegExp(`^${servico.nome}`)).check();
  await modal.getByLabel("Cliente").selectOption(clienteId);
  await modal.getByRole("button", { name: "Criar" }).click();
  await expect(modal).toBeHidden();

  await expect(grade.getByText(nomeCliente)).toBeVisible();
  await expect(grade.getByRole("button", { name: `Livre: ${profissional.nome} às ${hora}` })).toHaveCount(0);
});

test("usuário com perfil Profissional segue para o cadastro de profissional e a ficha dá acesso a quem não tem", async ({ page, request }) => {
  test.setTimeout(90_000);
  const sufixo = Date.now().toString().slice(-6);
  const nome = `Zz Barbeiro ${sufixo}`;
  const email = `barbeiro-${sufixo}@teste.dev`;

  await entrar(page);
  await page.goto(`${PAINEL_BASE}/painel/usuarios`);
  await page.getByRole("button", { name: "Novo usuário" }).click();
  let modal = page.getByRole("dialog");
  await modal.getByLabel("Nome").fill(nome);
  await modal.getByLabel("E-mail").fill(email);
  await modal.getByLabel("Senha", { exact: true }).fill("SenhaDoBarbeiro1");
  await modal.getByLabel("Perfil").selectOption("Profissional");
  await modal.getByRole("button", { name: "Criar" }).click();

  // Abre direto o cadastro de profissional, pré-preenchido, e vincula ao salvar.
  modal = page.getByRole("dialog", { name: "Novo profissional" });
  await expect(modal.getByLabel("Nome")).toHaveValue(nome);
  await expect(modal.getByText(`Ele vai entrar no sistema com ${email}`)).toBeVisible();
  await modal.getByRole("button", { name: "Criar" }).click();

  await expect(page).toHaveURL(/\/painel\/profissionais\/[0-9a-f-]+$/);
  const criadoDoUsuario = page.url().split("/").pop();
  await expect(page.getByRole("heading", { name: nome })).toBeVisible();
  await expect(page.getByText(`Entra com ${email}`)).toBeVisible();

  // Profissional que só existia na agenda: "Dar acesso ao sistema" com convite por e-mail.
  const login = await request.post(`${API}/painel/auth/login`, { data: { email: "admin@acme.dev", senha: "Admin!123" } });
  const auth = { Authorization: `Bearer ${(await login.json()).accessToken}` };
  const semAcesso: string = await (await request.post(`${API}/painel/profissionais`, { headers: auth, data: { nome: `Zz Sem acesso ${sufixo}` } })).json();
  const emailConvite = `convite-${sufixo}@teste.dev`;

  await page.goto(`${PAINEL_BASE}/painel/profissionais/${semAcesso}`);
  await expect(page.getByText("Sem acesso: aparece na agenda, mas não entra no sistema.")).toBeVisible();
  await page.getByRole("button", { name: "Dar acesso ao sistema" }).click();
  await page.getByLabel("E-mail de acesso").fill(emailConvite);
  await page.getByRole("button", { name: "Criar acesso" }).click();
  await expect(page.getByText(`Convite enviado para ${emailConvite}.`)).toBeVisible();
  await expect(page.getByText(`Entra com ${emailConvite}`)).toBeVisible();

  // Sai da agenda dos próximos testes: sem horário, eles não serviriam de "profissional ativo" para ninguém.
  for (const id of [criadoDoUsuario, semAcesso])
    expect((await request.post(`${API}/painel/profissionais/${id}/desativar`, { headers: auth })).status()).toBe(204);
});
