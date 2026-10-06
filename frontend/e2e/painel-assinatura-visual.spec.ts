import { test, expect, type Page, type APIRequestContext } from "@playwright/test";
import type { DetalheAssinatura, EstadoAssinatura, PlanoPublico } from "../src/lib/tipos";
import { ROTULOS_ESTADO_ASSINATURA } from "../src/lib/tipos";

const BASE = "http://app.agendeiiai.localhost:3000";
const API = "http://localhost:5080";
// O cache PWA não pode contornar a interceptação das chamadas financeiras.
test.use({ serviceWorkers: "block" });
const cobranca = { link: "https://pagamento.example.test/fatura", chavePix: null, whatsAppContato: null, texto: "Cobrança simulada para teste local." };

async function entrar(page: Page, tema = "light") {
  await page.addInitScript(tema => localStorage.setItem("tema", tema), tema);
  await page.goto(`${BASE}/painel/login`);
  await page.getByLabel("E-mail").fill("admin@acme.dev");
  await page.getByLabel("Senha", { exact: true }).fill("Admin!123");
  await page.getByRole("button", { name: "Entrar", exact: true }).click();
  await expect(page).toHaveURL(/\/painel$/);
}

async function catalogo(request: APIRequestContext): Promise<PlanoPublico[]> {
  const resposta = await request.get(`${API}/cadastro/planos`);
  expect(resposta.ok()).toBe(true);
  return resposta.json();
}

function detalhe(plano: PlanoPublico, estado: EstadoAssinatura = "EmTeste"): DetalheAssinatura {
  // Datas e histórico são fixtures exclusivamente do teste. Preços/faixas vêm do catálogo local real.
  return { planoId: plano.id, plano: plano.nome, periodicidade: "Mensal", estado,
    precoMensalTravado: plano.precoMensal, valorDoPeriodo: plano.precoMensal,
    fimTeste: "2099-10-25T12:00:00Z", proximoVencimento: estado === "EmTeste" ? null : "2099-11-25T12:00:00Z",
    carenciaAte: ["Atrasada", "Suspensa"].includes(estado) ? "2099-11-30T12:00:00Z" : null,
    profissionaisAtivos: 1, maximoProfissionais: plano.maximoProfissionais,
    cobrancas: estado === "EmTeste" ? [] : [{ pagoEm: "2099-10-25T12:00:00Z", valor: plano.precoMensal,
      forma: "Pix", periodoInicio: "2099-10-25T12:00:00Z", periodoFim: "2099-11-25T12:00:00Z", origem: "Gateway", estornadaEm: estado === "Atrasada" ? "2099-10-26T12:00:00Z" : null }],
    pagamentoAutomatico: true, contratada: estado !== "EmTeste", documentoTitular: "***.982.247-**", cancelamentoAte: null };
}

for (const tema of ["light", "dark"]) {
  test(`Assinatura: estados reais, valores e responsividade ${tema}`, async ({ page, request }, testInfo) => {
    test.setTimeout(120_000);
    const planos = await catalogo(request);
    let atual = detalhe(planos[0]);
    const erros: string[] = [];
    page.on("pageerror", erro => erros.push(erro.message));
    // Toda chamada desta área é interceptada; nenhum endpoint financeiro real é executado.
    await page.route(`${API}/painel/assinatura**`, async route => {
      const path = new URL(route.request().url()).pathname;
      if (route.request().method() !== "GET") throw new Error("Ação financeira não prevista neste teste visual");
      await route.fulfill(path.endsWith("/aviso") ? { status: 204 } : { json: atual });
    });
    await entrar(page, tema);
    for (const estado of Object.keys(ROTULOS_ESTADO_ASSINATURA) as EstadoAssinatura[]) {
      atual = detalhe(planos[0], estado);
      await page.goto(`${BASE}/painel/assinatura`);
      await expect(page.getByTestId("estado-assinatura")).toHaveText(ROTULOS_ESTADO_ASSINATURA[estado]);
      await expect(page.locator(".painel-assinatura-plano-atual")).toContainText(planos[0].nome);
      await expect(page.locator(".painel-assinatura-preco strong")).toHaveText(new Intl.NumberFormat("pt-BR", { style: "currency", currency: "BRL" }).format(atual.precoMensalTravado));
      await expect(page.getByRole("button", { name: "Cancelar assinatura", exact: true })).toHaveCount(estado === "Cancelada" ? 0 : 1);
      for (const width of [375, 390, 430, 768, 1024, 1440]) {
        await page.setViewportSize({ width, height: 932 });
        expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `${estado} ${width}px`).toBe(true);
        for (const controle of await page.locator(".painel-assinatura button").all()) {
          const caixa = (await controle.boundingBox())!;
          expect(caixa.height).toBeGreaterThanOrEqual(44);
          expect(caixa.x).toBeGreaterThanOrEqual(0);
          expect(caixa.x + caixa.width).toBeLessThanOrEqual(width);
        }
        if (width === 390 || width === 1440) await page.screenshot({ path: testInfo.outputPath(`assinatura-${estado}-${tema}-${width}.png`), fullPage: true });
      }
      if (estado === "EmTeste") await expect(page.getByText("Nenhuma cobrança registrada ainda.")).toBeVisible();
      else await expect(page.getByRole("table", { name: "Histórico de cobranças" })).toBeVisible();
    }
    expect(erros).toEqual([]);
  });
}

test("Assinatura: contratação, plano, confirmação e cancelamento com API inteiramente simulada", async ({ page, request }, testInfo) => {
  const planos = await catalogo(request);
  let atual = detalhe(planos[0]);
  const chamadas: { path: string; corpo: unknown }[] = [];
  await page.route(`${API}/painel/assinatura**`, async route => {
    const req = route.request(), path = new URL(req.url()).pathname;
    if (path.endsWith("/aviso")) return route.fulfill({ status: 204 });
    if (req.method() === "GET") return route.fulfill({ json: path.endsWith("/instrucoes-pagamento") ? cobranca : atual });
    chamadas.push({ path, corpo: req.postData() ? req.postDataJSON() : null });
    if (path.endsWith("/contratar")) {
      atual = { ...atual, contratada: true, periodicidade: "Anual" };
      return route.fulfill({ json: cobranca });
    }
    if (path.endsWith("/plano")) {
      atual = { ...atual, planoId: planos[1].id, plano: planos[1].nome, periodicidade: "Mensal" };
      return route.fulfill({ status: 204 });
    }
    if (path.endsWith("/cancelar")) {
      atual = { ...atual, cancelamentoAte: atual.fimTeste };
      return route.fulfill({ status: 200, json: { imediato: false } });
    }
    throw new Error(`Endpoint financeiro inesperado: ${path}`);
  });
  await entrar(page);
  await page.goto(`${BASE}/painel/assinatura`);
  const contratar = page.getByRole("button", { name: "Contratar e gerar cobrança", exact: true });
  await expect(contratar).toBeDisabled();
  const radios = page.locator('.painel-assinatura input[type="radio"]');
  await radios.first().focus();
  await page.keyboard.press("ArrowRight");
  await expect(radios.nth(1)).toBeChecked();
  await page.keyboard.press("ArrowLeft");
  await expect(radios.first()).toBeChecked();
  await page.getByRole("button", { name: "Anual", exact: true }).click();
  await expect(page.locator(".painel-assinatura-valor-plano").first()).toContainText(new Intl.NumberFormat("pt-BR", { style: "currency", currency: "BRL" }).format(planos[0].precoAnualPorMes));
  await page.getByLabel("CPF ou CNPJ de quem paga").fill("52998224725");
  await contratar.click();
  await expect(page.getByTestId("instrucoes-pagamento")).toContainText(cobranca.texto);
  expect(chamadas[0]).toEqual({ path: "/painel/assinatura/contratar", corpo: { planoId: planos[0].id, periodicidade: "Anual", cpfCnpj: "52998224725" } });
  await expect(page.getByRole("link", { name: "Pagar agora" })).toHaveAttribute("href", cobranca.link);
  await expect(page.getByRole("link", { name: "Pagar agora" })).toHaveAttribute("rel", "noopener noreferrer");
  await page.getByRole("button", { name: "Mensal", exact: true }).click();
  await page.locator(".painel-assinatura-opcao").nth(1).click();
  await page.getByRole("button", { name: "Trocar plano", exact: true }).click();
  await expect(page.locator(".painel-assinatura").getByRole("alert")).toHaveText("Plano atualizado.");
  expect(chamadas[1]).toEqual({ path: "/painel/assinatura/plano", corpo: { planoId: planos[1].id, periodicidade: "Mensal" } });
  await expect(page.getByRole("button", { name: "Trocar plano", exact: true })).toBeDisabled();
  await page.getByRole("button", { name: "Ver cobrança em aberto", exact: true }).click();
  await expect(page.getByTestId("instrucoes-pagamento")).toBeVisible();
  page.once("dialog", dialog => { expect(dialog.message()).toContain("nenhuma cobrança nova será gerada"); void dialog.dismiss(); });
  await page.getByRole("button", { name: "Cancelar assinatura", exact: true }).click();
  expect(chamadas).toHaveLength(2);
  page.once("dialog", dialog => { void dialog.accept(); });
  await page.getByRole("button", { name: "Cancelar assinatura", exact: true }).click();
  await expect(page.locator(".painel-assinatura").getByRole("alert")).toHaveText("Cancelamento registrado.");
  expect(chamadas[2]).toEqual({ path: "/painel/assinatura/cancelar", corpo: null });
  await expect(page.getByRole("status")).toContainText("Cancelamento pedido:");
  await expect(page.getByRole("button", { name: "Contratar e gerar cobrança", exact: true })).toBeVisible();
  await page.screenshot({ path: testInfo.outputPath("assinatura-cancelamento-agendado.png"), fullPage: true });
});

test("Assinatura: instruções manuais, loading, erro e acesso negado preservados", async ({ page, request }, testInfo) => {
  const planos = await catalogo(request);
  let modo: "manual" | "erro" | "negado" | "loading" = "manual";
  const atual = { ...detalhe(planos[0], "Ativa"), pagamentoAutomatico: false, contratada: false, proximoVencimento: null };
  await page.route(`${API}/painel/assinatura**`, async route => {
    const path = new URL(route.request().url()).pathname;
    if (path.endsWith("/aviso")) return route.fulfill({ status: 204 });
    if (path.endsWith("/instrucoes-pagamento")) {
      if (modo === "erro") return route.fulfill({ status: 502, json: { detail: "Serviço de pagamento indisponível." } });
      return route.fulfill({ json: { ...cobranca, link: null, chavePix: "pix-teste@example.test", whatsAppContato: "5511999999999" } });
    }
    if (modo === "negado") return route.fulfill({ status: 403, json: { detail: "Sem permissão." } });
    if (modo === "loading") return route.abort();
    if (route.request().method() !== "GET") throw new Error("Este teste não executa mutações");
    return route.fulfill({ json: atual });
  });
  await entrar(page, "dark");
  await page.setViewportSize({ width: 390, height: 932 });
  await page.goto(`${BASE}/painel/assinatura`);
  await expect(page.getByText("Sem vencimento", { exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Assinar agora", exact: true }).click();
  await expect(page.getByText("pix-teste@example.test")).toBeVisible();
  await expect(page.getByRole("button", { name: "Copiar", exact: true })).toBeVisible();
  await expect(page.getByRole("link", { name: "Enviar comprovante pelo WhatsApp" })).toHaveAttribute("href", `https://wa.me/5511999999999?text=${encodeURIComponent(`Quero assinar o plano ${atual.plano}.`)}`);
  await page.screenshot({ path: testInfo.outputPath("assinatura-manual-dark-390.png"), fullPage: true });
  modo = "erro";
  await page.reload();
  await page.getByRole("button", { name: "Assinar agora", exact: true }).click();
  await expect(page.locator(".painel-assinatura").getByRole("alert")).toHaveText("Serviço de pagamento indisponível.");
  modo = "negado";
  await page.reload();
  await expect(page.getByText("Só o administrador do negócio vê e altera a assinatura.")).toBeVisible();
  await expect(page.getByRole("button", { name: "Assinar agora" })).toHaveCount(0);
  modo = "loading";
  await page.reload();
  await expect(page.getByRole("status")).toHaveText("Carregando...");
  await expect(page.getByTestId("estado-assinatura")).toHaveCount(0);
});
