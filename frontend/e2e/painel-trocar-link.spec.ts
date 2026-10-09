import { test, expect } from "@playwright/test";
import { execFileSync } from "node:child_process";

/**
 * Item 13 da seção 14: trocar o link do negócio em "Meu negócio" (seção 5). Troca o link da acme de verdade e, no fim,
 * devolve o original direto no banco — pela API não dá, por causa do limite de uma troca a cada 30 dias.
 */

const PAINEL_BASE = "http://app.agendeiiai.localhost:3000";
const ADMIN_EMAIL = "admin@acme.dev";
const ADMIN_SENHA = "Admin!123";

/** SQL pela entrada padrão do psql do container (sem passar por aspas de shell, que mudam entre Windows e Linux). */
function sql(comando: string) {
  execFileSync("docker", ["compose", "exec", "-T", "postgres", "sh", "-c", 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1 -q'], {
    cwd: process.cwd().endsWith("frontend") ? ".." : ".",
    input: comando,
    stdio: ["pipe", "pipe", "pipe"],
  });
}

test.afterEach(() => {
  sql(
    "DELETE FROM slugs_anteriores WHERE negocio_id = (SELECT id FROM negocios WHERE slug LIKE 'acme-e2e-%' OR slug = 'acme'); " +
      "UPDATE negocios SET slug = 'acme', slug_alterado_em = NULL WHERE slug LIKE 'acme-e2e-%' OR slug = 'acme';",
  );
});

test("Administrador troca o link com aviso e o endereço antigo redireciona (301) para o novo", async ({ page }) => {
  const novo = `acme-e2e-${Date.now().toString().slice(-6)}`;

  await page.goto(`${PAINEL_BASE}/painel/login`);
  await page.getByLabel("E-mail").fill(ADMIN_EMAIL);
  await page.getByLabel("Senha").fill(ADMIN_SENHA);
  await page.getByRole("button", { name: "Entrar" }).click();
  await expect(page).toHaveURL(/\/painel$/);
  await page.goto(`${PAINEL_BASE}/painel/negocio`);

  const secao = page.getByTestId("link-negocio");
  await expect(secao.getByTestId("link-atual")).toHaveText(/^acme\./);
  await secao.getByRole("button", { name: "Editar" }).click();

  // Checagem em tempo real, com as regras do cadastro.
  const campo = secao.getByLabel("Novo endereço");
  await campo.fill("www");
  await expect(secao.getByRole("status")).toHaveText(/reservado/);
  await campo.fill(novo);
  await expect(secao.getByRole("status")).toHaveText("Disponível");

  // O aviso precisa ser confirmado antes.
  await expect(secao.getByText("Isso muda o endereço que seus clientes já têm.")).toBeVisible();
  const trocar = secao.getByRole("button", { name: "Trocar link" });
  await expect(trocar).toBeDisabled();
  await secao.getByLabel("Entendi, quero trocar o link").check();
  await trocar.click();

  await expect(secao.getByTestId("link-atual")).toHaveText(new RegExp(`^${novo}\\.`));
  await expect(secao.getByText(/Link trocado/)).toBeVisible();
  await expect(secao.getByText(/Você poderá trocar de novo em \d{2}\/\d{2}\/\d{4}/)).toBeVisible();
  await expect(secao.getByRole("button", { name: "Editar" })).toHaveCount(0);

  // O endereço antigo leva ao novo, mantendo o caminho (links já enviados por e-mail continuam valendo).
  const resposta = await page.goto("http://acme.agendeiiai.localhost:3000/privacidade?origem=cartao");
  expect(new URL(page.url()).host).toBe(`${novo}.agendeiiai.localhost:3000`);
  expect(new URL(page.url()).pathname).toBe("/privacidade");
  expect(new URL(page.url()).search).toBe("?origem=cartao");
  const redirecionamento = await resposta!.request().redirectedFrom()!.response();
  expect(redirecionamento!.status()).toBe(301);
  expect(redirecionamento!.headers()["cache-control"]).toBe("private, max-age=3600");

  // A página nova é a do negócio.
  await page.goto(`http://${novo}.agendeiiai.localhost:3000/`);
  await expect(page.getByRole("button", { name: "Agendar Agora" }).first()).toBeVisible();
});

test("slug que nunca existiu continua dando página não encontrada", async ({ page }) => {
  await page.goto("http://nunca-existiu-e2e.agendeiiai.localhost:3000/");
  await expect(page.getByText(/não encontrad/i).first()).toBeVisible();
});
