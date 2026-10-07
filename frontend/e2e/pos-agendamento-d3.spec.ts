import { test, expect, type Page, type Route } from "@playwright/test";

const acao = { permitido: true, codigoMotivo: null, motivo: null };
const detalhe = { id: "local", nomeNegocio: "Acme", local: "Rua local", inicio: "2026-10-08T12:00:00Z", fim: "2026-10-08T12:30:00Z", total: 45, status: "Agendado", servicos: ["Corte"], fuso: "America/Sao_Paulo", duracaoMinutos: 30, profissional: { id: "p", nome: "Diego Lima" }, servicosDetalhe: [{ servicoId: "s", nome: "Corte", duracaoMinutos: 30, preco: 45 }], regras: { antecedenciaMinimaHoras: 2, limiteParaAlterarEm: "2026-10-08T10:00:00Z" }, acoes: { cancelar: acao, remarcar: acao } };
const slot = "2026-10-09T12:15:00+00:00";
type Opcoes = { detalhe?: object; disponibilidade?: (r: Route, n: number) => Promise<void>; post?: (r: Route, n: number) => Promise<void>; consulta?: (r: Route, n: number) => Promise<void> };
async function preparar(page: Page, op: Opcoes = {}) {
  let gets = 0, slots = 0;
  const posts: { corpo: unknown; prefer: string | undefined }[] = [];
  const erros: string[] = [];
  page.on("pageerror", e => erros.push(e.message));
  await page.route("**/api/publico/**", async r => {
    const p = r.request(), u = new URL(p.url());
    if (u.pathname.endsWith("/negocio")) return r.fulfill({ json: { fuso: detalhe.fuso } });
    if (u.pathname.endsWith("/horarios-livres")) {
      slots++;
      if (op.disponibilidade) return op.disponibilidade(r, slots);
      return r.fulfill({ json: disponibilidade(u.searchParams.get("data")!) });
    }
    if (p.method() === "POST") {
      posts.push({ corpo: p.postDataJSON(), prefer: p.headers().prefer });
      if (op.post) return op.post(r, posts.length);
      return r.fulfill({ json: { ...detalhe, inicio: slot, status: u.pathname.endsWith("/cancelar") ? "Cancelado" : "Agendado" } });
    }
    gets++;
    if (op.consulta) return op.consulta(r, gets);
    return r.fulfill({ json: op.detalhe ?? detalhe });
  });
  await page.goto("/agendamentos/d3-local");
  await expect(page.getByRole("heading", { name: "Acme", exact: true })).toBeVisible();
  return { posts, gets: () => gets, slots: () => slots, erros };
}
function disponibilidade(data: string, horarios = [slot]) { return { data, horarios, fuso: detalhe.fuso, profissionalId: "p", duracaoMinutos: 30 }; }
async function abrir(page: Page) { await page.getByRole("button", { name: "Remarcar agendamento", exact: true }).click(); }
async function revisar(page: Page) {
  await abrir(page);
  await page.getByLabel("Data da remarcação").fill("2026-10-09");
  await page.getByRole("button", { name: "09:15", exact: true }).click();
  await page.getByRole("button", { name: "Revisar remarcação" }).click();
}
const confirmar = (page: Page) => page.getByRole("button", { name: "Confirmar remarcação", exact: true });
test.use({ serviceWorkers: "block" });

test("capabilities permitidas, contexto real, serviços e limite", async ({ page }) => {
  await preparar(page);
  await expect(page.getByText("Profissional: Diego Lima")).toBeVisible();
  await expect(page.getByText("Duração: 30 minutos")).toBeVisible();
  await expect(page.getByText("Corte · 30 min · R$ 45,00")).toBeVisible();
  await expect(page.getByText(/Limite para alterações:.*07:00/)).toBeVisible();
  await expect(page.getByRole("button", { name: "Cancelar agendamento" })).toBeEnabled();
});
for (const tipo of ["cancelar", "remarcar"] as const) test(`${tipo} bloqueado respeita motivo canônico`, async ({ page }) => {
  await preparar(page, { detalhe: { ...detalhe, acoes: { ...detalhe.acoes, [tipo]: { permitido: false, codigoMotivo: "antecedencia_minima", motivo: "Prazo para alteração encerrado." } } } });
  await expect(page.getByRole("button", { name: tipo === "cancelar" ? "Cancelar agendamento" : "Remarcar agendamento" })).toHaveCount(0);
  await expect(page.getByText(/Prazo para alteração encerrado/)).toBeVisible();
});
for (const status of ["Cancelado", "Concluido", "Faltou", "EmAtendimento", "Expirado"]) test(`${status}: capabilities são autoridade`, async ({ page }) => {
  await preparar(page, { detalhe: { ...detalhe, status, acoes: { cancelar: { permitido: false }, remarcar: { permitido: false } } } });
  await expect(page.locator('button[aria-controls="remarcacao"]')).toHaveCount(0);
});
test("campos novos ausentes falham seguros sem heurística", async ({ page }) => {
  await preparar(page, { detalhe: { id: "local", nomeNegocio: "Acme", local: "Rua local", inicio: detalhe.inicio, fim: detalhe.fim, total: 45, status: "Agendado", servicos: ["Corte"] } });
  await expect(page.getByRole("status")).toContainText("não estão disponíveis");
  await expect(page.getByText(/^Profissional:/)).toHaveCount(0);
});
test("status não substitui capability verdadeira fornecida pelo servidor", async ({ page }) => {
  await preparar(page, { detalhe: { ...detalhe, status: "EmAtendimento" } });
  await expect(page.getByRole("button", { name: "Remarcar agendamento" })).toBeEnabled();
});
for (const timezoneId of ["Europe/Lisbon", "America/New_York"]) test.describe(timezoneId, () => {
  test.use({ timezoneId });
  test("slot segue fuso do estabelecimento independentemente do navegador", async ({ page }) => {
    const mock = await preparar(page); await revisar(page); await confirmar(page).click();
    await expect(page.getByRole("status")).toHaveText("Agendamento remarcado.");
    expect(mock.posts[0].corpo).toEqual({ novoInicio: slot });
  });
});
test("Tab e Shift+Tab mantêm navegação; Enter seleciona slot", async ({ page }) => {
  await preparar(page); await abrir(page);
  await expect(page.getByRole("button", { name: "09:15" })).toBeVisible();
  // O campo date nativo possui segmentos internos focáveis no Chromium.
  for (let passo = 0; passo < 8 && !await page.getByRole("button", { name: "09:15" }).evaluate(el => el === document.activeElement); passo++) await page.keyboard.press("Tab");
  await expect(page.getByRole("button", { name: "09:15" })).toBeFocused();
  await page.keyboard.press("Enter"); await expect(page.getByRole("button", { name: "09:15" })).toHaveAttribute("aria-pressed", "true");
  await page.keyboard.press("Shift+Tab"); await expect(page.getByLabel("Data da remarcação")).toBeFocused();
});
test("cancelamento em andamento bloqueia remarcação e anuncia sucesso", async ({ page }) => {
  let liberar!: () => void; const espera = new Promise<void>(r => { liberar = r; });
  const mock = await preparar(page, { post: async r => { await espera; await r.fulfill({ json: { ...detalhe, status: "Cancelado", acoes: { cancelar: { permitido: false }, remarcar: { permitido: false } } } }); } });
  await revisar(page);
  page.once("dialog", d => d.accept()); await page.getByRole("button", { name: "Cancelar agendamento" }).click();
  await expect(page.getByRole("button", { name: "Cancelando..." })).toBeDisabled();
  await expect(confirmar(page)).toBeDisabled();
  await expect(page.getByRole("status")).toHaveText("Cancelando agendamento...");
  await expect(page.locator('button[aria-controls="remarcacao"]')).toBeDisabled(); liberar();
  await expect(page.getByRole("status")).toHaveText("Agendamento cancelado."); expect(mock.posts).toHaveLength(1);
});
test("abertura possui label, aria-expanded e foco no campo", async ({ page }) => {
  await preparar(page); await abrir(page);
  await expect(page.getByLabel("Data da remarcação")).toBeFocused();
  await expect(page.locator('button[aria-controls="remarcacao"]')).toHaveAttribute("aria-expanded", "true");
  await expect(page.getByRole("button", { name: "09:15" })).toBeVisible();
});
test("loading disponibilidade e nenhum slot stale", async ({ page }) => {
  let liberar!: () => void; const espera = new Promise<void>(r => { liberar = r; });
  await preparar(page, { disponibilidade: async r => { await espera; await r.fulfill({ json: disponibilidade("2026-10-08") }); } });
  await abrir(page);
  await expect(page.getByRole("status")).toHaveText("Consultando horários...");
  await expect(page.getByRole("button", { name: "Revisar remarcação" })).toBeDisabled(); liberar();
  await expect(page.getByRole("button", { name: "09:15" })).toBeVisible();
});
test("vazio é distinto de erro", async ({ page }) => {
  await preparar(page, { disponibilidade: r => r.fulfill({ json: disponibilidade("2026-10-08", []) }) }); await abrir(page);
  await expect(page.getByRole("status")).toContainText("Nenhum horário disponível");
  await expect(page.getByRole("main").getByRole("alert")).toHaveCount(0);
});
test("erro disponibilidade, anúncio e retry recuperam", async ({ page }) => {
  await preparar(page, { disponibilidade: (r, n) => n === 1 ? r.fulfill({ status: 503, body: "" }) : r.fulfill({ json: disponibilidade("2026-10-08") }) }); await abrir(page);
  await expect(page.getByRole("main").getByRole("alert")).toContainText("Não foi possível consultar");
  await page.getByRole("button", { name: "Tentar novamente horários" }).click();
  await expect(page.getByRole("button", { name: "09:15" })).toBeVisible();
});
test("disponibilidade malformada é erro recuperável, não quebra a página", async ({ page }) => {
  const mock = await preparar(page, { disponibilidade: r => r.fulfill({ json: disponibilidade("2026-10-08", ["inválido"]) }) }); await abrir(page);
  await expect(page.getByRole("main").getByRole("alert")).toContainText("Não foi possível consultar"); expect(mock.erros).toEqual([]);
});
test("ação com erro sem corpo usa mensagem humana", async ({ page }) => {
  await preparar(page, { post: r => r.fulfill({ status: 503, body: "" }) }); await revisar(page); await confirmar(page).click();
  await expect(page.getByRole("main").getByRole("alert")).toHaveText("Não foi possível concluir a solicitação. Tente novamente.");
});
test("troca de data invalida seleção e ignora resposta tardia", async ({ page }) => {
  let liberar!: () => void; const espera = new Promise<void>(r => { liberar = r; });
  await preparar(page, { disponibilidade: async r => {
    const data = new URL(r.request().url()).searchParams.get("data")!;
    if (data === "2026-10-10") await espera;
    await r.fulfill({ json: disponibilidade(data, data === "2026-10-10" ? ["2026-10-10T13:00:00Z"] : [slot]) });
  } }); await abrir(page);
  await page.getByRole("button", { name: "09:15" }).click();
  await page.getByLabel("Data da remarcação").fill("2026-10-10");
  await expect(page.getByRole("button", { name: "09:15" })).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Revisar remarcação" })).toBeDisabled();
  await page.getByLabel("Data da remarcação").fill("2026-10-11");
  await expect(page.getByRole("button", { name: "09:15" })).toBeVisible(); liberar();
  await expect(page.getByRole("button", { name: "10:00" })).toHaveCount(0);
});
test("seleção por teclado, timezone, revisão e retorno de foco", async ({ page }) => {
  const mock = await preparar(page); await abrir(page);
  const horario = page.getByRole("button", { name: "09:15" }); await horario.focus(); await page.keyboard.press("Space");
  await expect(horario).toHaveAttribute("aria-pressed", "true");
  await page.getByRole("button", { name: "Revisar remarcação" }).click();
  await expect(page.getByRole("heading", { name: "Confirme a remarcação" })).toBeFocused();
  await expect(page.getByText(/Novo horário:.*09:15/)).toBeVisible(); expect(mock.posts).toHaveLength(0);
  await page.getByRole("button", { name: "Voltar à seleção" }).click(); await expect(page.getByLabel("Data da remarcação")).toBeFocused();
});
test("POST preserva slot canônico e Prefer; 200 sem GET extra, foco e sucesso", async ({ page }) => {
  const mock = await preparar(page); await revisar(page); await confirmar(page).click();
  await expect(page.getByRole("status")).toHaveText("Agendamento remarcado.");
  expect(mock.posts).toEqual([{ corpo: { novoInicio: slot }, prefer: "return=representation" }]); expect(mock.gets()).toBe(1);
  await expect(page.getByRole("heading", { name: "Acme" })).toBeFocused();
});
test("204 reconcilia com GET", async ({ page }) => {
  const mock = await preparar(page, { post: r => r.fulfill({ status: 204, body: "" }) }); await revisar(page); await confirmar(page).click();
  await expect(page.getByRole("status")).toHaveText("Agendamento remarcado."); expect(mock.gets()).toBe(2);
});
test("204 com GET falho mantém aceitação e retry somente GET", async ({ page }) => {
  const mock = await preparar(page, { post: r => r.fulfill({ status: 204, body: "" }), consulta: (r, n) => n === 2 ? r.fulfill({ status: 503, body: "" }) : r.fulfill({ json: detalhe }) }); await revisar(page); await confirmar(page).click();
  await expect(page.getByRole("status")).toContainText("foi aceita"); await expect(page.getByText("Detalhes aguardando atualização.")).toBeVisible();
  await page.getByRole("button", { name: "Atualizar detalhes" }).click(); await expect(page.getByRole("status")).toHaveText("Agendamento remarcado."); expect(mock.posts).toHaveLength(1);
});
test("409 conflito reconsulta e permite recuperar sem sugestões stale", async ({ page }) => {
  const mock = await preparar(page, { post: (r, n) => n === 1 ? r.fulfill({ status: 409, json: { codigo: "horario_indisponivel", title: "Conflito", proximosHorariosLivres: ["2026-10-09T15:00:00Z"] } }) : r.fulfill({ json: { ...detalhe, inicio: slot } }) });
  await revisar(page); await confirmar(page).click(); await expect(page.getByRole("main").getByRole("alert")).toContainText("acabou de ficar indisponível");
  await expect(page.getByRole("button", { name: "Revisar remarcação" })).toBeDisabled(); expect(mock.slots()).toBeGreaterThan(1);
  await page.getByRole("button", { name: "09:15" }).click(); await page.getByRole("button", { name: "Revisar remarcação" }).click(); await confirmar(page).click(); await expect(page.getByRole("status")).toHaveText("Agendamento remarcado.");
});
test("double-submit e loading bloqueiam ações concorrentes", async ({ page }) => {
  let liberar!: () => void; const espera = new Promise<void>(r => { liberar = r; });
  const mock = await preparar(page, { post: async r => { await espera; await r.fulfill({ json: detalhe }); } }); await revisar(page);
  await confirmar(page).evaluate(el => { (el as HTMLButtonElement).click(); (el as HTMLButtonElement).click(); });
  await expect(page.getByRole("button", { name: "Remarcando..." })).toBeDisabled(); await expect(page.getByRole("button", { name: "Cancelar agendamento" })).toBeDisabled(); expect(mock.posts).toHaveLength(1); liberar(); await expect(page.getByRole("status")).toHaveText("Agendamento remarcado.");
});
test("cancelamento exige confirmação e usa representação canônica", async ({ page }) => {
  const mock = await preparar(page); page.once("dialog", d => d.dismiss()); await page.getByRole("button", { name: "Cancelar agendamento" }).click(); expect(mock.posts).toHaveLength(0);
  page.once("dialog", d => d.accept()); await page.getByRole("button", { name: "Cancelar agendamento" }).click(); await expect(page.getByText("Status: Cancelado")).toBeVisible(); await expect(page.getByRole("status")).toHaveText("Agendamento cancelado."); expect(mock.gets()).toBe(1);
});
for (const codigo of ["antecedencia_minima", "status_nao_permite", "nao_permitido", "horario_invalido", "data_invalida"]) test(`${codigo}: mensagem canônica sem código técnico`, async ({ page }) => {
  await preparar(page, { post: r => r.fulfill({ status: 400, json: { codigo, title: "Não é possível realizar esta alteração." } }) }); await revisar(page); await confirmar(page).click();
  await expect(page.getByRole("main").getByRole("alert")).toHaveText("Não é possível realizar esta alteração."); await expect(page.getByText(codigo, { exact: true })).toHaveCount(0);
});
for (const largura of [375, 390, 430, 768, 1024, 1440]) for (const tema of ["light", "dark"]) test(`${largura}px ${tema}: touch, reduced motion, seleção e ausência de overflow`, async ({ page }) => {
  await page.setViewportSize({ width: largura, height: 900 }); await page.emulateMedia({ reducedMotion: "reduce" }); await page.addInitScript(t => localStorage.setItem("tema", t), tema);
  const mock = await preparar(page); await abrir(page);
  const horario = page.getByRole("button", { name: "09:15" });
  const fundoNormal = await horario.evaluate(el => getComputedStyle(el).backgroundColor);
  await horario.click();
  expect(await horario.evaluate(el => getComputedStyle(el).backgroundColor)).not.toBe(fundoNormal);
  await expect(page.locator("html")).toHaveAttribute("data-theme", tema);
  const medidas = await page.locator("button:visible, input:visible").evaluateAll(els => els.map(el => ({ w: el.getBoundingClientRect().width, h: el.getBoundingClientRect().height })));
  expect(medidas.every(m => m.w >= 44 && m.h >= 44)).toBe(true);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await page.screenshot({ path: test.info().outputPath(`d3-${largura}-${tema}.png`), fullPage: true }); expect(mock.erros).toEqual([]);
});
