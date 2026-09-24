import { test, expect, type APIRequestContext } from "@playwright/test";
import { execSync } from "node:child_process";

/**
 * Fluxo completo do assistente público (seção 6.2/8.1 — Sprint 3, critério de aceite
 * "fluxo completo coberto por teste ponta a ponta"). Roda contra a pilha real do
 * `docker compose up` (API em :5080, painel/site em :3000) — precisa estar de pé antes
 * (ver README do comando ou CLAUDE.md).
 *
 * O código de confirmação nunca aparece na tela nem em nenhuma API — só é logado pelo
 * provedor `Fake` (seção 8.1.6). Em vez de um backdoor de teste na aplicação (que
 * pesaria contra a segurança do fluxo real), este teste lê o código direto do log do
 * container via `docker compose logs`, exatamente como um humano faria localmente.
 */

const API_BASE = "http://localhost:5080";
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

// O plano do negócio de dev limita os profissionais ativos (seção 7): cada execução
// desativa o profissional que criou, senão as execuções acumulam até estourar o limite.
let profissionalCriado: string | null = null;

test.afterEach(async ({ request }) => {
  if (!profissionalCriado) return;
  const token = await loginAdmin(request);
  await request.post(`${API_BASE}/painel/profissionais/${profissionalCriado}/desativar`, {
    headers: { Authorization: `Bearer ${token}` },
  });
  profissionalCriado = null;
});

function extrairCodigoDoLog(telefone: string): string {
  // Só funciona rodando local/CI com acesso ao daemon Docker do docker-compose deste
  // repositório — é exatamente o público-alvo deste teste (validação manual/CI local).
  const log = execSync("docker compose logs api --tail 200", {
    cwd: process.cwd().endsWith("frontend") ? ".." : ".",
    encoding: "utf-8",
  });

  const linhas = log.split("\n").filter((linha) => linha.includes(telefone) && linha.includes("MensageriaWhatsAppFake"));
  const ultimaLinha = linhas.at(-1);
  const match = ultimaLinha?.match(/: (\d{6})\. Válido/);

  if (!match) throw new Error(`Código não encontrado no log para o telefone ${telefone}. Linhas: ${linhas.join("\n")}`);
  return match[1];
}

test("cliente agenda um serviço do início ao fim pelo assistente público", async ({ page, request }) => {
  const sufixo = Date.now().toString().slice(-8);
  const nomeServico = `Corte Playwright ${sufixo}`;
  const telefone = `+557199${sufixo}`;

  // --- Arranjo: profissional com expediente, serviço e vínculo, via API do painel ---
  const token = await loginAdmin(request);
  const cabecalhos = { Authorization: `Bearer ${token}`, "Content-Type": "application/json" };

  const respProfissional = await request.post(`${API_BASE}/painel/profissionais`, {
    headers: cabecalhos,
    data: { nome: `Profissional Playwright ${sufixo}`, funcao: "Barbeiro" },
  });
  expect(respProfissional.ok()).toBeTruthy();
  const profissionalId = await respProfissional.json();
  profissionalCriado = profissionalId;

  await request.put(`${API_BASE}/painel/profissionais/${profissionalId}/horarios`, {
    headers: cabecalhos,
    data: [1, 2, 3, 4, 5, 6, 0].map((dia) => ({ diaSemana: dia, inicio: "08:00:00", fim: "20:00:00" })),
  });

  const respCategoria = await request.post(`${API_BASE}/painel/categorias`, {
    headers: cabecalhos,
    data: { nome: `Categoria Playwright ${sufixo}` },
  });
  const categoriaId = await respCategoria.json();

  const respServico = await request.post(`${API_BASE}/painel/servicos`, {
    headers: cabecalhos,
    data: { categoriaId, nome: nomeServico, preco: 45, duracaoMinutos: 30, popular: true },
  });
  const servicoId = await respServico.json();

  await request.post(`${API_BASE}/painel/profissionais/${profissionalId}/servicos`, {
    headers: cabecalhos,
    data: { servicoId },
  });

  // --- Ato: o fluxo público de verdade, pelo navegador ---
  await page.goto("/");
  await expect(page.getByRole("heading", { name: /Acme Barbearia/ })).toBeVisible();

  await page.getByRole("button", { name: "Agendar", exact: true }).click();

  const assistente = page.getByTestId("assistente-agendamento");
  await expect(assistente).toBeVisible();

  // Etapa 1 — Serviços
  await assistente.getByRole("button", { name: new RegExp(nomeServico) }).click();
  await assistente.getByRole("button", { name: "Continuar" }).click();

  // Etapa 2 — Data e horário. Escolhe amanhã explicitamente (não confia no padrão
  // "hoje", que pode não ter mais horário livre dependendo da hora em que o teste roda).
  await assistente.locator(".overflow-x-auto button").nth(1).click();
  const primeiroHorario = assistente.locator(".grid.grid-cols-3 button").first();
  await expect(primeiroHorario).toBeVisible({ timeout: 10_000 });
  await primeiroHorario.click();
  await assistente.getByRole("button", { name: "Continuar" }).click();

  // Etapa 3 — Seus dados + código de confirmação (seção 8.1)
  await assistente.getByPlaceholder("Nome completo").fill("Cliente Playwright");
  await assistente.getByPlaceholder("Telefone (+55...)").fill(telefone);
  await assistente.getByPlaceholder("E-mail").fill(`playwright-${sufixo}@teste.com`);
  await assistente.getByRole("checkbox").check();
  await assistente.getByRole("button", { name: "Enviar código" }).click();

  await expect(assistente.getByPlaceholder("000000")).toBeVisible({ timeout: 10_000 });
  const codigo = extrairCodigoDoLog(telefone);
  await assistente.getByPlaceholder("000000").fill(codigo);
  await assistente.getByRole("button", { name: "Confirmar código" }).click();

  // Etapa 4 — Resumo
  await expect(assistente.getByText(nomeServico)).toBeVisible();
  await assistente.getByRole("button", { name: "Confirmar Agendamento" }).click();

  // Sucesso (seção 6.3)
  await expect(assistente.getByText("Agendamento confirmado!")).toBeVisible({ timeout: 10_000 });
  await expect(assistente.getByText(nomeServico)).toBeVisible();
});
