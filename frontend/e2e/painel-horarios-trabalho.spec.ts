import { test, expect, type APIRequestContext } from "@playwright/test";

/**
 * Ajuste pós-Sprint 5 (seção 14 do prompt-do-projeto.md): "Replicar para..." no horário
 * de trabalho do profissional. Cobre exatamente o teste exigido pela especificação:
 * replicar de um dia para vários aplica os valores corretamente em todos os marcados, e
 * editar um deles depois não afeta os demais. Roda contra a pilha real do
 * `docker compose up`, como o outro spec deste diretório.
 */

const API_BASE = "http://localhost:5080";
const PAINEL_BASE = "http://app.agendeiiai.localhost:3000";
const ADMIN_EMAIL = "admin@acme.dev";
const ADMIN_SENHA = "Admin!123";

async function loginAdmin(request: APIRequestContext): Promise<string> {
  const resposta = await request.post(`${API_BASE}/painel/auth/login`, {
    data: { email: ADMIN_EMAIL, senha: ADMIN_SENHA },
  });
  expect(resposta.ok()).toBeTruthy();
  const corpo = await resposta.json();
  return corpo.accessToken as string;
}

test("replicar horário de um dia para vários dias, editar um depois não afeta os demais", async ({ page, request }) => {
  const sufixo = Date.now().toString().slice(-8);

  // --- Arranjo: profissional novo, sem horário nenhum, via API do painel ---
  const token = await loginAdmin(request);
  const respProfissional = await request.post(`${API_BASE}/painel/profissionais`, {
    headers: { Authorization: `Bearer ${token}`, "Content-Type": "application/json" },
    data: { nome: `Profissional Horarios ${sufixo}`, funcao: "Barbeiro" },
  });
  const profissionalId = await respProfissional.json();

  // --- Ato: login pela UI de verdade (o access token vive só em memória, seção
  // "Painel (frontend)" do CLAUDE.md — não dá pra injetar sessão sem passar pela tela).
  // Navega depois só por <Link> (client-side), nunca por page.goto: um reload completo
  // reinicia o React e depende do cookie de refresh sobreviver entre origens diferentes
  // (front em app.agendeiiai.localhost:3000, API em localhost:5080 no dev local), o que
  // esbarra num bug separado do refresh_token (SameSite=Strict entre origens distintas —
  // reportado à parte, fora do escopo deste ajuste). ---
  await page.goto(`${PAINEL_BASE}/painel/login`);
  await page.getByLabel("E-mail").fill(ADMIN_EMAIL);
  await page.getByLabel("Senha").fill(ADMIN_SENHA);
  await page.getByRole("button", { name: "Entrar" }).click();
  await expect(page).toHaveURL(/\/painel$/);

  await page.getByRole("link", { name: "Profissionais" }).click();
  await expect(page).toHaveURL(/\/painel\/profissionais$/);

  const linha = page.getByRole("row", { name: new RegExp(`Profissional Horarios ${sufixo}`) });
  await linha.getByRole("link", { name: "Horários e serviços" }).click();
  await expect(page).toHaveURL(new RegExp(`/painel/profissionais/${profissionalId}$`));

  const segunda = page.getByTestId("dia-horario-1");
  const terca = page.getByTestId("dia-horario-2");
  const quarta = page.getByTestId("dia-horario-3");

  await expect(segunda.getByText("Sem expediente nesse dia.")).toBeVisible();

  // Configura o horário de Segunda.
  await segunda.getByRole("button", { name: "Adicionar intervalo" }).click();
  await segunda.locator('input[type="time"]').nth(0).fill("09:00");
  await segunda.locator('input[type="time"]').nth(1).fill("18:00");

  // Replica Segunda para Terça e Quarta.
  page.once("dialog", (dialog) => dialog.accept());
  await segunda.getByRole("button", { name: "Replicar para..." }).click();
  await segunda.getByRole("checkbox", { name: "Terça" }).check();
  await segunda.getByRole("checkbox", { name: "Quarta" }).check();
  await segunda.getByRole("button", { name: "Aplicar" }).click();

  await expect(terca.locator('input[type="time"]').nth(0)).toHaveValue("09:00");
  await expect(terca.locator('input[type="time"]').nth(1)).toHaveValue("18:00");
  await expect(quarta.locator('input[type="time"]').nth(0)).toHaveValue("09:00");
  await expect(quarta.locator('input[type="time"]').nth(1)).toHaveValue("18:00");

  // Editar Terça depois de replicado não pode afetar Segunda nem Quarta — é cópia de
  // valores, não vínculo (critério de teste explícito da seção 14).
  await terca.locator('input[type="time"]').nth(0).fill("10:00");

  await expect(segunda.locator('input[type="time"]').nth(0)).toHaveValue("09:00");
  await expect(quarta.locator('input[type="time"]').nth(0)).toHaveValue("09:00");

  await page.getByRole("button", { name: "Salvar horário" }).click();
  await expect(page.getByText("Horário de trabalho salvo.")).toBeVisible();

  // Confere a persistência de verdade direto na API (não via page.reload(): um reload
  // completo reinicia o React e dependeria do cookie de refresh sobreviver entre origens
  // diferentes — bug à parte, fora do escopo deste ajuste, ver comentário acima).
  const respHorarios = await request.get(`${API_BASE}/painel/profissionais/${profissionalId}/horarios`, {
    headers: { Authorization: `Bearer ${token}` },
  });
  const horariosSalvos = await respHorarios.json();
  expect(horariosSalvos).toEqual(
    expect.arrayContaining([
      expect.objectContaining({ diaSemana: 1, inicio: "09:00:00", fim: "18:00:00" }),
      expect.objectContaining({ diaSemana: 2, inicio: "10:00:00", fim: "18:00:00" }),
      expect.objectContaining({ diaSemana: 3, inicio: "09:00:00", fim: "18:00:00" }),
    ]),
  );
  expect(horariosSalvos).toHaveLength(3);
});
