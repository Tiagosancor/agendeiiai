import { expect, test } from "@playwright/test";
import { execSync } from "node:child_process";

test.use({ serviceWorkers: "block" });

const RAIZ = "http://agendeiiai.localhost:3000";
const EMAIL = "outbox-e2e@plataforma.dev";
const SENHA = "SenhaOutbox123";

test.beforeAll(() => {
  execSync(`docker compose exec -T api dotnet Plataforma.Api.dll criar-admin-plataforma --email ${EMAIL} --nome "Outbox E2E"`, {
    cwd: process.cwd().endsWith("frontend") ? ".." : ".",
    input: `${SENHA}\n`,
    encoding: "utf-8",
  });
});

test("consulta a outbox real local sem PII, mutações, erros de console ou 5xx", async ({ page }) => {
  const errosConsole: string[] = [];
  const respostas5xx: string[] = [];
  page.on("console", (mensagem) => {
    if (mensagem.type() === "error") errosConsole.push(mensagem.text());
  });
  page.on("response", (resposta) => {
    if (resposta.status() >= 500) respostas5xx.push(`${resposta.status()} ${resposta.url()}`);
  });

  await page.goto(`${RAIZ}/plataforma`);
  await page.getByLabel("E-mail").fill(EMAIL);
  await page.getByLabel("Senha").fill(SENHA);
  await page.getByRole("button", { name: "Entrar" }).click();

  const resumo = page.getByTestId("resumo-outbox");
  await expect(resumo).toContainText("Saúde das notificações");
  await resumo.getByRole("link", { name: "Ver detalhes" }).click();
  await expect(page).toHaveURL(/\/plataforma\/notificacoes$/);
  await expect(page.getByRole("heading", { name: "Notificações", exact: true })).toBeVisible();

  await page.getByLabel("Status").selectOption("Pendente");
  await page.getByLabel("Tipo").selectOption("LembreteRetorno");
  await page.getByLabel("Canal").selectOption("Email");
  await page.getByLabel("ID do negócio").fill("22222222-2222-2222-2222-222222222222");
  await page.getByLabel("Atualizada a partir de").fill("2026-10-01");
  await page.getByLabel("Atualizada antes de").fill("2026-10-15");
  await page.getByLabel("Itens por página").selectOption("25");

  const consulta = page.waitForResponse((resposta) => {
    const url = new URL(resposta.url());
    return url.pathname === "/plataforma/notificacoes/outbox" && url.searchParams.get("status") === "Pendente";
  });
  await page.getByRole("button", { name: "Aplicar filtros" }).click();
  const resposta = await consulta;
  expect(resposta.status()).toBe(200);
  const url = new URL(resposta.url());
  expect(url.searchParams.get("tipo")).toBe("LembreteRetorno");
  expect(url.searchParams.get("canal")).toBe("Email");
  expect(url.searchParams.get("tamanho")).toBe("25");
  expect(url.searchParams.get("pagina")).toBe("1");
  await expect(page.getByRole("region", { name: "Mensagens da outbox" })).toBeVisible();

  await expect(page.getByRole("button", { name: /reenviar/i })).toHaveCount(0);
  await expect(page.getByRole("button", { name: /cancelar/i })).toHaveCount(0);
  await expect(page.getByRole("button", { name: /excluir/i })).toHaveCount(0);
  await expect(page.getByText(EMAIL, { exact: true })).toHaveCount(0);
  expect(errosConsole).toEqual([]);
  expect(respostas5xx).toEqual([]);
});
