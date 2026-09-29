import { test, expect } from "@playwright/test";

/**
 * Atendimento sem agendamento (seção 7, item 9): no balcão, cadastro rápido do cliente sem telefone
 * e "Apenas encaixar" num horário livre. "Lançar e iniciar" começa agora e depende do expediente do
 * momento, então fica nos testes de integração do backend (com expediente de dia inteiro).
 */

const PAINEL_BASE = "http://app.agendeiiai.localhost:3000";
const API = "http://localhost:5080";

test("recepção encaixa um cliente sem telefone num horário livre", async ({ page, request }) => {
  test.setTimeout(90_000);
  const login = await request.post(`${API}/painel/auth/login`, { data: { email: "admin@acme.dev", senha: "Admin!123" } });
  const auth = { Authorization: `Bearer ${(await login.json()).accessToken}` };

  const profissional = (await (await request.get(`${API}/painel/profissionais`, { headers: auth })).json()).find(
    (p: { ativo: boolean }) => p.ativo,
  );
  const servico = (await (await request.get(`${API}/painel/profissionais/${profissional.id}/servicos`, { headers: auth })).json())[0];

  // Primeiro horário livre de um dia útil daqui a algumas semanas.
  let horario: string | undefined;
  let data = "";
  for (let dias = 25; !horario && dias < 70; dias++) {
    data = new Date(Date.now() + dias * 86400000).toISOString().slice(0, 10);
    const livres = await (
      await request.get(`${API}/painel/profissionais/${profissional.id}/horarios-livres?data=${data}&duracaoMinutos=${servico.duracaoMinutos}`, {
        headers: auth,
      })
    ).json();
    horario = livres[0];
  }
  expect(horario).toBeTruthy();
  const local = new Date(horario!);
  const valorHorario = new Date(local.getTime() - local.getTimezoneOffset() * 60000).toISOString().slice(0, 16);
  const nome = `Balcão ${Date.now().toString().slice(-6)}`;

  await page.goto(`${PAINEL_BASE}/painel/login`);
  await page.getByLabel("E-mail").fill("admin@acme.dev");
  await page.getByLabel("Senha").fill("Admin!123");
  await page.getByRole("button", { name: "Entrar" }).click();
  await expect(page).toHaveURL(/\/painel$/);

  await page.goto(`${PAINEL_BASE}/painel/agenda`);

  await page.getByRole("tab", { name: "Por profissional" }).click();
  await page.getByRole("button", { name: "Atendimento sem agendamento" }).click();
  const modal = page.getByRole("dialog");
  await modal.getByRole("button", { name: "+ Cadastrar cliente novo" }).click();
  await modal.getByLabel("Nome", { exact: true }).fill(nome);
  await modal.getByLabel("Profissional").selectOption(profissional.id);
  await modal.getByLabel(new RegExp(`^${servico.nome}`)).check();
  await modal.getByLabel(/Horário/).fill(valorHorario);
  await modal.getByRole("button", { name: "Apenas encaixar" }).click();
  await expect(modal).toBeHidden();

  await page.getByLabel("Profissional").selectOption(profissional.id);
  await page.getByLabel("Data").fill(data);
  await expect(page.getByText(nome)).toBeVisible();
});
