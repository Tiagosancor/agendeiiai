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
    test(`C4 ${largura}px ${tema}: jornada e estados de recupera\u00e7\u00e3o`, async ({ page }) => {
      await page.setViewportSize({ width: largura, height: largura < 768 ? 812 : 900 });
      await page.addInitScript(tema => localStorage.setItem("tema", tema), tema);
      const estado = await preparar(page);
      await expect(page.locator("html")).toHaveAttribute("data-theme", tema);
      const origem = page.getByRole("button", { name: "Agendar", exact: true });
      await expect(origem).toBeEnabled();
      await expect(page.locator(".pagina-hero-nome")).toBeVisible();
      await capturar(page, "pagina", true);
      await origem.click();
      await capturar(page, "profissionais");
      await assistente(page).getByRole("button", { name: /Jo\u00e3o Teste/ }).click();
      await continuar(page).click();
      const servico = assistente(page).getByRole("button", { name: /Corte Teste/ });
      await servico.click();
      await expect(servico).toHaveAttribute("aria-pressed", "true");
      await capturar(page, "servicos");
      await continuar(page).click();
      await expect(dias(page)).toHaveCount(14);
      await expect(dias(page).first()).toContainText("out");
      await assistente(page).getByRole("button", { name: "09:00", exact: true }).click();
      await capturar(page, "data-horario");
      await continuar(page).click();
      await capturar(page, "identificacao");
      await assistente(page).getByLabel("Nome completo", { exact: true }).fill("Cliente Teste");
      await assistente(page).getByLabel("WhatsApp com DDD").fill("71988887777");
      await assistente(page).getByRole("checkbox").check();
      await assistente(page).getByRole("button", { name: "Enviar c\u00f3digo", exact: true }).click();
      await expect(assistente(page).getByText("C\u00f3digo enviado", { exact: true })).toBeVisible();
      await expect(assistente(page).getByText(/WhatsApp \u2022\u2022\u2022\u2022 7777/)).toBeVisible();
      await capturar(page, "codigo");
      await assistente(page).getByLabel("C\u00f3digo de confirma\u00e7\u00e3o", { exact: true }).fill("123456");
      await assistente(page).getByRole("button", { name: "Confirmar c\u00f3digo", exact: true }).click();
      await capturar(page, "resumo");
      await assistente(page).getByRole("button", { name: "Confirmar Agendamento", exact: true }).click();
      await expect(assistente(page).getByRole("heading", { name: "Agendamento confirmado!" })).toBeFocused();
      for (const valor of ["Corte Teste", "Jo\u00e3o Teste", "09:00", "R$ 45,00"]) await expect(assistente(page).locator("dl")).toContainText(valor);
      await capturar(page, "sucesso");
      await assistente(page).getByRole("button", { name: "Fechar", exact: true }).last().click();
      await expect(origem).toBeFocused();
      await origem.click();
      await assistente(page).getByRole("button", { name: /Maria sem servi\u00e7os/ }).click();
      await continuar(page).click();
      await expect(assistente(page).getByText("Nenhum servi\u00e7o dispon\u00edvel para essa escolha.")).toBeVisible();
      await capturar(page, "profissional-sem-servico");
      await assistente(page).getByRole("button", { name: "Escolher outro profissional" }).click();
      await assistente(page).getByRole("button", { name: /Jo\u00e3o Teste/ }).click();
      await continuar(page).click();
      await assistente(page).getByRole("button", { name: /Corte Teste/ }).click();
      await page.route("**/api/publico/horarios-livres?**", rota => rota.fulfill({ status: 503, json: {} }));
      await continuar(page).click();
      await expect(assistente(page).getByRole("alert")).toContainText("N\u00e3o foi poss\u00edvel consultar os hor\u00e1rios.");
      await capturar(page, "disponibilidade-erro");
      await page.unroute("**/api/publico/horarios-livres?**");
      await page.route("**/api/publico/horarios-livres?**", rota => rota.fulfill({ json: [] }));
      await assistente(page).getByRole("button", { name: "Tentar novamente", exact: true }).click();
      await expect(assistente(page).getByRole("status")).toContainText("Nenhum hor\u00e1rio livre neste dia");
      await capturar(page, "sem-horario");
      await page.keyboard.press("Escape");
      await page.route("**/api/publico/servicos", rota => rota.fulfill({ status: 503, json: {} }));
      await page.reload();
      await expect(page.locator(".pagina-servicos").getByRole("alert")).toContainText("N\u00e3o foi poss\u00edvel carregar");
      await capturar(page, "catalogo-erro", true);
      expect(estado.erros).toEqual([]);
    });
  }
}

for (const tema of ["light", "dark"]) {
  test(`C4 identidade configurada e contato ${tema}`, async ({ page, request }) => {
    const login = await request.post("http://localhost:5080/painel/auth/login", { data: { email: "admin@acme.dev", senha: "Admin!123" } });
    expect(login.ok()).toBe(true);
    const headers = { Authorization: `Bearer ${(await login.json()).accessToken}` };
    const perfilResposta = await request.get("http://localhost:5080/painel/negocio", { headers });
    expect(perfilResposta.ok()).toBe(true);
    const perfil = await perfilResposta.json();
    const cor = tema === "light" ? "#fef08a" : "#172554";
    try {
      const alteracao = await request.put("http://localhost:5080/painel/negocio", { headers, data: { ...perfil, corPrimaria: cor, corFundo: "#123456" } });
      expect(alteracao.status()).toBe(204);
      await page.addInitScript(tema => localStorage.setItem("tema", tema), tema);
      await preparar(page);
      for (const largura of [390, 1440]) {
        await page.setViewportSize({ width: largura, height: 900 });
        const cta = page.getByRole("button", { name: "Agendar Agora", exact: true });
        await expect(cta).toBeEnabled();
        // Aguarda o fim da transição entre catálogo indisponível e CTA ativo.
        await expect(cta).toHaveCSS("background-color", tema === "light" ? "rgb(254, 240, 138)" : "rgb(23, 37, 84)");
        const cores = await cta.evaluate(el => {
          const estilo = getComputedStyle(el);
          const luminancia = (valor: string) => {
            const canais = valor.match(/\d+/g)!.slice(0, 3).map(n => {
              const canal = Number(n) / 255;
              return canal <= .04045 ? canal / 12.92 : ((canal + .055) / 1.055) ** 2.4;
            });
            return canais[0] * .2126 + canais[1] * .7152 + canais[2] * .0722;
          };
          const a = luminancia(estilo.color), b = luminancia(estilo.backgroundColor);
          return { contraste: (Math.max(a, b) + .05) / (Math.min(a, b) + .05), fundo: estilo.backgroundColor };
        });
        expect(cores.contraste).toBeGreaterThanOrEqual(4.5);
        expect(cores.fundo).toBe(tema === "light" ? "rgb(254, 240, 138)" : "rgb(23, 37, 84)");
        await expect(page.getByTestId("pagina-negocio")).toHaveCSS("background-color", "rgb(18, 52, 86)");
        await capturar(page, `identidade-${largura}`, true);
        await cta.click();
        await expect(assistente(page)).toHaveCSS("background-color", "rgb(18, 52, 86)");
        await capturar(page, `identidade-assistente-${largura}`);
        await page.keyboard.press("Escape");
      }
      const contatos: unknown[] = [];
      await page.route("**/api/publico/contato", rota => {
        contatos.push(rota.request().postDataJSON());
        return rota.fulfill({ status: 202, body: "" });
      });
      const formulario = page.locator(".pagina-contato");
      await formulario.getByLabel("Nome", { exact: true }).fill("Cliente Teste");
      await formulario.getByLabel("Telefone", { exact: true }).fill("71988887777");
      await formulario.getByLabel("E-mail", { exact: true }).fill("cliente@example.test");
      await formulario.getByLabel("Mensagem", { exact: true }).fill("Mensagem de teste local");
      await formulario.getByRole("button", { name: "Enviar", exact: true }).click();
      await expect(formulario.getByRole("status")).toHaveText("Mensagem enviada!");
      expect(contatos).toEqual([{ nome: "Cliente Teste", telefone: "71988887777", email: "cliente@example.test", mensagem: "Mensagem de teste local" }]);
      await expect(formulario.getByLabel("Nome", { exact: true })).toHaveValue("");
    } finally {
      const restauracao = await request.put("http://localhost:5080/painel/negocio", { headers, data: perfil });
      expect(restauracao.status()).toBe(204);
    }
  });
}
