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

test.use({ serviceWorkers: "block", timezoneId: "America/Sao_Paulo" });


async function capturar(page: Page, nome: string, completa = false) {
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  const painel = assistente(page);
  if (await painel.count()) expect(await painel.evaluate(el => el.scrollWidth <= el.clientWidth)).toBe(true);
  const caminho = test.info().outputPath(`${nome}.png`);
  await page.screenshot({ path: caminho, fullPage: completa });
  await test.info().attach(nome, { path: caminho, contentType: "image/png" });
}

for (const largura of [375, 390, 430, 768, 1024, 1440]) {
  for (const tema of ["light", "dark"]) {
    test(`C4.2 ${largura}px ${tema}: ação única, processamento e recuperação`, async ({ page }) => {
      test.setTimeout(60_000);
      await page.setViewportSize({ width: largura, height: largura < 768 ? 812 : 900 });
      await page.addInitScript(tema => localStorage.setItem("tema", tema), tema);
      let catalogoFalhou = true;
      const estado = await preparar(page, { catalogo: rota => rota.fulfill(catalogoFalhou
        ? { status: 503, json: {} } : { json: categorias }) });
      const agendar = page.getByRole("button", { name: "Agendar", exact: true });
      const agendarAgora = page.getByRole("button", { name: "Agendar Agora", exact: true });
      await expect(page.getByRole("button", { name: "Tentar novamente", exact: true })).toBeVisible();
      const indisponivel = await agendar.evaluate(el => {
        const css = getComputedStyle(el);
        return { fundo: css.backgroundColor, sombra: css.boxShadow, filtro: css.filter, opacidade: css.opacity };
      });
      for (const botao of [agendar, agendarAgora]) {
        await expect(botao).toBeDisabled();
        await botao.hover();
        await expect(botao).toHaveCSS("opacity", "1");
        await expect(botao).toHaveCSS("filter", "none");
        await expect(botao).toHaveCSS("cursor", "default");
        await expect(botao).toHaveCSS("background-color", indisponivel.fundo);
      }
      if (largura <= 390) await capturar(page, "catalogo-erro", true);
      catalogoFalhou = false;
      await page.getByRole("button", { name: "Tentar novamente", exact: true }).click();
      await expect(agendar).toBeEnabled();
      await expect(agendarAgora).toBeEnabled();
      await expect(agendar).not.toHaveCSS("background-color", indisponivel.fundo);
      await agendar.hover();
      await expect(agendar).toHaveCSS("filter", "brightness(0.96)");
      await agendar.click();
      if (largura === 768 || largura === 1440) {
        const altura = await assistente(page).locator(".assistente-painel").evaluate(el => el.getBoundingClientRect().height);
        expect(altura).toBeLessThan(700);
        await capturar(page, "painel-conteudo-curto");
      }
      await assistente(page).getByRole("button", { name: /João Teste/ }).click();
      await continuar(page).click();
      await assistente(page).getByRole("button", { name: /Corte Teste/ }).click();
      await continuar(page).click();
      await expect(dias(page)).toHaveCount(14);
      if (largura === 390) {
        const faixa = await assistente(page).locator(".assistente-datas").evaluate(el => {
          const limite = el.getBoundingClientRect().right;
          return Array.from(el.children).some(filho => {
            const r = filho.getBoundingClientRect();
            return r.left < limite && r.right > limite;
          });
        });
        expect(faixa).toBe(true);
        await capturar(page, "datas-continuidade");
      }
      await assistente(page).getByRole("button", { name: "09:00", exact: true }).click();
      await continuar(page).click();
      const rodape = assistente(page).locator(".assistente-rodape");
      const principal = rodape.locator("button.publico-cta");
      await expect(assistente(page).locator(".publico-cta")).toHaveCount(1);
      await expect(principal).toHaveText("Enviar código");
      await expect(principal).toBeDisabled();
      if (largura <= 390) await capturar(page, "identificacao");
      const caixa = await principal.boundingBox();
      expect(caixa!.y + caixa!.height).toBeLessThanOrEqual(largura < 768 ? 812 : 900);
      await assistente(page).getByLabel("Nome completo", { exact: true }).fill("Cliente Teste");
      await assistente(page).getByLabel("WhatsApp com DDD").fill("71988887777");
      await expect(principal).toBeDisabled();
      await assistente(page).getByRole("checkbox").check();
      await expect(principal).toBeEnabled();
      let envio: Route | undefined;
      await page.route("**/api/publico/codigos", rota => { envio = rota; });
      await principal.click();
      await expect(principal).toHaveText("Enviando...");
      await expect(principal).toBeDisabled();
      await expect.poll(() => !!envio).toBe(true);
      await envio!.fulfill({ status: 202, body: "" });
      await page.unroute("**/api/publico/codigos");
      await expect(principal).toHaveText("Confirmar código");
      await expect(principal).toBeDisabled();
      await expect(assistente(page).locator(".publico-cta")).toHaveCount(1);
      const codigo = assistente(page).getByLabel("Código de confirmação", { exact: true });
      await expect(codigo).toBeFocused();
      if (largura <= 390) await capturar(page, "codigo");
      await codigo.fill("123");
      await expect(principal).toBeDisabled();
      await codigo.fill("123456");
      await page.route("**/api/publico/codigos/validar", rota => rota.fulfill({ status: 400, json: {} }));
      await principal.click();
      await expect(assistente(page).getByRole("alert")).toContainText("Código inválido ou expirado");
      await expect(codigo).toHaveAttribute("aria-invalid", "true");
      await expect(codigo).toBeFocused();
      await expect(principal).toHaveText("Confirmar código");
      await expect(assistente(page).getByRole("button", { name: /Reenviar em/ })).toBeDisabled();
      const reenvio = assistente(page).locator(".codigo-acoes button").last();
      for (let segundo = 0; segundo < 60 && await reenvio.isDisabled(); segundo++) {
        const anterior = await reenvio.textContent();
        await page.clock.runFor(1100);
        await expect(reenvio).not.toHaveText(anterior!);
      }
      await expect(assistente(page).getByRole("button", { name: "Reenviar código", exact: true })).toBeEnabled();
      await assistente(page).getByRole("button", { name: "Reenviar código", exact: true }).click();
      await expect(codigo).toHaveValue("");
      await expect(principal).toBeDisabled();
      await codigo.fill("654321");
      await page.unroute("**/api/publico/codigos/validar");
      let validacao: Route | undefined;
      await page.route("**/api/publico/codigos/validar", rota => { validacao = rota; });
      await principal.click();
      await expect(principal).toHaveText("Validando...");
      await expect(principal).toBeDisabled();
      await expect(codigo).toBeDisabled();
      await expect.poll(() => !!validacao).toBe(true);
      await validacao!.fulfill({ json: { tokenVerificacao: "token-local" } });
      await page.unroute("**/api/publico/codigos/validar");
      await expect(principal).toHaveText("Confirmar Agendamento");
      await assistente(page).getByRole("button", { name: "← Voltar" }).click();
      await assistente(page).getByRole("button", { name: "Mudar dados" }).click();
      await expect(principal).toHaveText("Continuar");
      await assistente(page).getByLabel("WhatsApp com DDD").fill("71977776666");
      await expect(principal).toHaveText("Enviar código");
      await expect(assistente(page).getByRole("button", { name: "Confirmar código", exact: true })).toHaveCount(0);
      expect(estado.confirmacoes).toHaveLength(0);
      await principal.click();
      await codigo.fill("123456");
      await principal.click();
      await expect(principal).toHaveText("Confirmar Agendamento");
      await principal.click();
      await expect(assistente(page).getByRole("heading", { name: "Agendamento confirmado!" })).toBeVisible();
      await expect(assistente(page).locator('[aria-label="Progresso"] [class*="bg-(--cor-primaria)"]')).toHaveCount(5);
      if (largura === 390 || largura === 1440) await capturar(page, "sucesso-progresso");
      expect(estado.erros).toEqual([]);
      expect(estado.confirmacoes).toHaveLength(1);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    });
  }
}
