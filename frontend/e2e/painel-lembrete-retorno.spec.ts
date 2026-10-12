import { test, expect, type Page } from "@playwright/test";

const BASE = "http://app.agendeiiai.localhost:3000";

async function entrar(page: Page) {
  await page.goto(`${BASE}/painel/login`);
  await page.getByLabel("E-mail").fill("admin@acme.dev");
  await page.getByLabel("Senha").fill("Admin!123");
  for (let tentativa = 0; tentativa < 2; tentativa++) {
    await page.getByRole("button", { name: "Entrar", exact: true }).click();
    try {
      await expect(page).toHaveURL(/\/painel$/, { timeout: 10_000 });
      return;
    } catch {
      if (tentativa === 1) throw new Error("Não foi possível autenticar no painel após duas tentativas.");
      await expect(page.getByRole("button", { name: "Entrar", exact: true })).toBeEnabled();
    }
  }
}

test("configura, valida e consulta os resultados do lembrete de retorno", async ({ page }) => {
  test.setTimeout(120_000);
  await entrar(page);

  const carregamento = page.waitForResponse((resposta) =>
    resposta.url().endsWith("/painel/configuracoes/lembrete-retorno")
    && resposta.request().method() === "GET"
    && resposta.ok());
  await page.goto(`${BASE}/painel/configuracoes/notificacoes`);
  const original = await (await carregamento).json();

  const secao = page.getByRole("region", { name: "Lembrete de retorno" });
  await expect(secao).toBeVisible();
  await expect(secao.getByLabel("Dias após o atendimento")).toHaveValue(String(original.dias));
  await expect(secao.getByLabel("Horário de envio")).toHaveValue(original.horario.slice(0, 5));
  await expect(secao.getByLabel("Mensagem")).toHaveValue(original.mensagem);
  await expect(secao).toContainText("{nome_cliente}");
  await expect(secao).toContainText("{link_agendamento}");

  await expect(secao.getByRole("button", { name: "Salvar", exact: true })).toBeDisabled();
  if (!original.ativo) {
    await secao.getByRole("switch", { name: "Ativar lembrete de retorno" }).check();
    await expect(secao.getByText("Ao reativar, não serão enviados lembretes de datas passadas.")).toBeVisible();
  }
  await secao.getByLabel("Mensagem").fill("Olá ");
  await secao.getByRole("button", { name: "{nome_cliente}" }).click();
  await expect(secao.getByText("Olá Ana", { exact: false })).toBeVisible();
  await secao.getByLabel("Mensagem").fill("Olá {variavel_invalida}");
  await secao.getByRole("button", { name: "Salvar", exact: true }).click();
  await expect(secao.getByText("Variável não reconhecida: {variavel_invalida}.")).toBeVisible();

  await secao.getByLabel("Mensagem").fill(original.mensagem);
  const diasTemporarios = original.dias === 365 ? 364 : original.dias + 1;
  await secao.getByLabel("Dias após o atendimento").fill(String(diasTemporarios));
  const gravada = page.waitForResponse((resposta) =>
    resposta.url().endsWith("/painel/configuracoes/lembrete-retorno")
    && resposta.request().method() === "PUT");
  await secao.getByRole("button", { name: "Salvar", exact: true }).click();
  expect((await gravada).status()).toBe(204);
  await expect(secao.getByRole("status")).toHaveText("Configuração do lembrete atualizada.");

  await secao.getByLabel("Dias após o atendimento").fill(String(original.dias));
  if (!original.ativo) await secao.getByRole("switch", { name: "Ativar lembrete de retorno" }).uncheck();
  const restaurada = page.waitForResponse((resposta) =>
    resposta.url().endsWith("/painel/configuracoes/lembrete-retorno")
    && resposta.request().method() === "PUT");
  await secao.getByRole("button", { name: "Salvar", exact: true }).click();
  expect((await restaurada).status()).toBe(204);

  await expect(secao.getByRole("heading", { name: "Últimos 30 dias" })).toBeVisible();
  await expect(secao.getByText(/Nenhum lembrete enviado ainda|Geraram novo agendamento/)).toBeVisible();

  for (const largura of [375, 390, 768, 1440]) {
    await page.setViewportSize({ width: largura, height: 932 });
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `${largura}px`).toBe(true);
    for (const controle of ["Dias após o atendimento", "Horário de envio", "Mensagem"]) {
      expect((await secao.getByLabel(controle).boundingBox())!.height).toBeGreaterThanOrEqual(44);
    }
  }
});

for (const tema of ["light", "dark"]) {
  test(`aceite individual do lembrete funciona em clientes no tema ${tema}`, async ({ page }) => {
    test.setTimeout(120_000);
    await page.addInitScript((valor) => localStorage.setItem("tema", valor), tema);
    await entrar(page);

    const carregamento = page.waitForResponse((resposta) =>
      resposta.url().endsWith("/painel/clientes")
      && resposta.request().method() === "GET"
      && resposta.headers()["content-type"]?.includes("application/json")
      && resposta.ok());
    await page.goto(`${BASE}/painel/clientes`);
    const clientes = await (await carregamento).json();
    const cliente = clientes.find((item: { excluido: boolean }) => !item.excluido);
    expect(cliente, "a base local precisa ter ao menos um cliente ativo").toBeTruthy();

    const linha = page.getByRole("row").filter({ hasText: cliente.nome }).first();
    const aceite = linha.getByRole("checkbox");
    await expect(aceite).toBeChecked({ checked: cliente.aceitaLembreteRetorno });

    const alteracao = page.waitForResponse((resposta) =>
      resposta.url().endsWith(`/painel/clientes/${cliente.id}/aceita-lembrete-retorno`)
      && resposta.request().method() === "PATCH");
    await aceite.click();
    expect((await alteracao).status()).toBe(204);
    await expect(aceite).toBeChecked({ checked: !cliente.aceitaLembreteRetorno });

    const restauracao = page.waitForResponse((resposta) =>
      resposta.url().endsWith(`/painel/clientes/${cliente.id}/aceita-lembrete-retorno`)
      && resposta.request().method() === "PATCH");
    await aceite.click();
    expect((await restauracao).status()).toBe(204);
    await expect(aceite).toBeChecked({ checked: cliente.aceitaLembreteRetorno });

    for (const largura of [390, 768, 1440]) {
      await page.setViewportSize({ width: largura, height: 932 });
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `${tema} ${largura}px`).toBe(true);
      expect((await aceite.boundingBox())!.height).toBeGreaterThanOrEqual(18);
    }
  });
}
