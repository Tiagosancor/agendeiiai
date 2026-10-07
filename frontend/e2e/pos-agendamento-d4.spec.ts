import { test, expect, type Page, type Route } from "@playwright/test";

const permitido = { permitido: true, codigoMotivo: null, motivo: null };
const detalhe = { id: "d4-local", nomeNegocio: "Acme", local: "Rua local", inicio: "2026-10-08T12:00:00Z", fim: "2026-10-08T12:30:00Z", total: 45, status: "Agendado", servicos: ["Corte"], fuso: "America/Sao_Paulo", duracaoMinutos: 30, profissional: { id: "p", nome: "Diego Lima" }, servicosDetalhe: [{ servicoId: "s", nome: "Corte", duracaoMinutos: 30, preco: 45 }], regras: { antecedenciaMinimaHoras: 2, limiteParaAlterarEm: "2026-10-08T10:00:00Z" }, acoes: { cancelar: permitido, remarcar: permitido } };
const slot = "2026-10-09T12:15:00+00:00";
type Opcoes = { detalhe?: object; consulta?: (r: Route, n: number) => Promise<void>; disponibilidade?: (r: Route, n: number) => Promise<void>; post?: (r: Route) => Promise<void>; abrir?: boolean };
async function preparar(page: Page, op: Opcoes = {}) {
  let gets = 0, consultas = 0;
  const posts: unknown[] = [], urls: string[] = [], erros: string[] = [];
  page.on("pageerror", e => erros.push(e.message));
  await page.route("**/api/publico/**", async r => {
    const req = r.request(), u = new URL(req.url()); urls.push(u.pathname);
    if (u.pathname.endsWith("/horarios-livres")) {
      consultas++;
      return op.disponibilidade ? op.disponibilidade(r, consultas) : r.fulfill({ json: { data: u.searchParams.get("data"), horarios: [slot], fuso: detalhe.fuso, profissionalId: "p", duracaoMinutos: 30 } });
    }
    if (req.method() === "POST") {
      posts.push(req.postDataJSON());
      return op.post ? op.post(r) : r.fulfill({ json: { ...detalhe, inicio: slot } });
    }
    gets++;
    return op.consulta ? op.consulta(r, gets) : r.fulfill({ json: op.detalhe ?? detalhe });
  });
  await page.goto("/agendamentos/d4-local");
  if (op.abrir !== false) await expect(page.getByRole("heading", { name: "Acme", exact: true })).toBeVisible();
  return { posts, urls, erros, gets: () => gets, consultas: () => consultas };
}
async function abrir(page: Page) { await page.getByRole("button", { name: "Remarcar agendamento", exact: true }).click(); }
async function revisar(page: Page) {
  await abrir(page); await page.getByLabel("Data da remarcação").fill("2026-10-09");
  await page.getByRole("button", { name: "09:15", exact: true }).click();
  await page.getByRole("button", { name: "Revisar remarcação" }).click();
}
async function semOverflow(page: Page) { expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true); }
test.use({ serviceWorkers: "block" });

for (const width of [375, 390, 430, 768, 1024, 1440]) for (const tema of ["light", "dark"]) test(`D4 ${width}px ${tema}: detalhes, ações e remarcação sem overflow`, async ({ page }) => {
  await page.setViewportSize({ width, height: width < 768 ? 844 : 900 });
  await page.addInitScript(t => localStorage.setItem("tema", t), tema);
  const mock = await preparar(page);
  await expect(page.locator("html")).toHaveAttribute("data-theme", tema);
  await expect(page.getByText("Status: Agendado")).toBeVisible();
  await expect(page.locator(".gestao-hora")).toHaveText("09:00");
  await semOverflow(page);
  if (width === 390) for (const seletor of [".gestao-intro h1", ".gestao-status", ".gestao-data", ".gestao-servicos strong", ".gestao-profissional"]) {
    const box = await page.locator(seletor).boundingBox(); expect(box!.y + box!.height).toBeLessThan(844);
  }
  await page.screenshot({ path: `../docs/qa-d4/compromisso-${width}-${tema}.png`, fullPage: true });
  await abrir(page); await expect(page.getByRole("button", { name: "09:15", exact: true })).toBeVisible();
  await semOverflow(page);
  const alvos = await page.locator("button:visible, input:visible").evaluateAll(els => els.map(el => { const b = el.getBoundingClientRect(); return { w: b.width, h: b.height }; }));
  expect(alvos.every(b => b.w >= 44 && b.h >= 44)).toBe(true);
  await page.screenshot({ path: `../docs/qa-d4/remarcacao-${width}-${tema}.png`, fullPage: true });
  expect(mock.erros).toEqual([]);
});

test("D4 serviços múltiplos preservam nomes, durações e preços", async ({ page }) => {
  await preparar(page, { detalhe: { ...detalhe, total: 80, servicos: ["Corte", "Barba"], servicosDetalhe: [...detalhe.servicosDetalhe, { servicoId: "b", nome: "Barba", duracaoMinutos: 20, preco: 35 }] } });
  await expect(page.getByRole("heading", { name: "Seus serviços" })).toBeVisible();
  await expect(page.getByText("Barba · 20 min · R$ 35,00")).toBeVisible();
  await expect(page.getByText("Total: R$ 80,00")).toBeVisible();
});
test("D4 ausência de profissional não inventa identidade", async ({ page }) => {
  await preparar(page, { detalhe: { ...detalhe, profissional: null } });
  await expect(page.locator(".gestao-profissional")).toHaveCount(0);
});
test("D4 contrato antigo conserva serviços sem detalhamento", async ({ page }) => {
  await preparar(page, { detalhe: { ...detalhe, servicosDetalhe: null } });
  await expect(page.locator(".gestao-servico-nome")).toHaveText("Corte");
});
for (const status of ["Cancelado", "Concluido", "Expirado", "Faltou", "EmAtendimento"]) test(`D4 ${status}: apresentação encerrada usa capabilities canônicas`, async ({ page }) => {
  await preparar(page, { detalhe: { ...detalhe, status, acoes: { remarcar: { permitido: false, motivo: "Alteração não permitida." }, cancelar: { permitido: false, motivo: "Cancelamento não permitido." } } } });
  await expect(page.locator(".gestao-status")).toHaveAttribute("data-estado", status);
  await expect(page.getByRole("button", { name: "Remarcar agendamento" })).toHaveCount(0);
  await expect(page.getByText("Remarcação: Alteração não permitida.")).toBeVisible();
});
test("D4 remarcação bloqueada tem explicação contextual", async ({ page }) => {
  await preparar(page, { detalhe: { ...detalhe, acoes: { ...detalhe.acoes, remarcar: { permitido: false, motivo: "Prazo encerrado." } } } });
  await expect(page.getByText("Remarcação: Prazo encerrado.")).toBeVisible();
  await expect(page.getByRole("button", { name: "Cancelar agendamento" })).toBeEnabled();
});
test("D4 cancelamento bloqueado não impede remarcação permitida", async ({ page }) => {
  await preparar(page, { detalhe: { ...detalhe, acoes: { ...detalhe.acoes, cancelar: { permitido: false, motivo: "Prazo encerrado." } } } });
  await expect(page.getByText("Cancelamento: Prazo encerrado.")).toBeVisible(); await abrir(page);
});
test("D4 link inválido é estado completo sem ação mutável", async ({ page }) => {
  await preparar(page, { abrir: false, consulta: r => r.fulfill({ status: 401, json: { detalhe: "Link inválido" } }) });
  await expect(page.getByRole("main").getByRole("alert")).toContainText("Link inválido ou expirado");
  await expect(page.getByRole("button", { name: "Tentar novamente" })).toHaveCount(0);
});
test("D4 erro temporário permite retry de carga", async ({ page }) => {
  const mock = await preparar(page, { abrir: false, consulta: (r, n) => n === 1 ? r.abort() : r.fulfill({ json: detalhe }) });
  await expect(page.getByRole("main").getByRole("alert")).toContainText("Não foi possível carregar");
  await page.getByRole("button", { name: "Tentar novamente", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Acme" })).toBeVisible(); expect(mock.gets()).toBe(2);
});
test("D4 loading inicial não oferece ações antecipadas", async ({ page }) => {
  let liberar!: () => void; const espera = new Promise<void>(r => { liberar = r; });
  await preparar(page, { abrir: false, consulta: async r => { await espera; await r.fulfill({ json: detalhe }); } });
  await expect(page.getByRole("status")).toHaveText("Carregando...");
  await expect(page.getByRole("button", { name: "Remarcar agendamento" })).toHaveCount(0); liberar();
  await expect(page.getByRole("heading", { name: "Acme" })).toBeVisible();
});
test("D4 faixa de datas seleciona sem restringir campo livre", async ({ page }) => {
  await preparar(page); await abrir(page);
  await page.getByRole("group", { name: "Próximas datas" }).getByRole("button").first().click();
  const escolhido = await page.getByLabel("Data da remarcação").inputValue();
  expect(escolhido).toMatch(/^\d{4}-\d{2}-\d{2}$/);
  await page.getByLabel("Data da remarcação").fill("2026-11-23");
  await expect(page.getByLabel("Data da remarcação")).toHaveValue("2026-11-23");
});
test("D4 disponibilidade vazia difere de erro", async ({ page }) => {
  await preparar(page, { disponibilidade: r => r.fulfill({ json: { data: new URL(r.request().url()).searchParams.get("data"), horarios: [], fuso: detalhe.fuso } }) }); await abrir(page);
  await expect(page.getByRole("status")).toContainText("Nenhum horário disponível");
  await expect(page.getByRole("main").getByRole("alert")).toHaveCount(0);
});
test("D4 erro de slots permite retry preservando data", async ({ page }) => {
  await preparar(page, { disponibilidade: (r, n) => n === 1 ? r.abort() : r.fulfill({ json: { data: new URL(r.request().url()).searchParams.get("data"), horarios: [slot], fuso: detalhe.fuso } }) }); await abrir(page);
  await expect(page.getByRole("main").getByRole("alert")).toContainText("Não foi possível consultar");
  const data = await page.getByLabel("Data da remarcação").inputValue();
  await page.getByRole("button", { name: "Tentar novamente horários" }).click();
  await expect(page.getByRole("button", { name: "09:15", exact: true })).toBeVisible();
  await expect(page.getByLabel("Data da remarcação")).toHaveValue(data);
});
test("D4 seleção possui estado e símbolo além da cor", async ({ page }) => {
  await preparar(page); await abrir(page); const botao = page.getByRole("button", { name: "09:15", exact: true }); await botao.click();
  await expect(botao).toHaveAttribute("aria-pressed", "true");
  expect(await botao.evaluate(el => getComputedStyle(el, "::after").content)).toContain("✓");
});
test("D4 loading de slots anuncia consulta antes de habilitar revisão", async ({ page }) => {
  let liberar!: () => void; const espera = new Promise<void>(r => { liberar = r; });
  await preparar(page, { disponibilidade: async r => { await espera; await r.fulfill({ json: { data: new URL(r.request().url()).searchParams.get("data"), horarios: [slot], fuso: detalhe.fuso } }); } });
  await abrir(page); await expect(page.getByRole("status")).toHaveText("Consultando horários...");
  await expect(page.getByRole("button", { name: "Revisar remarcação" })).toBeDisabled(); liberar();
  await expect(page.getByRole("button", { name: "09:15", exact: true })).toBeVisible();
});
test("D4 processamento bloqueia envio e anuncia resultado", async ({ page }) => {
  let liberar!: () => void; const espera = new Promise<void>(r => { liberar = r; });
  await preparar(page, { post: async r => { await espera; await r.fulfill({ json: { ...detalhe, inicio: slot } }); } }); await revisar(page);
  await page.getByRole("button", { name: "Confirmar novo horário" }).click();
  await expect(page.getByRole("status")).toHaveText("Remarcando agendamento...");
  await expect(page.getByRole("button", { name: "Remarcando...", exact: true })).toBeDisabled(); liberar();
  await expect(page.getByRole("status")).toHaveText("Agendamento remarcado.");
});
test("D4 revisão DE/PARA e voltar preservam seleção sem POST", async ({ page }) => {
  const mock = await preparar(page); await revisar(page);
  await expect(page.locator(".gestao-review-periodo").first()).toContainText("Horário atual:");
  await expect(page.locator('[data-destino="true"]')).toContainText("Novo horário:");
  await page.screenshot({ path: "../docs/qa-d4/revisao.png", fullPage: true });
  await page.getByRole("button", { name: "Voltar à seleção" }).click();
  await expect(page.getByRole("button", { name: "09:15", exact: true })).toHaveAttribute("aria-pressed", "true"); expect(mock.posts).toHaveLength(0);
});
test("D4 confirmação canonical 200 muda compromisso sem GET extra", async ({ page }) => {
  const mock = await preparar(page); await revisar(page); await page.getByRole("button", { name: "Confirmar novo horário" }).click();
  await expect(page.getByRole("status")).toHaveText("Agendamento remarcado.");
  await expect(page.locator(".gestao-hora")).toHaveText("09:15"); expect(mock.gets()).toBe(1); expect(mock.posts).toEqual([{ novoInicio: slot }]);
});
test("D4 conflito anuncia feedback e descarta seleção anterior", async ({ page }) => {
  const mock = await preparar(page, { post: r => r.fulfill({ status: 409, json: { codigo: "horario_indisponivel", detalhe: "Conflito" } }) }); await revisar(page);
  await page.getByRole("button", { name: "Confirmar novo horário" }).click();
  await expect(page.getByRole("main").getByRole("alert")).toContainText("acabou de ficar indisponível");
  await expect(page.getByRole("button", { name: "Revisar remarcação" })).toBeDisabled(); expect(mock.consultas()).toBeGreaterThan(1);
});
test("D4 cancelamento requer confirmação e exibe estado canônico", async ({ page }) => {
  const mock = await preparar(page, { post: r => r.fulfill({ json: { ...detalhe, status: "Cancelado", acoes: { cancelar: { permitido: false }, remarcar: { permitido: false } } } }) });
  page.once("dialog", d => d.dismiss()); await page.getByRole("button", { name: "Cancelar agendamento" }).click(); expect(mock.posts).toHaveLength(0);
  page.once("dialog", d => d.accept()); await page.getByRole("button", { name: "Cancelar agendamento" }).click();
  await expect(page.getByText("Status: Cancelado")).toBeVisible(); await expect(page.getByRole("status")).toHaveText("Agendamento cancelado.");
});
test("D4 foco na data, revisão e retorno pelo teclado", async ({ page }) => {
  await preparar(page); await abrir(page); await expect(page.getByLabel("Data da remarcação")).toBeFocused();
  const botao = page.getByRole("button", { name: "09:15", exact: true }); await botao.focus(); await page.keyboard.press("Enter");
  await page.getByRole("button", { name: "Revisar remarcação" }).click();
  await expect(page.getByRole("heading", { name: "Confirme a remarcação" })).toBeFocused();
  await page.getByRole("button", { name: "Voltar à seleção" }).click(); await expect(page.getByLabel("Data da remarcação")).toBeFocused();
});
test("D4 reduced-motion conserva funcionalidade", async ({ page }) => {
  await page.emulateMedia({ reducedMotion: "reduce" }); await preparar(page); await revisar(page);
  expect(await page.getByRole("button", { name: "Confirmar novo horário" }).evaluate(el => getComputedStyle(el).transitionDuration)).toBe("0s");
  await page.getByRole("button", { name: "Confirmar novo horário" }).click(); await expect(page.getByRole("status")).toHaveText("Agendamento remarcado.");
});
test("D4 não infere ICS nem adiciona consulta de identidade", async ({ page }) => {
  const mock = await preparar(page);
  await expect(page.getByRole("link", { name: /calendário|ics/i })).toHaveCount(0);
  await expect(page.getByRole("button", { name: /calendário|ics/i })).toHaveCount(0);
  expect(mock.urls).toEqual(["/api/publico/meus-agendamentos/d4-local"]);
});
test.describe("D4 fuso do dispositivo diferente", () => {
  test.use({ timezoneId: "Europe/Lisbon" });
  test("data e horário usam fuso canônico do estabelecimento", async ({ page }) => {
    await preparar(page); await expect(page.locator(".gestao-hora")).toHaveText("09:00");
    await revisar(page); await expect(page.locator('[data-destino="true"]')).toContainText("09:15");
  });
});
