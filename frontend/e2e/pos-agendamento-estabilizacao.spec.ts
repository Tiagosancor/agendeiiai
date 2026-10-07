import { test, expect, type Page, type Route } from "@playwright/test";
import { horarioEstabelecimentoParaIso } from "../src/lib/horario-estabelecimento";

// Toda consulta/ação pública é interceptada; nenhum dado real é lido ou alterado.
const detalhe = { id: "reserva-local", nomeNegocio: "Acme", local: "Rua de teste, 10", inicio: "2026-10-05T12:00:00Z", fim: "2026-10-05T12:30:00Z", servicos: ["Corte Teste"], total: 45, status: "Agendado" };
type Cenario = {
  fuso?: string;
  status?: string;
  consulta?: (rota: Route, numero: number, token: string) => Promise<void>;
  acao?: (rota: Route) => Promise<void>;
};
async function preparar(page: Page, cenario: Cenario = {}) {
  let consultas = 0;
  const posts: { caminho: string; corpo: unknown }[] = [];
  const erros: string[] = [];
  page.on("pageerror", e => erros.push(e.message));
  await page.context().route("**/*", async rota => {
    const pedido = rota.request();
    const url = new URL(pedido.url());
    if (!url.hostname.endsWith("localhost")) return rota.abort();
    if (url.pathname === "/api/publico/negocio") return rota.fulfill({ json: { fuso: cenario.fuso ?? "America/Sao_Paulo" } });
    if (url.pathname.startsWith("/api/publico/meus-agendamentos/")) {
      if (pedido.method() === "POST") {
        posts.push({ caminho: url.pathname, corpo: pedido.postData() ? pedido.postDataJSON() : null });
        if (cenario.acao) return cenario.acao(rota);
        return rota.fulfill({ status: 204, body: "" });
      }
      consultas++;
      if (cenario.consulta) return cenario.consulta(rota, consultas, url.pathname.split("/")[4]);
      return rota.fulfill({ json: { ...detalhe, status: cenario.status ?? "Agendado" } });
    }
    if (url.pathname.startsWith("/api/publico/") || !["GET", "HEAD"].includes(pedido.method())) throw new Error(`Requisição não simulada: ${pedido.method()} ${url.pathname}`);
    return rota.continue();
  });
  return { posts, erros, consultas: () => consultas };
}
const abrir = (page: Page, token = "token-local") => page.goto(`/agendamentos/${token}`);
const campo = (page: Page) => page.getByLabel("Remarcar para", { exact: true });
const remarcar = (page: Page) => page.getByRole("button", { name: "Remarcar", exact: true });
function portao() {
  let liberar!: () => void;
  const promessa = new Promise<void>(resolve => { liberar = resolve; });
  return { promessa, liberar: () => liberar() };
}
test.use({ serviceWorkers: "block" });

for (const timezoneId of ["America/Sao_Paulo", "Europe/Lisbon", "America/New_York"]) {
  test.describe(timezoneId, () => {
    test.use({ timezoneId });
    test("consulta e envio seguem o estabelecimento, não o navegador", async ({ page }) => {
      const mock = await preparar(page);
      await abrir(page);
      await expect(page.getByText(/segunda-feira, 5 de outubro de 2026.*09:00/)).toBeVisible();
      await expect(page.getByText("Total: R$ 45,00")).toBeVisible();
      await expect(page.getByText("Horários do estabelecimento (America/Sao_Paulo).")).toBeVisible();
      await campo(page).fill("2026-10-06T09:00");
      await remarcar(page).click();
      await expect(page.getByRole("status")).toHaveText("Agendamento remarcado.");
      expect(mock.posts).toEqual([{ caminho: "/api/publico/meus-agendamentos/token-local/remarcar", corpo: { novoInicio: "2026-10-06T12:00:00.000Z" } }]);
      expect(mock.erros).toEqual([]);
    });
  });
}

for (const [civil, iso] of [["2026-01-10T09:00", "2026-01-10T14:00:00.000Z"], ["2026-07-10T09:00", "2026-07-10T13:00:00.000Z"]]) {
  test(`DST: offset da data ${civil}, não o offset atual`, async ({ page }) => {
    const mock = await preparar(page, { fuso: "America/New_York" });
    await abrir(page);
    await campo(page).fill(civil);
    await remarcar(page).click();
    await expect(page.getByRole("status")).toHaveText("Agendamento remarcado.");
    expect(mock.posts[0].corpo).toEqual({ novoInicio: iso });
  });
}
for (const [civil, texto] of [["2026-03-08T02:30", "não existe"], ["2026-11-01T01:30", "ocorre duas vezes"]]) {
  test(`DST: ${texto} não envia POST`, async ({ page }) => {
    const mock = await preparar(page, { fuso: "America/New_York" });
    await abrir(page);
    await campo(page).fill(civil);
    await remarcar(page).click();
    await expect(page.getByRole("main").getByRole("alert")).toContainText(texto);
    await expect(remarcar(page)).toBeEnabled();
    expect(mock.posts).toHaveLength(0);
  });
}
test("helper rejeita data inválida, fuso inválido e gap de meia hora", () => {
  expect(() => horarioEstabelecimentoParaIso("2026-02-30T09:00", "America/Sao_Paulo")).toThrow("válidos");
  expect(() => horarioEstabelecimentoParaIso("2026-10-06T09:00", "")).toThrow("indisponível");
  expect(() => horarioEstabelecimentoParaIso("2026-10-06T09:00", "Fuso/Inexistente")).toThrow();
  expect(() => horarioEstabelecimentoParaIso("2026-10-04T02:15", "Australia/Lord_Howe")).toThrow("não existe");
});

test("remarcação usa o horário canônico do GET, sem assumir o valor solicitado", async ({ page }) => {
  await preparar(page, { consulta: (rota, n) => rota.fulfill({ json: n === 1 ? detalhe : { ...detalhe, inicio: "2026-10-06T13:00:00Z" } }) });
  await abrir(page);
  await campo(page).fill("2026-10-06T09:00");
  await remarcar(page).click();
  await expect(page.getByText(/terça-feira, 6 de outubro.*10:00/)).toBeVisible();
  await expect(campo(page)).toHaveValue("");
});

test("POST aceito + GET falho oculta dado antigo e retry faz somente GET", async ({ page }) => {
  const mock = await preparar(page, { consulta: (rota, n) => n === 2
    ? rota.fulfill({ status: 503, json: { title: "Indisponível" } })
    : rota.fulfill({ json: n === 1 ? detalhe : { ...detalhe, inicio: "2026-10-06T13:00:00Z" } }) });
  await abrir(page);
  await campo(page).fill("2026-10-06T09:00");
  await remarcar(page).click();
  await expect(page.getByRole("status")).toContainText("A remarcação foi aceita");
  await expect(page.getByText("Detalhes aguardando atualização.")).toBeVisible();
  await expect(page.getByText(/segunda-feira, 5 de outubro/)).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Cancelar agendamento" })).toHaveCount(0);
  await page.getByRole("button", { name: "Atualizar detalhes", exact: true }).click();
  await expect(page.getByRole("status")).toHaveText("Agendamento remarcado.");
  await expect(page.getByText(/terça-feira, 6 de outubro.*10:00/)).toBeVisible();
  expect(mock.posts).toHaveLength(1);
  expect(mock.consultas()).toBe(3);
});

for (const acao of ["remarcar", "cancelar"] as const) {
  test(`dois cliques síncronos em ${acao}: somente um POST, ações conflitantes bloqueadas`, async ({ page }) => {
    const espera = portao();
    const mock = await preparar(page, { acao: async rota => { await espera.promessa; await rota.fulfill({ status: 204, body: "" }); } });
    await abrir(page);
    if (acao === "remarcar") await campo(page).fill("2026-10-06T09:00");
    else page.on("dialog", dialog => dialog.accept());
    const botao = acao === "remarcar" ? remarcar(page) : page.getByRole("button", { name: "Cancelar agendamento" });
    await botao.evaluate(el => { (el as HTMLButtonElement).click(); (el as HTMLButtonElement).click(); });
    await expect.poll(() => mock.posts.length).toBe(1);
    await expect(page.getByRole("button", { name: acao === "remarcar" ? "Remarcando..." : "Cancelando...", exact: true })).toBeDisabled();
    await expect(campo(page)).toBeDisabled();
    expect(mock.posts).toHaveLength(1);
    espera.liberar();
    await expect(page.getByRole("status")).toHaveText(acao === "remarcar" ? "Agendamento remarcado." : "Agendamento cancelado.");
    if (acao === "cancelar") await expect(page.getByText("Status: Cancelado")).toBeVisible();
    expect(mock.posts).toHaveLength(1);
  });
}
test("cancelamento recusado na confirmação não envia ação", async ({ page }) => {
  const mock = await preparar(page);
  await abrir(page);
  page.on("dialog", dialog => dialog.dismiss());
  await page.getByRole("button", { name: "Cancelar agendamento" }).click();
  await expect(remarcar(page)).toBeEnabled();
  expect(mock.posts).toHaveLength(0);
});

for (const status of [401, 404]) {
  test(`${status}: mensagem segura compartilhada, sem retry temporário`, async ({ page }) => {
    await preparar(page, { consulta: rota => rota.fulfill({ status, body: "" }) });
    await abrir(page);
    await expect(page.getByRole("main").getByRole("alert")).toHaveText("Link inválido ou expirado, ou agendamento não encontrado.");
    await expect(page.getByRole("button", { name: "Tentar novamente" })).toHaveCount(0);
  });
}
for (const falha of ["503", "rede"]) {
  test(`${falha}: retry limpa erro, mostra loading e recupera detalhes`, async ({ page }) => {
    const espera = portao();
    await preparar(page, { consulta: async (rota, n) => {
      if (n === 1) return falha === "rede" ? rota.abort("failed") : rota.fulfill({ status: 503, body: "" });
      await espera.promessa;
      await rota.fulfill({ json: detalhe });
    } });
    await abrir(page);
    await expect(page.getByRole("main").getByRole("alert")).toHaveText("Não foi possível carregar o agendamento. Tente novamente.");
    await page.getByRole("button", { name: "Tentar novamente" }).click();
    await expect(page.getByRole("status")).toHaveText("Carregando...");
    await expect(page.getByRole("main").getByRole("alert")).toHaveCount(0);
    espera.liberar();
    await expect(campo(page)).toBeVisible();
  });
}
test("fuso ausente impede ações e não usa fallback do aparelho", async ({ page }) => {
  const mock = await preparar(page, { fuso: "" });
  await abrir(page);
  await expect(page.getByRole("main").getByRole("alert")).toContainText("Não foi possível carregar");
  await expect(campo(page)).toHaveCount(0);
  expect(mock.posts).toHaveLength(0);
});
test("erro da ação preserva detalhe e permite nova tentativa", async ({ page }) => {
  const mock = await preparar(page, { acao: rota => rota.fulfill({ status: 409, json: { detail: "Esse horário não está disponível." } }) });
  await abrir(page);
  await campo(page).fill("2026-10-06T09:00");
  await remarcar(page).click();
  await expect(page.getByRole("main").getByRole("alert")).toHaveText("Esse horário não está disponível.");
  await expect(page.getByText(/segunda-feira, 5 de outubro.*09:00/)).toBeVisible();
  await expect(remarcar(page)).toBeEnabled();
  expect(mock.consultas()).toBe(1);
});

test("resposta atrasada do token anterior não substitui o atual", async ({ page }) => {
  const espera = portao();
  await preparar(page, { consulta: async (rota, _n, token) => {
    if (token === "token-antigo") {
      await espera.promessa;
      // A navegação pode abortar o pedido antigo antes de sua resposta.
      await rota.fulfill({ json: { ...detalhe, nomeNegocio: "Antigo" } }).catch(() => {});
    } else await rota.fulfill({ json: { ...detalhe, nomeNegocio: "Atual" } });
  } });
  await abrir(page, "token-antigo");
  await expect(page.getByRole("status")).toHaveText("Carregando...");
  await abrir(page, "token-atual");
  await expect(page.getByRole("heading", { name: "Atual" })).toBeVisible();
  espera.liberar();
  await expect(page.getByRole("heading", { name: "Antigo" })).toHaveCount(0);
  await expect(page.getByRole("heading", { name: "Atual" })).toBeVisible();
});
test("mudança de token descarta detalhe, formulário e mensagem anteriores durante loading", async ({ page }) => {
  const espera = portao();
  await preparar(page, { consulta: async (rota, _n, token) => {
    if (token === "token-novo") await espera.promessa;
    await rota.fulfill({ json: { ...detalhe, nomeNegocio: token === "token-novo" ? "Novo" : "Anterior" } });
  } });
  await abrir(page, "token-anterior");
  await campo(page).fill("2026-10-06T09:00");
  await remarcar(page).click();
  await expect(page.getByRole("status")).toHaveText("Agendamento remarcado.");
  await abrir(page, "token-novo");
  await expect(page.getByRole("status")).toHaveText("Carregando...");
  await expect(page.getByRole("heading", { name: "Anterior" })).toHaveCount(0);
  await expect(page.getByText("Agendamento remarcado.")).toHaveCount(0);
  await expect(campo(page)).toHaveCount(0);
  espera.liberar();
  await expect(page.getByRole("heading", { name: "Novo" })).toBeVisible();
  await expect(campo(page)).toHaveValue("");
});
for (const status of ["Cancelado", "Concluido", "Faltou", "Expirado", "Reservado", "EmAtendimento"]) {
  test(`${status}: preserva política atual de não oferecer ações`, async ({ page }) => {
    await preparar(page, { status });
    await abrir(page);
    await expect(page.getByText(`Status: ${{ Concluido: "Concluído", EmAtendimento: "Em atendimento" }[status] ?? status}`)).toBeVisible();
    await expect(campo(page)).toHaveCount(0);
    await expect(page.getByRole("button", { name: "Cancelar agendamento" })).toHaveCount(0);
  });
}

for (const largura of [375, 390, 430, 768, 1024, 1440]) {
  for (const tema of ["light", "dark"]) {
    test(`smoke ${largura}px ${tema}: consulta, reconciliação, retry e ausência de overflow`, async ({ page }) => {
      await page.setViewportSize({ width: largura, height: 900 });
      await page.addInitScript(tema => { localStorage.setItem("tema", tema); }, tema);
      const mock = await preparar(page, { consulta: (rota, n) => n === 2 ? rota.fulfill({ status: 503, body: "" }) : rota.fulfill({ json: detalhe }) });
      await abrir(page);
      await expect(campo(page)).toBeVisible();
      // O tema usa a mesma chave de persistência do provedor existente.
      await expect(page.locator("html")).toHaveAttribute("data-theme", tema);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      await page.screenshot({ path: test.info().outputPath(`consulta-${largura}-${tema}.png`), fullPage: true });
      await campo(page).fill("2026-10-06T09:00");
      await remarcar(page).click();
      await expect(page.getByText("Detalhes aguardando atualização.")).toBeVisible();
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      await page.screenshot({ path: test.info().outputPath(`reconciliacao-${largura}-${tema}.png`), fullPage: true });
      await page.getByRole("button", { name: "Atualizar detalhes" }).click();
      await expect(page.getByRole("status")).toHaveText("Agendamento remarcado.");
      expect(mock.posts).toHaveLength(1);
      expect(mock.erros).toEqual([]);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    });
  }
}
