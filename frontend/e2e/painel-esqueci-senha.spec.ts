import { test, expect } from "@playwright/test";
import { execSync } from "node:child_process";

/**
 * "Esqueci minha senha" do painel e o botão de olho do campo de senha, contra a pilha real
 * do `docker compose up`. O link chega por e-mail e é lido do log do provedor `Fake`, como
 * o código do cadastro. Usa um usuário novo (criado pela API como o admin da acme) para não
 * mexer na senha do admin, que os outros testes usam.
 */

const PAINEL_BASE = "http://app.agendeiiai.localhost:3000";
const API = "http://localhost:5080";

function extrairLinkDoEmail(email: string): string {
  const log = execSync("docker compose logs api --tail 300", {
    cwd: process.cwd().endsWith("frontend") ? ".." : ".",
    encoding: "utf-8",
  });

  const inicio = log.lastIndexOf(`Para: ${email}`);
  const match = inicio >= 0 ? log.slice(inicio).match(/(https?:\/\/[^"\\\s]*\/painel\/redefinir-senha\?token=[A-Za-z0-9_-]+)/) : null;
  if (!match) throw new Error(`Link de redefinição não encontrado no log para ${email}.`);
  return match[1];
}

test("usuário mostra a senha digitada, pede o link e troca a senha", async ({ page, request }) => {
  const sufixo = Date.now().toString().slice(-8);
  const email = `recepcao-${sufixo}@teste.dev`;

  const login = await request.post(`${API}/painel/auth/login`, { data: { email: "admin@acme.dev", senha: "Admin!123" } });
  const { accessToken } = await login.json();
  const criado = await request.post(`${API}/painel/usuarios`, {
    headers: { Authorization: `Bearer ${accessToken}` },
    data: { nome: "Recepção E2E", email, senha: "SenhaAntiga1", perfil: "Recepcionista" },
  });
  expect(criado.ok()).toBeTruthy();

  // Olho: mostra e volta a esconder o que foi digitado.
  await page.goto(`${PAINEL_BASE}/painel/login`);
  const campoSenha = page.getByLabel("Senha");
  await campoSenha.fill("digitado123");
  await expect(campoSenha).toHaveAttribute("type", "password");
  await page.getByRole("button", { name: "Mostrar senha" }).click();
  await expect(campoSenha).toHaveAttribute("type", "text");
  await expect(campoSenha).toHaveValue("digitado123");
  await page.getByRole("button", { name: "Ocultar senha" }).click();
  await expect(campoSenha).toHaveAttribute("type", "password");

  // Pede o link.
  await page.getByRole("link", { name: "Esqueci minha senha" }).click();
  await expect(page).toHaveURL(/\/painel\/esqueci-senha$/);
  await page.getByLabel("E-mail").fill(email);
  await page.getByRole("button", { name: "Enviar link" }).click();
  await expect(page.getByText("vai receber um e-mail")).toBeVisible();

  // Abre o link do e-mail e cria a senha nova.
  await page.goto(extrairLinkDoEmail(email));
  await expect(page).toHaveURL(/\/painel\/redefinir-senha$/); // o token sai da barra de endereço
  await page.getByLabel("Senha nova").fill("SenhaNova2026");
  await page.getByRole("button", { name: "Salvar senha nova" }).click();
  await expect(page.getByText("Senha alterada")).toBeVisible();

  // Entra com a senha nova.
  await page.getByRole("link", { name: "Ir para a entrada" }).click();
  await page.getByLabel("E-mail").fill(email);
  await page.getByLabel("Senha").fill("SenhaNova2026");
  await page.getByRole("button", { name: "Entrar" }).click();
  await expect(page).toHaveURL(/\/painel$/);
});
