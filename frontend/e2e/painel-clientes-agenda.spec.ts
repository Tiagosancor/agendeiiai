import { expect, test, type Page } from "@playwright/test";

test.use({ serviceWorkers: "block" });

const PAINEL_BASE = "http://app.agendeiiai.localhost:3000";

async function abrirNovoAgendamento(page: Page) {
  await page.goto(`${PAINEL_BASE}/painel/login`);
  await page.getByLabel("E-mail").fill("admin@acme.dev");
  await page.getByLabel("Senha").fill("Admin!123");
  await page.getByRole("button", { name: "Entrar" }).click();
  await expect(page).toHaveURL(/\/painel$/, { timeout: 15_000 });
  await page.goto(`${PAINEL_BASE}/painel/agenda`);
  await page.getByRole("button", { name: "Novo agendamento" }).click();
  return page.getByRole("dialog", { name: "Novo agendamento" });
}

test("busca cliente com debounce, ignora resposta antiga e permite seleção por teclado", async ({ page }) => {
  test.setTimeout(90_000);
  const termos: string[] = [];
  await page.route("**/*", async (rota) => {
    const url = new URL(rota.request().url());
    if (url.pathname !== "/painel/agenda/clientes/busca") return rota.continue();
    const termo = url.searchParams.get("termo") ?? "";
    termos.push(termo);
    const headers = {
      "access-control-allow-origin": PAINEL_BASE,
      "access-control-allow-credentials": "true",
      "content-type": "application/json",
    };
    if (termo === "An") {
      await new Promise((resolve) => setTimeout(resolve, 700));
      try {
        await rota.fulfill({ status: 200, headers, body: JSON.stringify([{ id: "antiga", nome: "Ana Antiga", telefone: "+5571999990000", email: null }]) });
      } catch {
        // A requisição antiga foi cancelada pelo componente, que é justamente o comportamento testado.
      }
      return;
    }
    await rota.fulfill({ status: 200, headers, body: JSON.stringify([{ id: "atual", nome: "André Atual", telefone: "+5571988887777", email: null }]) });
  });

  const modal = await abrirNovoAgendamento(page);
  const busca = modal.getByLabel("Buscar cliente por nome ou telefone");
  await busca.fill("A");
  await page.waitForTimeout(400);
  expect(termos).toHaveLength(0);
  await busca.fill("An");
  await page.waitForTimeout(350);
  await busca.fill("Andre");
  await expect(modal.getByRole("option", { name: /André Atual/ })).toBeVisible();
  await page.waitForTimeout(700);
  await expect(modal.getByRole("option", { name: /Ana Antiga/ })).toHaveCount(0);
  await busca.press("ArrowDown");
  await busca.press("Enter");
  await expect(modal.getByText("André Atual", { exact: true })).toBeVisible();
  await expect(modal.getByText("(71) 98888-7777", { exact: true })).toBeVisible();
});

test("cadastro rápido valida, preserva o agendamento e seleciona cliente criado", async ({ page }) => {
  test.setTimeout(90_000);
  await page.route("**/painel/agenda/clientes/rapido", async (rota) => {
    const corpo = rota.request().postDataJSON();
    expect(corpo).toEqual({ nome: "Cliente Rápido", telefone: "(71) 98888-7777", email: "rapido@teste.dev" });
    await rota.fulfill({ status: 201, contentType: "application/json", body: JSON.stringify({ id: "novo", nome: "Cliente Rápido", telefone: "+5571988887777", email: "rapido@teste.dev" }) });
  });

  const modal = await abrirNovoAgendamento(page);
  const primeiroServico = modal.locator('input[type="checkbox"]').first();
  await primeiroServico.check();
  const busca = modal.getByLabel("Buscar cliente por nome ou telefone");
  await busca.fill("Cliente Rápido");
  await modal.getByRole("button", { name: "+ Cadastrar novo cliente" }).click();
  await expect(modal.getByLabel("Nome", { exact: true })).toHaveValue("Cliente Rápido");
  await modal.getByRole("button", { name: "Cadastrar e selecionar" }).click();
  await expect(modal.getByRole("alert")).toHaveText("Telefone inválido. Informe o DDD e o número.");
  await modal.getByLabel("Telefone", { exact: true }).fill("71988887777");
  await modal.getByLabel("E-mail (opcional)").fill("rapido@teste.dev");
  await modal.getByRole("button", { name: "Cadastrar e selecionar" }).click();
  await expect(modal.getByRole("status")).toHaveText("Cliente cadastrado e selecionado.");
  await expect(modal.getByText("Cliente Rápido", { exact: true })).toBeVisible();
  await expect(primeiroServico).toBeChecked();
});

test("conflito de telefone oferece o cliente existente e a busca permite tentar novamente", async ({ page }) => {
  test.setTimeout(90_000);
  let buscas = 0;
  await page.route("**/*", async (rota) => {
    const url = new URL(rota.request().url());
    if (url.pathname !== "/painel/agenda/clientes/busca") return rota.continue();
    buscas++;
    const headers = {
      "access-control-allow-origin": PAINEL_BASE,
      "access-control-allow-credentials": "true",
      "content-type": "application/json",
    };
    if (buscas === 1) {
      await rota.fulfill({ status: 500, headers, body: JSON.stringify({ title: "Falha simulada" }) });
      return;
    }
    await rota.fulfill({ status: 200, headers, body: "[]" });
  });
  await page.route("**/painel/agenda/clientes/rapido", async (rota) => {
    await rota.fulfill({
      status: 409,
      contentType: "application/json",
      body: JSON.stringify({ title: "Já existe um cliente com este telefone.", codigo: "telefone_ja_cadastrado", id: "existente", nome: "Cliente Existente", telefone: "+5571977776666", email: null }),
    });
  });

  const modal = await abrirNovoAgendamento(page);
  const busca = modal.getByLabel("Buscar cliente por nome ou telefone");
  await busca.fill("Pessoa ausente");
  await expect(modal.getByRole("alert")).toContainText("Não foi possível buscar clientes.");
  await modal.getByRole("button", { name: "Tentar de novo" }).click();
  await expect(modal.getByText("Nenhum cliente encontrado.")).toBeVisible();
  await modal.getByRole("button", { name: "Cadastrar novo cliente", exact: true }).click();
  await modal.getByLabel("Telefone", { exact: true }).fill("71977776666");
  await modal.getByRole("button", { name: "Cadastrar e selecionar" }).click();
  await expect(modal.getByRole("alert")).toHaveText("Este telefone já está cadastrado.");
  await modal.getByRole("button", { name: "Usar cliente existente: Cliente Existente" }).click();
  await expect(modal.getByText("Cliente Existente", { exact: true })).toBeVisible();
});

test("cadastro rápido permanece utilizável no celular sem overflow global", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  const modal = await abrirNovoAgendamento(page);
  await modal.getByRole("button", { name: "+ Cadastrar novo cliente" }).click();
  const medidas = await page.evaluate(() => ({ scrollWidth: document.documentElement.scrollWidth, clientWidth: document.documentElement.clientWidth }));
  expect(medidas.scrollWidth).toBeLessThanOrEqual(medidas.clientWidth);
  await expect(modal.getByRole("button", { name: "Cadastrar e selecionar" })).toBeVisible();
});
