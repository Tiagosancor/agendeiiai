import { test, expect, type APIRequestContext } from "@playwright/test";
import { execSync } from "node:child_process";

/**
 * Administração da plataforma e tela de assinatura (seção 7, ajuste 4d), contra a pilha real
 * do `docker compose up`. O administrador é criado pelo comando de linha de verdade, dentro
 * do container da API (senha pela entrada padrão). Suspende o negócio de dev "acme", confere
 * o efeito na página pública e no painel, e reativa — o afterEach reativa de qualquer jeito
 * para não deixar os outros specs com o acme suspenso.
 */

const API = "http://localhost:5080";
const RAIZ = "http://agendeiiai.localhost:3000";
const EMAIL_DONO = "dono-e2e@plataforma.dev";
const SENHA_DONO = "SenhaDoDono123";

function criarAdministradorPelaLinhaDeComando() {
  execSync(`docker compose exec -T api dotnet Plataforma.Api.dll criar-admin-plataforma --email ${EMAIL_DONO} --nome "Dono E2E"`, {
    cwd: process.cwd().endsWith("frontend") ? ".." : ".",
    input: `${SENHA_DONO}\n`,
    encoding: "utf-8",
  });
}

async function tokenPlataforma(request: APIRequestContext): Promise<string> {
  const resposta = await request.post(`${API}/plataforma/auth/login`, { data: { email: EMAIL_DONO, senha: SENHA_DONO } });
  expect(resposta.ok()).toBeTruthy();
  return (await resposta.json()).accessToken;
}

async function idDoAcme(request: APIRequestContext, token: string): Promise<string> {
  const lista = await (await request.get(`${API}/plataforma/negocios`, { headers: { Authorization: `Bearer ${token}` } })).json();
  return lista.find((n: { slug: string }) => n.slug === "acme").id;
}

test.beforeAll(() => criarAdministradorPelaLinhaDeComando());

test.afterEach(async ({ request }) => {
  const token = await tokenPlataforma(request);
  const id = await idDoAcme(request, token);
  await request.post(`${API}/plataforma/negocios/${id}/reativar`, { headers: { Authorization: `Bearer ${token}` } });
});

test("dono suspende e reativa um negócio pela administração, e o efeito aparece no público e no painel", async ({ page, browser }) => {
  page.on("dialog", (dialogo) => dialogo.accept());

  await page.goto(`${RAIZ}/plataforma`);
  await page.getByLabel("E-mail").fill(EMAIL_DONO);
  await page.getByLabel("Senha").fill(SENHA_DONO);
  await page.getByRole("button", { name: "Entrar" }).click();

  await page.getByRole("row", { name: /Acme Barbearia/ }).click();
  const detalhe = page.getByTestId("detalhe-negocio-plataforma");
  await detalhe.getByLabel("Motivo da suspensão").fill("Teste e2e");
  await detalhe.getByRole("button", { name: "Suspender" }).click();
  await expect(detalhe.getByText("Suspensa.", { exact: true })).toBeVisible();

  // Página pública do negócio: continua no ar, sem agendar online e sem falar de pagamento.
  const publico = await browser.newPage();
  await publico.goto("http://acme.agendeiiai.localhost:3000/");
  await expect(publico.getByTestId("agendamento-por-telefone")).toBeVisible();
  await expect(publico.getByRole("button", { name: /Agendar/ })).toHaveCount(0);

  // Painel do negócio: qualquer tela leva à assinatura.
  const painel = await browser.newPage();
  await painel.goto(`${RAIZ}/painel/login`);
  await painel.getByLabel("E-mail").fill("admin@acme.dev");
  await painel.getByLabel("Senha").fill("Admin!123");
  await painel.getByRole("button", { name: "Entrar" }).click();
  await expect(painel).toHaveURL(/\/painel\/assinatura$/);
  await expect(painel.getByTestId("estado-assinatura")).toHaveText("Suspensa");
  await expect(painel.getByTestId("aviso-assinatura")).toContainText("suspensa");

  await detalhe.getByRole("button", { name: "Reativar" }).click();
  await expect(detalhe.getByText("Reativada.", { exact: true })).toBeVisible();

  await publico.reload();
  await expect(publico.getByRole("button", { name: "Agendar Agora" })).toBeVisible();
});

test("tela de assinatura mostra as instruções de pagamento configuradas", async ({ page }) => {
  await page.goto(`${RAIZ}/painel/login`);
  await page.getByLabel("E-mail").fill("admin@acme.dev");
  await page.getByLabel("Senha").fill("Admin!123");
  await page.getByRole("button", { name: "Entrar" }).click();
  await expect(page).toHaveURL(/\/painel$/);

  await page.getByRole("link", { name: "Assinatura" }).click();
  await page.getByRole("button", { name: "Assinar agora" }).click();

  const instrucoes = page.getByTestId("instrucoes-pagamento");
  await expect(instrucoes).toContainText("Chave PIX");
  await expect(instrucoes.getByRole("link", { name: "Enviar comprovante pelo WhatsApp" })).toHaveAttribute("href", /wa\.me\/\d+/);
});
