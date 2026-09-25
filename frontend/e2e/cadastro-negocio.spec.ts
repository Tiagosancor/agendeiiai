import { test, expect } from "@playwright/test";
import { execSync } from "node:child_process";

/**
 * Cadastro de negócio novo ponta a ponta (seção 6.5, ajuste 4c), contra a pilha real do
 * `docker compose up`. Critério de aceite do ajuste 4: escolher um plano, cadastrar-se e
 * entrar no painel em menos de 3 minutos. O código chega por e-mail; como no e2e do
 * agendamento, é lido do log do provedor `Fake` — nunca de um atalho na aplicação.
 */

const RAIZ = "http://agendeiiai.localhost:3000";

function extrairCodigoDoEmail(email: string): string {
  const log = execSync("docker compose logs api --tail 300", {
    cwd: process.cwd().endsWith("frontend") ? ".." : ".",
    encoding: "utf-8",
  });

  const inicio = log.lastIndexOf(`Para: ${email}`);
  const match = inicio >= 0 ? log.slice(inicio).match(/<h2>(\d{6})<\/h2>/) : null;
  if (!match) throw new Error(`Código de cadastro não encontrado no log para ${email}.`);
  return match[1];
}

test("visitante escolhe plano, cadastra o negócio e entra no painel em menos de 3 minutos", async ({ page }) => {
  const inicio = Date.now();
  const sufixo = Date.now().toString().slice(-8);
  const email = `dono-${sufixo}@teste.dev`;

  // 0. Site do produto (seção 6.4): escolhe o plano no card, já no anual.
  await page.goto(RAIZ);
  const planos = page.locator("#planos");
  await planos.getByRole("radio", { name: "Anual" }).click();
  const ritmo = planos.getByTestId("card-plano").filter({ hasText: "Ritmo" });
  await expect(ritmo).toContainText("Mais escolhido");
  await expect(ritmo).toContainText("68,90");
  await ritmo.getByRole("link", { name: "Começar teste grátis" }).click();

  // 1. Plano — chega pré-selecionado do card clicado.
  const assistente = page.getByTestId("assistente-cadastro");
  await expect(assistente.getByRole("radio", { name: "Anual" })).toHaveAttribute("aria-checked", "true");
  await expect(assistente.getByRole("radio", { name: /Ritmo/ })).toBeChecked();
  await expect(assistente.getByText("Depois,")).toContainText("68,90");
  await assistente.getByRole("button", { name: "Continuar" }).click();

  // 2. Negócio — o endereço é sugerido a partir do nome e checado em tempo real
  await assistente.getByLabel("Nome do negócio").fill(`Barbearia Teste ${sufixo}`);
  await assistente.getByLabel("Tipo").selectOption("Barbearia");
  await expect(assistente.getByText(/Disponível:/)).toContainText(`barbearia-teste-${sufixo}`);
  await assistente.getByRole("button", { name: "Continuar" }).click();

  // 3. Dados de acesso
  await assistente.getByLabel("Seu nome").fill("Dono do Teste");
  await assistente.getByLabel("E-mail").fill(email);
  await assistente.getByLabel("Telefone (WhatsApp)").fill(`(71) 9${sufixo}`);
  await assistente.getByLabel("Senha").fill("SenhaForte123");
  await assistente.getByRole("checkbox").check();
  await assistente.getByRole("button", { name: "Enviar código" }).click();

  // 4. Código do e-mail
  await expect(assistente.getByText("Confirme seu e-mail")).toBeVisible();
  await expect.poll(() => { try { return extrairCodigoDoEmail(email); } catch { return null; } }).not.toBeNull();
  await assistente.getByLabel("Código").fill(extrairCodigoDoEmail(email));
  await assistente.getByRole("button", { name: "Confirmar e criar conta" }).click();

  // 5. Pronto — já autenticado
  await expect(assistente.getByText("Tudo pronto!")).toBeVisible();
  await assistente.getByRole("link", { name: "Ir para o painel" }).click();
  await expect(page).toHaveURL(/\/painel$/);
  await expect(page.getByTestId("primeiros-passos")).toBeVisible();

  expect(Date.now() - inicio).toBeLessThan(3 * 60 * 1000);
});

test("endereço reservado é recusado na hora, sem deixar continuar", async ({ page }) => {
  await page.goto(`${RAIZ}/cadastro`);
  const assistente = page.getByTestId("assistente-cadastro");

  await assistente.getByRole("radio", { name: /Começo/ }).click();
  await assistente.getByRole("button", { name: "Continuar" }).click();
  await assistente.getByLabel("Nome do negócio").fill("Admin");
  await assistente.getByLabel("Tipo").selectOption("Salao");

  await expect(assistente.getByText("Este endereço é reservado. Escolha outro.")).toBeVisible();
  await expect(assistente.getByRole("button", { name: "Preencha os dados do negócio" })).toBeDisabled();
});
