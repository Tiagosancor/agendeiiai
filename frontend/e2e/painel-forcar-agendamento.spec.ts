import { test, expect } from "@playwright/test";

/**
 * Forçar agendamento (seção 7, item 10): o Administrador digita um horário fora do expediente, o sistema
 * recusa, mostra as regras quebradas e aceita "Forçar mesmo assim" com motivo — a agenda marca "Forçado".
 */

const PAINEL_BASE = "http://app.agendeiiai.localhost:3000";
const API = "http://localhost:5080";

test("administrador força um agendamento fora do expediente e a agenda marca como forçado", async ({ page, request }) => {
  test.setTimeout(90_000);
  const login = await request.post(`${API}/painel/auth/login`, { data: { email: "admin@acme.dev", senha: "Admin!123" } });
  const auth = { Authorization: `Bearer ${(await login.json()).accessToken}` };

  const profissional = (await (await request.get(`${API}/painel/profissionais`, { headers: auth })).json()).find(
    (p: { ativo: boolean }) => p.ativo,
  );
  const servico = (await (await request.get(`${API}/painel/profissionais/${profissional.id}/servicos`, { headers: auth })).json())[0];
  const nomeCliente = `Forçado ${Date.now().toString().slice(-6)}`;
  const cliente = await request.post(`${API}/painel/clientes`, {
    headers: auth,
    data: { nome: nomeCliente, telefone: `71${Math.floor(900000000 + Math.random() * 99999999)}` },
  });
  expect(cliente.status()).toBe(201);
  const clienteId: string = await cliente.json();

  // Um dia útil bem à frente, às 23:15 — fora de qualquer expediente de exemplo.
  // Um dia útil pode ser folga desse profissional. Confere o expediente real antes
  // de testar especificamente a regra "fora do expediente", sem mudar a regra testada.
  let data = "";
  const primeiroDia = 60 + Math.floor(Math.random() * 30);
  for (let dias = primeiroDia; dias < primeiroDia + 30; dias++) {
    const dia = new Date(Date.now() + dias * 86400000);
    if (dia.getDay() === 0 || dia.getDay() === 6) continue;
    const candidata = dia.toISOString().slice(0, 10);
    const livres = await (await request.get(
      `${API}/painel/profissionais/${profissional.id}/horarios-livres?data=${candidata}&duracaoMinutos=${servico.duracaoMinutos}`,
      { headers: auth },
    )).json();
    if (livres.length) { data = candidata; break; }
  }
  expect(data).toBeTruthy();

  await page.goto(`${PAINEL_BASE}/painel/login`);
  await page.getByLabel("E-mail").fill("admin@acme.dev");
  await page.getByLabel("Senha").fill("Admin!123");
  await page.getByRole("button", { name: "Entrar" }).click();
  await expect(page).toHaveURL(/\/painel$/);

  await page.goto(`${PAINEL_BASE}/painel/agenda`);

  await page.getByRole("tab", { name: "Por profissional" }).click();
  await page.getByLabel("Profissional").selectOption(profissional.id);
  await page.getByLabel("Data").fill(data);
  await page.getByRole("button", { name: "Novo agendamento" }).click();

  const modal = page.getByRole("dialog");
  await modal.getByLabel(new RegExp(`^${servico.nome}`)).check();
  await modal.getByLabel("Cliente").selectOption(clienteId);
  await modal.getByLabel(/Outro horário/).fill("23:15");
  await modal.getByRole("button", { name: "Criar" }).click();

  const aviso = modal.getByRole("region", { name: "Forçar agendamento" });
  await expect(aviso.getByText(/Fora do expediente do profissional/)).toBeVisible();
  const botao = aviso.getByRole("button", { name: "Forçar mesmo assim" });
  await expect(botao).toBeDisabled();
  await aviso.getByLabel("Motivo (obrigatório)").fill("Cliente só pode depois do trabalho");
  await botao.click();
  await expect(modal).toBeHidden();

  const linha = page.locator("div").filter({ hasText: nomeCliente }).filter({ hasText: "Forçado" }).last();
  await expect(linha.getByText("Forçado", { exact: true })).toBeVisible();
  await expect(linha.getByText(/Cliente só pode depois do trabalho/)).toBeVisible();
});
