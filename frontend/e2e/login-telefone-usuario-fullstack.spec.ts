import { test, expect, type Page } from "@playwright/test";
import { execFileSync } from "node:child_process";
import { randomInt, randomUUID } from "node:crypto";

// Exige a API desta branch, banco de desenvolvimento e EmailSenderFake.
const PAINEL = process.env.E2E_PAINEL_URL ?? "http://app.agendeiiai.localhost:3000";
const API = "http://localhost:5080";

async function entrar(page: Page, identificador: string, senha: string) {
  await page.goto(`${PAINEL}/painel/login`);
  await page.getByLabel("E-mail").fill(identificador);
  await page.getByLabel("Senha", { exact: true }).fill(senha);
  await page.getByRole("button", { name: "Entrar", exact: true }).click();
  await expect(page).toHaveURL(/\/painel$/);
}

async function sair(page: Page) {
  if (await page.getByRole("button", { name: "Abrir menu" }).isVisible()) await page.getByRole("button", { name: "Abrir menu" }).click();
  await page.getByRole("button", { name: /^Sair/ }).click();
  await expect(page).toHaveURL(/\/painel\/login$/);
}

function codigoDoEmail(email: string): string | undefined {
  const log = execFileSync("docker", ["compose", "logs", "api", "--tail", "400"], {
    cwd: process.cwd().endsWith("frontend") ? ".." : ".", encoding: "utf8",
  });
  const inicio = log.lastIndexOf(`Para: ${email}`);
  return inicio >= 0 ? log.slice(inicio).match(/<h2>(\d{6})<\/h2>/)?.[1] : undefined;
}

test("conta sem e-mail entra por usuário e telefone, confirma e-mail, troca senha e mantém sessão", async ({ page, request }) => {
  test.setTimeout(120_000);
  const sufixo = randomUUID().slice(0, 8);
  const nomeUsuario = `zz-e2e-${sufixo}`;
  const telefoneSemNove = `71${randomInt(80000000, 90000000)}`;
  const telefone = `719${telefoneSemNove.slice(2)}`;
  const email = `e2e-${sufixo}@teste.dev`;
  const loginAdmin = await request.post(`${API}/painel/auth/login`, { data: { email: "admin@acme.dev", senha: "Admin!123" } });
  expect(loginAdmin.ok()).toBeTruthy();
  const headers = { Authorization: `Bearer ${(await loginAdmin.json()).accessToken}` };
  let usuarioId: string | undefined;
  try {
    await entrar(page, "admin@acme.dev", "Admin!123");
    await page.goto(`${PAINEL}/painel/usuarios`);
    await page.getByRole("button", { name: "Novo usuário", exact: true }).click();
    const modal = page.getByRole("dialog", { name: "Novo usuário", exact: true });
    await modal.getByLabel("Nome", { exact: true }).fill(`Zz Pessoa ${sufixo}`);
    await modal.getByLabel("Nome de usuário (opcional)", { exact: true }).fill(nomeUsuario);
    await modal.getByLabel("Telefone de login (opcional)", { exact: true }).fill(telefone);
    await modal.getByLabel("Senha", { exact: true }).fill("SenhaInicial123");
    const resposta = page.waitForResponse((r) => new URL(r.url()).pathname === "/painel/usuarios" && r.request().method() === "POST");
    await modal.getByRole("button", { name: "Criar", exact: true }).click();
    const criado = await resposta;
    expect(criado.status()).toBe(201);
    usuarioId = await criado.json();
    await expect(modal).toBeHidden();
    await sair(page);
    await entrar(page, nomeUsuario, "SenhaInicial123");
    await expect(page.getByText("Complete seu cadastro:", { exact: false })).toBeVisible();
    await sair(page);
    await entrar(page, telefoneSemNove, "SenhaInicial123");
    await page.goto(`${PAINEL}/painel/minha-conta`);
    await page.getByLabel("E-mail", { exact: true }).fill(email);
    await page.getByLabel("Senha atual para confirmar e-mail", { exact: true }).fill("SenhaInicial123");
    await page.getByRole("button", { name: "Solicitar código", exact: true }).click();
    await expect(page.getByLabel("Código de 6 dígitos", { exact: true })).toBeVisible();
    let codigo: string | undefined;
    await expect.poll(() => { codigo = codigoDoEmail(email); return !!codigo; }).toBe(true);
    await page.getByLabel("Código de 6 dígitos", { exact: true }).fill(codigo!);
    await page.getByRole("button", { name: "Confirmar e-mail", exact: true }).click();
    await expect(page.getByRole("status").filter({ hasText: "E-mail confirmado." })).toBeVisible();
    await expect(page.getByText("Complete seu cadastro:", { exact: false })).toHaveCount(0);
    await page.getByLabel("Senha atual", { exact: true }).fill("SenhaInicial123");
    await page.getByLabel("Nova senha", { exact: true }).fill("SenhaNova456");
    await page.getByRole("button", { name: "Trocar senha", exact: true }).click();
    await expect(page.getByRole("status").filter({ hasText: "Senha alterada." })).toBeVisible();
    await page.reload();
    await expect(page.getByRole("heading", { name: "Minha conta", exact: true })).toBeVisible();
    await expect(page.getByText("Sua senha foi definida pelo administrador.", { exact: false })).toHaveCount(0);
  } finally {
    if (usuarioId) expect((await request.delete(`${API}/painel/usuarios/${usuarioId}`, { headers })).ok()).toBeTruthy();
  }
});
