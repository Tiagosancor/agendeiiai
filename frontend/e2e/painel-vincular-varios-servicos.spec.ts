import { test, expect, type APIRequestContext, type APIResponse, type Page } from "@playwright/test";
import type { ProfissionalServicoResumo, ServicoResumo } from "../src/lib/tipos";

const API = "http://localhost:5080";
const PAINEL = "http://app.agendeiiai.localhost:3000";
test.use({ serviceWorkers: "block" });

async function abrirFicha(page: Page, profissionalId: string) {
  await page.goto(`${PAINEL}/painel/login`);
  await page.getByLabel("E-mail").fill("admin@acme.dev");
  await page.getByLabel("Senha", { exact: true }).fill("Admin!123");
  await page.getByRole("button", { name: "Entrar", exact: true }).click();
  await expect(page).toHaveURL(/\/painel$/);
  await page.goto(`${PAINEL}/painel/profissionais/${profissionalId}`);
}

// Cada caso cria seus próprios registros, e limpa mesmo se uma asserção falhar.
async function preparar(request: APIRequestContext) {
  const login = await request.post(`${API}/painel/auth/login`, { data: { email: "admin@acme.dev", senha: "Admin!123" } });
  expect(login.ok()).toBeTruthy();
  const headers = { Authorization: `Bearer ${(await login.json()).accessToken}` };
  const categorias = await request.get(`${API}/painel/categorias`, { headers });
  expect(categorias.ok()).toBeTruthy();
  const categoriaId = (await categorias.json())[0]?.id;
  expect(categoriaId, "A stack de desenvolvimento precisa de uma categoria semeada").toBeTruthy();
  const sufixo = Date.now();
  let profissionalId: string | undefined;
  const servicos: ServicoResumo[] = [];
  async function limpar() {
    const erros: unknown[] = [];
    async function tentar(acao: () => Promise<void>) {
      try { await acao(); } catch (erro) { erros.push(erro); }
    }
    if (profissionalId) {
      await tentar(async () => {
        const resposta = await request.get(`${API}/painel/profissionais/${profissionalId}/servicos`, { headers });
        expect(resposta.ok()).toBeTruthy();
        const vinculos: ProfissionalServicoResumo[] = await resposta.json();
        for (const vinculo of vinculos) {
          await tentar(async () => {
            expect((await request.delete(`${API}/painel/profissionais/${profissionalId}/servicos/${vinculo.servicoId}`, { headers })).ok()).toBeTruthy();
          });
        }
      });
      await tentar(async () => {
        expect((await request.delete(`${API}/painel/profissionais/${profissionalId}`, { headers })).ok()).toBeTruthy();
      });
    }
    for (const servico of servicos) {
      await tentar(async () => {
        expect((await request.delete(`${API}/painel/servicos/${servico.id}`, { headers })).ok()).toBeTruthy();
      });
    }
    if (erros.length > 0) throw new AggregateError(erros, "Falhas ao limpar os registros de teste.");
  }
  try {
    const profissional = await request.post(`${API}/painel/profissionais`, { headers, data: { nome: `Zz Vínculos ${sufixo}`, funcao: "Teste" } });
    expect(profissional.ok()).toBeTruthy();
    profissionalId = await profissional.json() as string;
    for (let n = 1; n <= 3; n++) {
      const dados = { categoriaId, nome: `Zz Serviço ${n} ${sufixo}`, preco: 30 + n, duracaoMinutos: 15 + n * 5, popular: false, ativo: true, exibirNaPaginaInicial: true };
      const resposta = await request.post(`${API}/painel/servicos`, { headers, data: dados });
      expect(resposta.ok()).toBeTruthy();
      servicos.push({ ...dados, id: await resposta.json() });
    }
    return { profissionalId, servicos, headers, limpar };
  } catch (erro) {
    try { await limpar(); } catch (erroLimpeza) {
      throw new AggregateError([erro, erroLimpeza], "Falha na preparação e na limpeza dos registros de teste.");
    }
    throw erro;
  }
}

test("vincula dois serviços em sequência com valores padrão e persiste pela API", async ({ page, request }) => {
  const dados = await preparar(request);
  try {
    const corpos: unknown[] = [];
    let emCurso = false;
    let sobreposicoes = 0;
    const botaoDesabilitadoDuranteEnvio: boolean[] = [];
    const errosDoHandler: unknown[] = [];
    await page.route(`**/painel/profissionais/${dados.profissionalId}/servicos`, async (route) => {
      if (route.request().method() !== "POST") return route.continue();
      if (emCurso) sobreposicoes++;
      emCurso = true;
      let resposta: APIResponse | undefined;
      try {
        corpos.push(route.request().postDataJSON());
        botaoDesabilitadoDuranteEnvio.push(await page.getByRole("button", { name: "Vinculando..." }).isDisabled());
        resposta = await route.fetch();
      } catch (erro) {
        errosDoHandler.push(erro);
      } finally {
        emCurso = false;
        if (resposta) await route.fulfill({ response: resposta });
        else await route.abort();
      }
    });
    await abrirFicha(page, dados.profissionalId);
    const selecao = page.getByRole("group", { name: "Vincular serviços" });
    await expect(selecao.getByRole("button", { name: "Vincular selecionados (0)" })).toBeDisabled();
    for (const servico of dados.servicos.slice(0, 2)) {
      await selecao.getByRole("checkbox", { name: new RegExp(servico.nome) }).check();
    }
    await expect(selecao.getByRole("checkbox", { name: "Selecionar todos", exact: true })).toHaveJSProperty("indeterminate", true);
    await selecao.getByRole("button", { name: "Vincular selecionados (2)" }).click();
    await expect(page.getByRole("status").filter({ hasText: "2 serviços vinculados." })).toBeVisible();
    expect(errosDoHandler).toEqual([]);
    expect(sobreposicoes).toBe(0);
    expect(botaoDesabilitadoDuranteEnvio).toEqual([true, true]);
    for (const servico of dados.servicos.slice(0, 2)) {
      await expect(selecao.getByRole("checkbox", { name: new RegExp(servico.nome) })).toHaveCount(0);
    }
    expect(corpos).toEqual(dados.servicos.slice(0, 2).map(s => ({ servicoId: s.id, precoPersonalizado: null, duracaoPersonalizadaMinutos: null })));
    const resposta = await request.get(`${API}/painel/profissionais/${dados.profissionalId}/servicos`, { headers: dados.headers });
    expect(resposta.ok()).toBeTruthy();
    const vinculos = await resposta.json();
    expect(vinculos).toHaveLength(2);
    expect(vinculos).toEqual(expect.arrayContaining(dados.servicos.slice(0, 2).map(s => expect.objectContaining({ servicoId: s.id, preco: s.preco, duracaoMinutos: s.duracaoMinutos }))));
  } finally { await dados.limpar(); }
});

test("falha parcial mantém somente o serviço que falhou marcado e permite tentar novamente", async ({ page, request }) => {
  const dados = await preparar(request);
  let falhar = true;
  try {
    await page.route(`**/painel/profissionais/${dados.profissionalId}/servicos`, async (route) => {
      if (route.request().method() === "POST" && route.request().postDataJSON().servicoId === dados.servicos[1].id && falhar) {
        return route.fulfill({ status: 500, json: { detail: "Falha simulada no vínculo." } });
      }
      return route.continue();
    });
    await abrirFicha(page, dados.profissionalId);
    const selecao = page.getByRole("group", { name: "Vincular serviços" });
    for (const s of dados.servicos) await selecao.getByRole("checkbox", { name: new RegExp(s.nome) }).check();
    await selecao.getByRole("button", { name: "Vincular selecionados (3)" }).click();
    const resultado = page.getByRole("status").filter({ hasText: "2 serviços vinculados." });
    await expect(resultado).toContainText(dados.servicos[1].nome);
    await expect(resultado).toContainText("1 serviço não vinculado");
    await expect(selecao.getByRole("checkbox", { name: new RegExp(dados.servicos[1].nome) })).toBeChecked();
    await expect(selecao.getByRole("button", { name: "Vincular selecionados (1)" })).toBeEnabled();
    for (const s of [dados.servicos[0], dados.servicos[2]]) {
      await expect(selecao.getByRole("checkbox", { name: new RegExp(s.nome) })).toHaveCount(0);
    }
    const respostaParcial = await request.get(`${API}/painel/profissionais/${dados.profissionalId}/servicos`, { headers: dados.headers });
    expect(respostaParcial.ok()).toBeTruthy();
    expect((await respostaParcial.json() as ProfissionalServicoResumo[]).map(v => v.servicoId).sort()).toEqual([dados.servicos[0].id, dados.servicos[2].id].sort());
    falhar = false;
    await selecao.getByRole("button", { name: "Vincular selecionados (1)" }).click();
    await expect(page.getByRole("status").filter({ hasText: "1 serviço vinculado." })).toBeVisible();
    await expect(selecao.getByRole("checkbox", { name: new RegExp(dados.servicos[1].nome) })).toHaveCount(0);
    const respostaFinal = await request.get(`${API}/painel/profissionais/${dados.profissionalId}/servicos`, { headers: dados.headers });
    expect(respostaFinal.ok()).toBeTruthy();
    const vinculos: ProfissionalServicoResumo[] = await respostaFinal.json();
    expect(vinculos).toHaveLength(3);
    expect(vinculos.map(v => v.servicoId).sort()).toEqual(dados.servicos.map(s => s.id).sort());
  } finally { await dados.limpar(); }
});

test("busca ignora acentos e caixa; selecionar todos afeta só os visíveis, em mobile", async ({ page, request }) => {
  const dados = await preparar(request);
  try {
    // Catálogo controlado para os estados de interface; este caso não envia vínculos.
    const catalogo = Array.from({ length: 10 }, (_, n) => ({ ...dados.servicos[0], id: `visual-${n}`, nome: n < 2 ? `ÁCIDO ${n}` : `Outro ${n}` }));
    await page.route("**/painel/servicos", route => route.request().method() === "GET"
      ? route.fulfill({ json: [...catalogo, { ...dados.servicos[0], id: "inativo", nome: "Inativo", ativo: false }] })
      : route.continue());
    await page.route(`**/painel/profissionais/${dados.profissionalId}/servicos`, route => route.request().method() === "GET"
      ? route.fulfill({ json: [{ servicoId: catalogo[9].id, nome: catalogo[9].nome, preco: 31, duracaoMinutos: 20 } satisfies ProfissionalServicoResumo] })
      : route.continue());
    await page.setViewportSize({ width: 375, height: 812 });
    await abrirFicha(page, dados.profissionalId);
    const selecao = page.getByRole("group", { name: "Vincular serviços" });
    await expect(selecao.getByRole("checkbox")).toHaveCount(10); // 9 disponíveis + selecionar todos.
    const todos = selecao.getByRole("checkbox", { name: /^Selecionar todos/ });
    await todos.check();
    await expect(selecao.getByRole("button", { name: "Vincular selecionados (9)" })).toBeEnabled();
    await todos.uncheck();
    await selecao.getByRole("checkbox", { name: /Outro 2/ }).check();
    await selecao.getByRole("searchbox", { name: "Buscar serviço por nome" }).fill("acido");
    await expect(selecao.getByRole("checkbox")).toHaveCount(3);
    await todos.check();
    await expect(selecao.getByRole("button", { name: "Vincular selecionados (3)" })).toBeEnabled();
    await todos.uncheck();
    await expect(selecao.getByRole("button", { name: "Vincular selecionados (1)" })).toBeEnabled();
    await selecao.getByRole("searchbox").fill("sem correspondência");
    await expect(selecao.getByText("Nenhum serviço encontrado.")).toBeVisible();
    await expect(todos).toBeDisabled();
    await selecao.getByRole("searchbox").fill("");
    await expect(selecao.getByRole("checkbox", { name: /Outro 2/ })).toBeChecked();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  } finally { await dados.limpar(); }
});

test("falha de rede usa mensagem em português e o último vínculo leva o foco ao título", async ({ page, request }) => {
  const dados = await preparar(request);
  let falhar = true;
  try {
    // Limita os disponíveis ao serviço real deste caso para exercitar o esvaziamento.
    await page.route("**/painel/servicos", route => route.request().method() === "GET"
      ? route.fulfill({ json: [dados.servicos[0]] })
      : route.continue());
    await page.route(`**/painel/profissionais/${dados.profissionalId}/servicos`, route =>
      route.request().method() === "POST" && falhar ? route.abort("failed") : route.continue());
    await abrirFicha(page, dados.profissionalId);
    const selecao = page.getByRole("group", { name: "Vincular serviços" });
    await selecao.getByRole("checkbox", { name: new RegExp(dados.servicos[0].nome) }).check();
    await expect(selecao.getByRole("status")).toHaveText("1 selecionado");
    await selecao.getByRole("button", { name: "Vincular selecionados (1)" }).click();
    await expect(page.getByRole("status").filter({ hasText: "Falhas:" })).toContainText("Não foi possível vincular (sem conexão com o servidor).");
    await expect(selecao.getByRole("checkbox", { name: new RegExp(dados.servicos[0].nome) })).toBeChecked();
    falhar = false;
    await selecao.getByRole("button", { name: "Vincular selecionados (1)" }).click();
    await expect(selecao).toHaveCount(0);
    await expect(page.getByRole("heading", { name: "Serviços executados", exact: true })).toBeFocused();
  } finally { await dados.limpar(); }
});
