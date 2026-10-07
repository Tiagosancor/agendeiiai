import { test, expect, type Page, type Route } from "@playwright/test";

// Nenhuma escrita chega à API: catálogo e ações usam exclusivamente fixtures locais.
const PROFISSIONAL = "11111111-1111-4111-8111-111111111111";
const SERVICO = "22222222-2222-4222-8222-222222222222";
const A = "2026-10-05";
const B = "2026-10-06";
const horario = (dia: string) => ({ inicio: `${dia}T${dia === A ? "12" : "13"}:00:00Z`, profissionalId: PROFISSIONAL });
const profissionais = [
  { id: PROFISSIONAL, nome: "João Teste", fotoUrl: null, funcao: "Barbeiro", servicoIds: [SERVICO], servicos: [{ servicoId: SERVICO, preco: 45, duracaoMinutos: 30 }] },
  { id: "33333333-3333-4333-8333-333333333333", nome: "Maria sem serviços", fotoUrl: null, funcao: null, servicoIds: [], servicos: [] },
];
const categorias = [{ categoriaId: "categoria-local", nome: "Cortes", servicos: [{ id: SERVICO, nome: "Corte Teste", preco: 45, duracaoMinutos: 30, popular: true, exibirNaPaginaInicial: true }] }];

function portao() {
  let liberar!: () => void;
  const espera = new Promise<void>((resolve) => { liberar = resolve; });
  return { espera, liberar };
}

interface Cenario {
  catalogo?: (rota: Route) => Promise<void>;
  disponibilidade?: (rota: Route, dia: string) => Promise<void>;
  reserva?: (rota: Route) => Promise<void>;
  vazio?: boolean;
  unico?: boolean;
}

async function preparar(page: Page, cenario: Cenario = {}) {
  const reservas: unknown[] = [];
  const confirmacoes: unknown[] = [];
  const erros: string[] = [];
  let verificacoes = 0;
  await page.clock.install({ time: new Date("2026-10-06T01:30:00Z") }); // Ainda 05/10 no estabelecimento.
  page.on("pageerror", (erro) => erros.push(erro.message));
  await page.context().route("**/*", async (rota) => {
    const pedido = rota.request();
    const url = new URL(pedido.url());
    if (!url.hostname.endsWith("localhost")) return rota.abort();
    if (url.pathname.startsWith("/api/publico/")) {
      if (url.pathname === "/api/publico/servicos") {
        if (cenario.catalogo) return cenario.catalogo(rota);
        return rota.fulfill({ json: cenario.vazio ? [] : categorias });
      }
      if (url.pathname === "/api/publico/profissionais") return rota.fulfill({ json: cenario.unico ? [profissionais[0]] : profissionais });
      if (url.pathname === "/api/publico/horarios-livres") {
        const dia = url.searchParams.get("data")!;
        if (cenario.disponibilidade) return cenario.disponibilidade(rota, dia);
        return rota.fulfill({ json: [horario(dia)] });
      }
      if (url.pathname === "/api/publico/reservas") {
        reservas.push(pedido.postDataJSON());
        if (cenario.reserva) return cenario.reserva(rota);
        return rota.fulfill({ status: 201, json: { agendamentoId: "reserva-local" } });
      }
      if (url.pathname === "/api/publico/codigos/validar") return rota.fulfill({ json: { tokenVerificacao: `token-local-${++verificacoes}` } });
      if (url.pathname === "/api/publico/codigos" || url.pathname === "/api/publico/codigos/reenviar") return rota.fulfill({ status: 202, body: "" });
      if (url.pathname === "/api/publico/agendamentos") {
        confirmacoes.push(pedido.postDataJSON());
        return rota.fulfill({ status: 201, json: { agendamentoId: "reserva-local", tokenAgendamento: "agendamento-local" } });
      }
      if (url.pathname.includes("/meus-agendamentos/")) return rota.fulfill({ json: { id: "reserva-local", nomeNegocio: "Acme", local: "", inicio: horario(A).inicio, fim: `${A}T12:30:00Z`, servicos: ["Corte Teste"], total: 45, status: "Agendado" } });
      throw new Error(`Endpoint público não previsto na fixture: ${url.pathname}`);
    }
    if (pedido.method() !== "GET" && pedido.method() !== "HEAD") {
      throw new Error(`Escrita não simulada: ${pedido.method()} ${url.pathname}`);
    }
    return rota.continue();
  });
  await page.goto("/");
  return { reservas, confirmacoes, erros };
}

const assistente = (page: Page) => page.getByTestId("assistente-agendamento");
const continuar = (page: Page) => assistente(page).getByRole("button", { name: "Continuar", exact: true });
const dias = (page: Page) => assistente(page).locator(".overflow-x-auto button");

async function abrirHorarios(page: Page, unico = false) {
  const agendar = page.getByRole("button", { name: "Agendar", exact: true });
  await expect(agendar).toBeEnabled();
  await agendar.click();
  if (!unico) {
    await assistente(page).getByRole("button", { name: /João Teste/ }).click();
    await continuar(page).click();
  }
  await assistente(page).getByRole("button", { name: /Corte Teste/ }).click();
  await continuar(page).click();
}

async function verificar(page: Page) {
  await assistente(page).getByPlaceholder("Nome completo").fill("Cliente Teste");
  await assistente(page).getByLabel("WhatsApp com DDD").fill("(71) 98888-7777");
  await assistente(page).getByRole("checkbox").check();
  await assistente(page).getByRole("button", { name: "Enviar código", exact: true }).click();
  await assistente(page).getByPlaceholder("000000").fill("123456");
  await assistente(page).getByRole("button", { name: "Confirmar código", exact: true }).click();
  await expect(assistente(page).getByRole("heading", { name: "Resumo", exact: true })).toBeVisible();
}

test.use({ serviceWorkers: "block", timezoneId: "America/Sao_Paulo" });

test("trocar A por B invalida horários imediatamente e reserva somente B", async ({ page }) => {
  const pendente = portao();
  let consultouB = false;
  const estado = await preparar(page, { disponibilidade: async (rota, dia) => {
    if (dia === B) { consultouB = true; await pendente.espera; }
    await rota.fulfill({ json: [horario(dia)] });
  } });
  await abrirHorarios(page);
  await assistente(page).getByRole("button", { name: "09:00", exact: true }).click();
  await dias(page).nth(1).click();
  await expect.poll(() => consultouB).toBe(true);
  await expect(assistente(page).getByRole("button", { name: "09:00", exact: true })).toHaveCount(0);
  await expect(assistente(page).getByRole("button", { name: "Selecione um horário" })).toBeDisabled();
  expect(estado.reservas).toHaveLength(0);
  pendente.liberar();
  await assistente(page).getByRole("button", { name: "10:00", exact: true }).click();
  await continuar(page).click();
  await expect(assistente(page).getByRole("heading", { name: "Seus dados" })).toBeVisible();
  expect(estado.reservas).toEqual([{ profissionalId: PROFISSIONAL, servicoIds: [SERVICO], inicio: horario(B).inicio }]);
  expect(estado.erros).toEqual([]);
});

test("resposta A atrasada não sobrescreve B", async ({ page }) => {
  const pendente = portao();
  let consultouA = false;
  await preparar(page, { disponibilidade: async (rota, dia) => {
    if (dia === A) { consultouA = true; await pendente.espera; }
    await rota.fulfill({ json: [horario(dia)] });
  } });
  await abrirHorarios(page);
  await expect.poll(() => consultouA).toBe(true);
  await dias(page).nth(1).click();
  await expect(assistente(page).getByRole("button", { name: "10:00", exact: true })).toBeVisible();
  const respostaA = page.waitForResponse((r) => r.url().includes(`data=${A}`));
  pendente.liberar();
  await respostaA;
  await expect(assistente(page).getByRole("button", { name: "09:00", exact: true })).toHaveCount(0);
  await expect(assistente(page).getByRole("button", { name: "10:00", exact: true })).toBeVisible();
});

test("erro B não restaura A; retry conserva data e escolha do serviço", async ({ page }) => {
  let tentativasB = 0;
  const estado = await preparar(page, { disponibilidade: async (rota, dia) => {
    if (dia === B && ++tentativasB === 1) return rota.fulfill({ status: 500, json: { title: "Falha simulada" } });
    await rota.fulfill({ json: [horario(dia)] });
  } });
  await abrirHorarios(page);
  await expect(assistente(page).getByRole("button", { name: "09:00", exact: true })).toBeVisible();
  await dias(page).nth(1).click();
  await expect(assistente(page).getByText("Não foi possível consultar os horários.", { exact: true })).toBeVisible();
  await expect(assistente(page).getByRole("button", { name: "09:00", exact: true })).toHaveCount(0);
  await expect(assistente(page).getByRole("button", { name: "Selecione um horário" })).toBeDisabled();
  await assistente(page).getByRole("button", { name: "Tentar novamente" }).click();
  await expect(assistente(page).getByRole("button", { name: "10:00", exact: true })).toBeVisible();
  await expect(assistente(page).getByText("1 serviço(s) · 30 min")).toBeVisible();
  expect(estado.erros).toEqual([]);
});

test("catálogo: loading, erro, retry com erro e retry com sucesso", async ({ page }) => {
  const pendente = portao();
  let tentativas = 0;
  const estado = await preparar(page, { catalogo: async (rota) => {
    const tentativa = ++tentativas;
    if (tentativa === 1) await pendente.espera;
    await rota.fulfill(tentativa <= 2 ? { status: 500, json: { title: "Falha simulada" } } : { json: categorias });
  } });
  await expect(page.getByText("Carregando serviços e profissionais...")).toBeVisible();
  await expect(page.getByRole("button", { name: "Agendar", exact: true })).toBeDisabled();
  pendente.liberar();
  await expect(page.getByText("Não foi possível carregar os serviços e profissionais.")).toBeVisible();
  await expect(page.getByText("Nenhum serviço disponível no momento.")).toHaveCount(0);
  await page.getByRole("button", { name: "Tentar novamente" }).click();
  await expect.poll(() => tentativas).toBe(2);
  await expect(page.getByText("Não foi possível carregar os serviços e profissionais.")).toBeVisible();
  await page.getByRole("button", { name: "Tentar novamente" }).click();
  await expect(page.getByRole("button", { name: "Agendar", exact: true })).toBeEnabled();
  await expect(page.getByRole("button", { name: /Corte Teste/ }).first()).toBeVisible();
  expect(estado.erros).toEqual([]);
});

test("catálogo realmente vazio é diferente de request com erro", async ({ page }) => {
  await preparar(page, { vazio: true });
  await expect(page.getByText("Nenhum serviço disponível no momento.")).toBeVisible();
  await expect(page.getByRole("button", { name: "Tentar novamente" })).toHaveCount(0);
});

test("profissional sem serviços informa vazio e permite escolher outro", async ({ page }) => {
  await preparar(page);
  await expect(page.getByRole("button", { name: "Agendar", exact: true })).toBeEnabled();
  await page.getByRole("button", { name: "Agendar", exact: true }).click();
  await assistente(page).getByRole("button", { name: "Maria sem serviços" }).click();
  await continuar(page).click();
  await expect(assistente(page).getByText("Nenhum serviço disponível para essa escolha.")).toBeVisible();
  await assistente(page).getByRole("button", { name: "Escolher outro profissional" }).click();
  await assistente(page).getByRole("button", { name: /João Teste/ }).click();
  await continuar(page).click();
  await expect(assistente(page).getByRole("button", { name: /Corte Teste/ })).toBeVisible();
});

test("mudar telefone invalida token anterior; nova verificação permite confirmar", async ({ page }) => {
  const estado = await preparar(page);
  await abrirHorarios(page);
  await assistente(page).getByRole("button", { name: "09:00", exact: true }).click();
  await continuar(page).click();
  await verificar(page);
  await assistente(page).getByRole("button", { name: "← Voltar" }).click();
  await assistente(page).getByRole("button", { name: "Mudar dados" }).click();
  await assistente(page).getByLabel("WhatsApp com DDD").fill("(71) 97777-6666");
  await expect(assistente(page).getByRole("button", { name: "Confirmar código", exact: true })).toHaveCount(0);
  await expect(assistente(page).getByRole("button", { name: "Enviar código", exact: true })).toBeEnabled();
  expect(estado.confirmacoes).toHaveLength(0);
  await assistente(page).getByRole("button", { name: "Enviar código", exact: true }).click();
  await assistente(page).getByPlaceholder("000000").fill("654321");
  await assistente(page).getByRole("button", { name: "Confirmar código", exact: true }).click();
  await assistente(page).getByRole("button", { name: "Confirmar Agendamento", exact: true }).click();
  await expect(assistente(page).getByText("Agendamento confirmado!", { exact: true })).toBeVisible();
  expect(estado.confirmacoes).toEqual([expect.objectContaining({ telefone: "(71) 97777-6666", tokenVerificacao: "token-local-2" })]);
});

test("nome e e-mail não são claims da verificação e não invalidam o telefone verificado", async ({ page }) => {
  const estado = await preparar(page);
  await abrirHorarios(page);
  await assistente(page).getByRole("button", { name: "09:00", exact: true }).click();
  await continuar(page).click();
  await verificar(page);
  await assistente(page).getByRole("button", { name: "← Voltar" }).click();
  await assistente(page).getByRole("button", { name: "Mudar dados" }).click();
  await assistente(page).getByPlaceholder("Nome completo").fill("Outro nome");
  await assistente(page).getByPlaceholder("E-mail", { exact: true }).fill("cliente@teste.com");
  await expect(continuar(page)).toBeEnabled();
  await continuar(page).click();
  await assistente(page).getByRole("button", { name: "Confirmar Agendamento", exact: true }).click();
  await expect(assistente(page).getByText("Agendamento confirmado!", { exact: true })).toBeVisible();
  expect(estado.confirmacoes).toEqual([expect.objectContaining({ tokenVerificacao: "token-local-1", nome: "Outro nome", email: "cliente@teste.com" })]);
});

test("reserva bloqueia double submit e recupera ação após erro", async ({ page }) => {
  const pendente = portao();
  let tentativas = 0;
  const estado = await preparar(page, { reserva: async (rota) => {
    const tentativa = ++tentativas;
    if (tentativa === 1) { await pendente.espera; await rota.fulfill({ status: 500, json: { title: "Falha simulada" } }); }
    else await rota.fulfill({ status: 201, json: { agendamentoId: "reserva-local" } });
  } });
  await abrirHorarios(page);
  await assistente(page).getByRole("button", { name: "09:00", exact: true }).click();
  await continuar(page).evaluate((botao) => { (botao as HTMLButtonElement).click(); (botao as HTMLButtonElement).click(); });
  await expect(assistente(page).getByRole("button", { name: "Reservando..." })).toBeDisabled();
  await expect.poll(() => tentativas).toBe(1);
  expect(estado.reservas).toHaveLength(1);
  pendente.liberar();
  await expect(assistente(page).getByText("Não foi possível reservar esse horário. Tente novamente.")).toBeVisible();
  await expect(continuar(page)).toBeEnabled();
  await continuar(page).click();
  await expect(assistente(page).getByRole("heading", { name: "Seus dados" })).toBeVisible();
  expect(estado.reservas).toHaveLength(2);
});

for (const fuso of ["America/Sao_Paulo", "Europe/Lisbon", "America/New_York"]) {
  test(`hora, data inicial, resumo e sucesso usam fuso do negócio sob ${fuso}`, async ({ browser }) => {
    const contexto = await browser.newContext({ timezoneId: fuso, baseURL: "http://acme.agendeiiai.localhost:3000", serviceWorkers: "block" });
    const page = await contexto.newPage();
    await preparar(page, { unico: true });
    await abrirHorarios(page, true);
    await expect(dias(page).first()).toContainText("5");
    await assistente(page).getByRole("button", { name: "09:00", exact: true }).click();
    await continuar(page).click();
    await verificar(page);
    await expect(assistente(page).getByText(/segunda-feira, 05 de outubro às 09:00/)).toBeVisible();
    await assistente(page).getByRole("button", { name: "Confirmar Agendamento", exact: true }).click();
    await expect(assistente(page).locator("dl")).toContainText("segunda-feira, 05 de outubro");
    await expect(assistente(page).locator("dl")).toContainText("09:00");
    await contexto.close();
  });
}

for (const largura of [375, 390, 430, 768, 1024, 1440]) {
  for (const tema of ["light", "dark"]) {
    test(`fluxo público sem overflow em ${largura}px / ${tema}`, async ({ page }) => {
      await page.setViewportSize({ width: largura, height: 900 });
      await page.addInitScript((valor) => localStorage.setItem("tema", valor), tema);
      await preparar(page);
      const semOverflow = async () => {
        await expect(page.locator("html")).toHaveAttribute("data-theme", tema);
        expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
        const painel = assistente(page);
        if (await painel.count()) expect(await painel.evaluate((e) => e.scrollWidth <= window.innerWidth)).toBe(true);
      };
      await semOverflow();
      await abrirHorarios(page);
      await semOverflow();
      await assistente(page).getByRole("button", { name: "09:00", exact: true }).click();
      await continuar(page).click();
      await semOverflow();
      await verificar(page);
      await semOverflow();
      await assistente(page).getByRole("button", { name: "Confirmar Agendamento", exact: true }).click();
      await expect(assistente(page).getByText("Agendamento confirmado!", { exact: true })).toBeVisible();
      await semOverflow();
    });
  }
}
