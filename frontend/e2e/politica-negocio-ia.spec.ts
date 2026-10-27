import { test, expect } from "@playwright/test";

test("política do negócio explica o assistente, os dados enviados e o aceite pelo WhatsApp", async ({ page }) => {
  const resposta = await page.goto("http://acme.agendeiiai.localhost:3000/privacidade");

  expect(resposta?.status()).toBe(200);
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Política de privacidade");
  await expect(page.getByRole("heading", { level: 2 })).toHaveText([
    "O que coletamos",
    "Atendimento pelo WhatsApp com assistente virtual",
    "Como a inteligência artificial usa as suas mensagens",
    "Como o agendamento é confirmado",
    "Decisões automatizadas",
    "Por quanto tempo guardamos",
    "Seus direitos",
    "Contato",
  ]);

  const conteudo = page.getByRole("main");
  await expect(conteudo).toContainText("Esta página descreve como Acme Barbearia");
  await expect(conteudo).toContainText("O atendimento de Acme Barbearia");
  await expect(conteudo).toContainText("Google (modelo Gemini)");
  await expect(conteudo).toContainText("que pode processar os dados fora do Brasil");
  await expect(conteudo).toContainText("esse filtro não é perfeito");
  await expect(conteudo).toContainText("Versão 2.0 desta política.");
  await expect(conteudo).toContainText("Não pedimos código de confirmação no WhatsApp");
  await expect(conteudo).toContainText("continuamos enviando um código de 6 dígitos para confirmar o telefone");
  await expect(conteudo).toContainText("As reservas que você não confirma expiram sozinhas em 10 minutos");
  await expect(conteudo.locator("strong")).toHaveText([
    "assistente virtual com inteligência artificial",
    "não envie dados sensíveis",
    'o agendamento só vale depois que você responde "SIM"',
  ]);
  await expect(page.getByTestId("aviso-rascunho")).toHaveCount(0);
});

test("domínio raiz preserva a política do produto", async ({ page }) => {
  const resposta = await page.goto("http://agendeiiai.localhost:3000/privacidade");

  expect(resposta?.status()).toBe(200);
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Política de Privacidade");
  await expect(page.getByRole("heading", { name: "Dados de quem assina", exact: true })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Assistente virtual de WhatsApp", exact: true })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Como o agendamento é confirmado", exact: true })).toHaveCount(0);
  await expect(page.getByTestId("aviso-rascunho")).toHaveCount(0);
});
