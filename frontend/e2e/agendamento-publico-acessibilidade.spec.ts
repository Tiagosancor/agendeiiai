import { test, expect, type Page, type Route } from "@playwright/test";

// Nenhuma escrita chega à API: catálogo e ações usam exclusivamente fixtures locais.
const PROFISSIONAL = "11111111-1111-4111-8111-111111111111";
const SERVICO = "22222222-2222-4222-8222-222222222222";
const A = "2026-10-05";
const horario = (dia: string) => ({ inicio: `${dia}T${dia === A ? "12" : "13"}:00:00Z`, profissionalId: PROFISSIONAL });
const profissionais = [
  { id: PROFISSIONAL, nome: "João Teste", fotoUrl: null, funcao: "Barbeiro", servicoIds: [SERVICO], servicos: [{ servicoId: SERVICO, preco: 45, duracaoMinutos: 30 }] },
  { id: "33333333-3333-4333-8333-333333333333", nome: "Maria sem serviços", fotoUrl: null, funcao: null, servicoIds: [], servicos: [] },
];
const categorias = [{ categoriaId: "categoria-local", nome: "Cortes", servicos: [{ id: SERVICO, nome: "Corte Teste", preco: 45, duracaoMinutos: 30, popular: true, exibirNaPaginaInicial: true }] }];

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

test.use({ serviceWorkers: "block", timezoneId: "America/Sao_Paulo" });


async function evidencia(page: Page, nome: string) {
  const painel = assistente(page);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  expect(await painel.evaluate(el => el.scrollWidth <= el.clientWidth)).toBe(true);
  for (const controle of await painel.locator("button, a").all()) {
    const caixa = await controle.boundingBox();
    if (caixa) {
      expect(caixa.height).toBeGreaterThanOrEqual(44);
      expect(caixa.width).toBeGreaterThanOrEqual(44);
    }
  }
  const caminho = test.info().outputPath(`${nome}.png`);
  await page.screenshot({ path: caminho });
  await test.info().attach(nome, { path: caminho, contentType: "image/png" });
}

test("modal contém foco, Escape fecha e restaura o botão de origem", async ({ page }) => {
  await preparar(page);
  const origem = page.getByRole("button", { name: "Agendar", exact: true });
  await origem.click();
  await expect(assistente(page)).toHaveAttribute("aria-modal", "true");
  await expect(assistente(page).getByRole("heading", { name: "Escolha o profissional" })).toBeFocused();
  for (let i = 0; i < 15; i++) {
    await page.keyboard.press(i % 3 ? "Tab" : "Shift+Tab");
    expect(await assistente(page).evaluate(el => el.contains(document.activeElement))).toBe(true);
  }
  await page.keyboard.press("Escape");
  await expect(assistente(page)).toHaveCount(0);
  await expect(origem).toBeFocused();
});

test("viewport mobile reduzida mantém campo com erro e CTA acessíveis", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 812 });
  await preparar(page);
  await abrirHorarios(page);
  await assistente(page).getByRole("button", { name: "09:00", exact: true }).click();
  await continuar(page).click();
  await assistente(page).getByLabel("Nome completo", { exact: true }).fill("Cliente Teste");
  await assistente(page).getByLabel("WhatsApp com DDD").fill("71988887777");
  await assistente(page).getByRole("checkbox").check();
  await assistente(page).getByRole("button", { name: "Enviar código", exact: true }).click();
  // Aproximação de espaço reduzido; não simula um teclado virtual real.
  await page.setViewportSize({ width: 390, height: 460 });
  const codigo = assistente(page).getByLabel("Código de confirmação", { exact: true });
  await codigo.fill("123456");
  await page.route("**/api/publico/codigos/validar", rota => rota.fulfill({ status: 400, json: {} }));
  await assistente(page).getByRole("button", { name: "Confirmar código", exact: true }).click();
  await expect(codigo).toBeFocused();
  const campo = await codigo.boundingBox();
  const rodape = await assistente(page).getByRole("button", { name: "Confirmar código", exact: true }).boundingBox();
  expect(campo!.y).toBeGreaterThanOrEqual(0);
  expect(campo!.y + campo!.height).toBeLessThanOrEqual(rodape!.y);
  expect(rodape!.y + rodape!.height).toBeLessThanOrEqual(460);
  await evidencia(page, "viewport-reduzida-erro");
});

test("seleção por teclado, progresso, código e erros associados", async ({ page }) => {
  await preparar(page);
  await abrirHorarios(page);
  await expect(dias(page).first()).toHaveAttribute("aria-pressed", "true");
  const hora = assistente(page).getByRole("button", { name: "09:00", exact: true });
  await hora.focus();
  await page.keyboard.press("Space");
  await expect(hora).toHaveAttribute("aria-pressed", "true");
  await continuar(page).focus();
  await page.keyboard.press("Enter");
  await expect(assistente(page).getByRole("heading", { name: "Seus dados" })).toBeFocused();
  await expect(assistente(page).getByText("Etapa 4 de 5")).toBeVisible();
  await assistente(page).getByLabel("Nome completo", { exact: true }).fill("Cliente Teste");
  await assistente(page).getByLabel("WhatsApp com DDD").fill("71988887777");
  await assistente(page).getByLabel("E-mail", { exact: true }).fill("cliente@example.test");
  await assistente(page).getByRole("checkbox").check();
  await assistente(page).getByRole("button", { name: "Enviar código", exact: true }).click();
  const codigo = assistente(page).getByLabel("Código de confirmação", { exact: true });
  await expect(codigo).toBeFocused();
  await expect(codigo).toHaveAttribute("inputmode", "numeric");
  await expect(codigo).toHaveAttribute("autocomplete", "one-time-code");
  await codigo.fill("123456");
  await page.route("**/api/publico/codigos/validar", rota => rota.fulfill({ status: 400, json: { mensagem: "inválido" } }));
  await assistente(page).getByRole("button", { name: "Confirmar código", exact: true }).click();
  await expect(codigo).toHaveAttribute("aria-invalid", "true");
  await expect(codigo).toBeFocused();
  const erroId = await assistente(page).getByRole("alert").getAttribute("id");
  expect((await codigo.getAttribute("aria-describedby"))?.split(" ")).toContain(erroId);
  await codigo.fill("654321");
  await expect(codigo).toHaveAttribute("aria-invalid", "false");
  await page.unroute("**/api/publico/codigos/validar");
  await assistente(page).getByRole("button", { name: "Confirmar código", exact: true }).click();
  await expect(assistente(page).getByRole("heading", { name: "Resumo", exact: true })).toBeFocused();
  await expect(assistente(page).getByLabel("Cupom", { exact: true })).toBeVisible();
  await expect(assistente(page).getByLabel("Observações (opcional)")).toBeVisible();
});

for (const largura of [375, 390, 430, 768, 1024, 1440]) {
  for (const tema of ["light", "dark"]) {
    test(`${largura}px ${tema}: jornada acessível sem overflow`, async ({ page }) => {
      await page.setViewportSize({ width: largura, height: largura < 768 ? 812 : 900 });
      await page.emulateMedia({ reducedMotion: "reduce" });
      await page.addInitScript(tema => localStorage.setItem("tema", tema), tema);
      await preparar(page);
      await expect(page.locator("html")).toHaveAttribute("data-theme", tema);
      await page.getByRole("button", { name: "Agendar", exact: true }).click();
      await evidencia(page, "profissionais");
      for (const botao of await assistente(page).getByRole("button").all()) {
        const caixa = await botao.boundingBox();
        if (caixa) { expect(caixa.height).toBeGreaterThanOrEqual(44); expect(caixa.width).toBeGreaterThanOrEqual(44); }
      }
      await assistente(page).getByRole("button", { name: /João Teste/ }).click();
      await continuar(page).click();
      await assistente(page).getByRole("button", { name: /Corte Teste/ }).click();
      await expect(assistente(page).getByRole("button", { name: /Corte Teste/ })).toHaveAttribute("aria-pressed", "true");
      await evidencia(page, "servicos");
      await continuar(page).click();
      await assistente(page).getByRole("button", { name: "09:00", exact: true }).click();
      await evidencia(page, "horarios");
      await continuar(page).click();
      await evidencia(page, "dados");
      await assistente(page).getByLabel("Nome completo", { exact: true }).fill("Cliente Teste");
      await assistente(page).getByLabel("WhatsApp com DDD").fill("71988887777");
      await assistente(page).getByRole("checkbox").check();
      await assistente(page).getByRole("button", { name: "Enviar código", exact: true }).click();
      await evidencia(page, "codigo");
      const codigo = assistente(page).getByLabel("Código de confirmação", { exact: true });
      await expect(codigo).toBeFocused();
      await codigo.fill("123456");
      await assistente(page).getByRole("button", { name: "Confirmar código", exact: true }).click();
      await evidencia(page, "resumo");
      await assistente(page).getByRole("button", { name: "Confirmar Agendamento", exact: true }).click();
      await expect(assistente(page).getByRole("heading", { name: "Agendamento confirmado!" })).toBeFocused();
      await evidencia(page, "sucesso");
      await expect(assistente(page).getByRole("link", { name: "Adicionar ao calendário" })).toHaveAttribute("href", /ics$/);
    });
  }
}
