import { expect, test, type Page, type Route } from "@playwright/test";

test.use({ serviceWorkers: "block" });

const RAIZ = "http://agendeiiai.localhost:3000";
const ORIGEM = "http://agendeiiai.localhost:3000";

const itemBase = {
  id: "11111111-1111-1111-1111-111111111111",
  negocioId: "22222222-2222-2222-2222-222222222222",
  tipo: "Confirmacao",
  canal: "Email",
  status: "Pendente",
  tentativas: 1,
  criadaEm: "2026-10-15T11:50:00Z",
  atualizadaEm: "2026-10-15T12:00:00Z",
  proximaTentativaEm: "2026-10-15T12:05:00Z",
  expiraEm: "2026-10-16T12:00:00Z",
  enviadaEm: null,
  canceladaEm: null,
  ultimoErroCodigo: "falha_provedor",
  agendamentoId: "33333333-3333-3333-3333-333333333333",
};

const resumoSaudavel = {
  pendentes: 0,
  enviando: 0,
  falhas: 0,
  desconhecidas: 0,
  idadeDaPendenciaMaisAntigaSegundos: null,
  pendenciaMaisAntigaDesde: null,
  geradoEm: "2026-10-15T12:00:00Z",
  falhasRecentes: [],
};

function responderJson(rota: Route, corpo: unknown, status = 200) {
  return rota.fulfill({
    status,
    headers: {
      "access-control-allow-origin": ORIGEM,
      "access-control-allow-credentials": "true",
      "content-type": "application/json",
    },
    body: JSON.stringify(corpo),
  });
}

type Cenario = {
  resumo?: Record<string, unknown>;
  listar?: (url: URL, chamada: number, rota: Route) => Promise<void>;
  resumoHandler?: (chamada: number, rota: Route) => Promise<void>;
};

async function prepararApi(page: Page, cenario: Cenario = {}) {
  let chamadasLista = 0;
  let chamadasResumo = 0;
  const urlsLista: URL[] = [];
  await page.route("**/*", async (rota) => {
    const url = new URL(rota.request().url());
    if (url.pathname === "/plataforma/auth/login") return responderJson(rota, { accessToken: "token-plataforma" });
    if (url.pathname === "/plataforma/negocios") return responderJson(rota, []);
    if (url.pathname === "/plataforma/whatsapp/saude") return responderJson(rota, { estado: "NaoSeAplica", provedor: "Fake", detalhe: null });
    if (url.pathname === "/plataforma/notificacoes/resumo") {
      chamadasResumo++;
      if (cenario.resumoHandler) return cenario.resumoHandler(chamadasResumo, rota);
      return responderJson(rota, cenario.resumo ?? resumoSaudavel);
    }
    if (url.pathname === "/plataforma/notificacoes/outbox") {
      chamadasLista++;
      urlsLista.push(url);
      if (cenario.listar) return cenario.listar(url, chamadasLista, rota);
      return responderJson(rota, { itens: [], pagina: Number(url.searchParams.get("pagina") ?? 1), tamanho: Number(url.searchParams.get("tamanho") ?? 50), total: 0 });
    }
    return rota.continue();
  });
  return { urlsLista, get chamadasLista() { return chamadasLista; }, get chamadasResumo() { return chamadasResumo; } };
}

async function entrar(page: Page, caminho = "/plataforma/notificacoes") {
  await page.goto(`${RAIZ}${caminho}`);
  await page.getByLabel("E-mail").fill("dono@plataforma.dev");
  await page.getByLabel("Senha").fill("SenhaSegura123");
  await page.getByRole("button", { name: "Entrar" }).click();
}

for (const caso of [
  { nome: "saudável e sem pendências", resumo: resumoSaudavel, texto: "Sem pendências" },
  { nome: "com pendência antiga", resumo: { ...resumoSaudavel, pendentes: 2, idadeDaPendenciaMaisAntigaSegundos: 360 }, texto: "há 6 minutos" },
  { nome: "com falhas definitivas", resumo: { ...resumoSaudavel, falhas: 3 }, texto: "3" },
  { nome: "com resultados desconhecidos", resumo: { ...resumoSaudavel, desconhecidas: 2 }, texto: "Resultado desconhecido significa" },
]) {
  test(`apresenta resumo ${caso.nome}`, async ({ page }) => {
    await prepararApi(page, { resumo: caso.resumo });
    await entrar(page);
    await expect(page.getByRole("heading", { name: "Notificações", exact: true })).toBeVisible();
    await expect(page.getByText(caso.texto, { exact: caso.texto !== "Resultado desconhecido significa" }).first()).toBeVisible();
    await expect(page.getByText("Última consulta:")).toBeVisible();
  });
}

test("mostra o resumo na plataforma e abre a listagem sem perder a sessão em memória", async ({ page }) => {
  await prepararApi(page);
  await entrar(page, "/plataforma");
  const resumo = page.getByTestId("resumo-outbox");
  await expect(resumo).toContainText("Saúde das notificações");
  await expect(resumo).toContainText("Saudável");
  await resumo.getByRole("link", { name: "Ver detalhes" }).click();
  await expect(page).toHaveURL(/\/plataforma\/notificacoes$/);
  await expect(page.getByRole("heading", { name: "Notificações", exact: true })).toBeVisible();
  await expect(page.getByLabel("E-mail")).toHaveCount(0);
});

test("preserva dados válidos após erro e permite nova tentativa manual", async ({ page }) => {
  let falhar = false;
  await prepararApi(page, {
    listar: async (url, _chamada, rota) => {
      if (falhar) return responderJson(rota, { title: "Falha simulada" }, 500);
      return responderJson(rota, { itens: [itemBase], pagina: 1, tamanho: Number(url.searchParams.get("tamanho")), total: 1 });
    },
  });
  await entrar(page);
  await expect(page.getByRole("table").getByText("Confirmação", { exact: true })).toBeVisible();
  falhar = true;
  await page.getByRole("button", { name: "Atualizar", exact: true }).click();
  const alerta = page.getByRole("region", { name: "Mensagens da outbox" }).getByRole("alert");
  await expect(alerta).toContainText("últimos dados válidos");
  await expect(page.getByRole("table").getByText("Confirmação", { exact: true })).toBeVisible();
  falhar = false;
  await alerta.getByRole("button", { name: "Tentar novamente" }).click();
  await expect(alerta).toHaveCount(0);
});

test("anuncia carregamento e vazio, preservando o estado após falha de rede", async ({ page }) => {
  let falharRede = false;
  await prepararApi(page, {
    listar: async (url, _chamada, rota) => {
      if (falharRede) return rota.abort("failed");
      await new Promise((resolve) => setTimeout(resolve, 300));
      return responderJson(rota, { itens: [], pagina: 1, tamanho: Number(url.searchParams.get("tamanho")), total: 0 });
    },
  });
  await entrar(page);
  await expect(page.getByRole("status", { name: "" }).filter({ hasText: "Carregando mensagens" })).toBeVisible();
  await expect(page.getByText("Nenhuma mensagem encontrada", { exact: true })).toBeVisible();
  falharRede = true;
  await page.getByRole("button", { name: "Atualizar", exact: true }).click();
  const alerta = page.getByRole("region", { name: "Mensagens da outbox" }).getByRole("alert");
  await expect(alerta).toContainText("Verifique a conexão");
  await expect(page.getByText("Nenhuma mensagem encontrada", { exact: true })).toBeVisible();
});

test("aplica todos os filtros, pagina e limpa sem pedir mais de 200 itens", async ({ page }) => {
  const controle = await prepararApi(page, {
    listar: async (url, _chamada, rota) => responderJson(rota, { itens: [itemBase], pagina: Number(url.searchParams.get("pagina")), tamanho: Number(url.searchParams.get("tamanho")), total: 401 }),
  });
  await entrar(page);
  await page.getByLabel("Status").selectOption("Falhou");
  await page.getByLabel("Tipo").selectOption("LembreteRetorno");
  await page.getByLabel("Canal").selectOption("WhatsApp");
  await page.getByLabel("ID do negócio").fill(itemBase.negocioId);
  await page.getByLabel("Atualizada a partir de").fill("2026-10-01");
  await page.getByLabel("Atualizada antes de").fill("2026-10-15");
  await page.getByLabel("Itens por página").selectOption("200");
  await page.getByRole("button", { name: "Aplicar filtros" }).click();
  await expect.poll(() => controle.urlsLista.length).toBeGreaterThan(1);
  const filtrada = controle.urlsLista.at(-1)!;
  expect(filtrada.searchParams.get("status")).toBe("Falhou");
  expect(filtrada.searchParams.get("tipo")).toBe("LembreteRetorno");
  expect(filtrada.searchParams.get("canal")).toBe("WhatsApp");
  expect(filtrada.searchParams.get("negocioId")).toBe(itemBase.negocioId);
  expect(filtrada.searchParams.get("de")).toContain("2026-10-01");
  expect(Number.isNaN(Date.parse(filtrada.searchParams.get("ate") ?? ""))).toBeFalsy();
  expect(filtrada.searchParams.get("tamanho")).toBe("200");
  await page.getByRole("button", { name: "Próxima" }).click();
  await expect.poll(() => controle.urlsLista.at(-1)?.searchParams.get("pagina")).toBe("2");
  await page.getByRole("button", { name: "Limpar filtros" }).click();
  await expect.poll(() => controle.urlsLista.at(-1)?.searchParams.get("status")).toBeNull();
  await expect(page.getByLabel("Status")).toHaveValue("");
});

test("traduz todos os estados, códigos e valores futuros sem renderizar dados pessoais", async ({ page }) => {
  const status = ["Pendente", "Enviando", "Enviada", "Falhou", "Cancelada", "Desconhecida", "Futuro"];
  const codigos = [
    "tentativas_esgotadas", "prazo_esgotado", "recusada_provedor", "resultado_ambiguo", "lease_expirado_apos_inicio",
    "expirada", "agendamento_obsoleto", "horario_alterado", "sem_destinatario", "canal_desativado", "opt_out",
    "cliente_inelegivel", "negocio_inativo", "referencia_inexistente", "falha_provedor", "erro_interno", "codigo_futuro",
  ];
  const itens = codigos.map((codigo, indice) => ({
    ...itemBase,
    id: `${String(indice).padStart(8, "0")}-1111-1111-1111-111111111111`,
    status: status[indice % status.length],
    ultimoErroCodigo: codigo,
    nome: "Pessoa Sigilosa",
    telefone: "+5571999999999",
    email: "sigiloso@teste.dev",
    mensagem: "conteúdo privado",
  }));
  await prepararApi(page, { listar: async (_url, _chamada, rota) => responderJson(rota, { itens, pagina: 1, tamanho: 50, total: itens.length }) });
  await entrar(page);
  for (const texto of ["Pendente", "Em processamento", "Enviada", "Falha definitiva", "Cancelada com segurança", "Resultado desconhecido", "Status ainda não reconhecido"]) {
    await expect(page.getByRole("table").getByText(texto, { exact: true }).first()).toBeVisible();
  }
  for (const codigo of codigos.slice(0, -1)) await expect(page.getByTitle(codigo).first()).toBeVisible();
  await expect(page.getByText("Código técnico ainda não reconhecido", { exact: false }).first()).toBeVisible();
  await expect(page.getByText("Pessoa Sigilosa")).toHaveCount(0);
  await expect(page.getByText("+5571999999999")).toHaveCount(0);
  await expect(page.getByText("sigiloso@teste.dev")).toHaveCount(0);
  await expect(page.getByText("conteúdo privado")).toHaveCount(0);
  const cancelada = page.getByText("Cancelada com segurança", { exact: true }).first();
  await expect(cancelada).not.toHaveClass(/red/);
});

test("sem autenticação mostra login e 403 autenticado é anunciado", async ({ page }) => {
  await page.route("**/*", async (rota) => {
    const url = new URL(rota.request().url());
    if (url.pathname === "/plataforma/auth/login") return responderJson(rota, { accessToken: "sem-permissao" });
    if (rota.request().resourceType() !== "document" && url.pathname.startsWith("/plataforma/notificacoes")) return responderJson(rota, { title: "Proibido" }, 403);
    return rota.continue();
  });
  await page.goto(`${RAIZ}/plataforma/notificacoes`);
  await expect(page.getByRole("heading", { name: "Administração da plataforma" })).toBeVisible();
  await page.getByLabel("E-mail").fill("sem@permissao.dev");
  await page.getByLabel("Senha").fill("Senha123");
  await page.getByRole("button", { name: "Entrar" }).click();
  await expect(page.getByRole("alert").first()).toContainText("não possui acesso");
});

test("401 na API encerra a sessão administrativa mantida somente em memória", async ({ page }) => {
  await page.route("**/*", async (rota) => {
    const url = new URL(rota.request().url());
    if (url.pathname === "/plataforma/auth/login") return responderJson(rota, { accessToken: "token-expirado" });
    if (rota.request().resourceType() !== "document" && url.pathname.startsWith("/plataforma/notificacoes")) return responderJson(rota, { title: "Não autenticado" }, 401);
    return rota.continue();
  });
  await entrar(page);
  await expect(page.getByRole("heading", { name: "Administração da plataforma" })).toBeVisible();
});

for (const largura of [375, 390, 768, 1024]) {
  test(`é responsiva, acessível e sem overflow em ${largura}px`, async ({ page }) => {
    await page.setViewportSize({ width: largura, height: 900 });
    await prepararApi(page, { listar: async (_url, _chamada, rota) => responderJson(rota, { itens: [itemBase], pagina: 1, tamanho: 50, total: 1 }) });
    await entrar(page);
    await page.evaluate(() => document.documentElement.setAttribute("data-theme", "light"));
    await expect(page.getByLabel("Status")).toBeVisible();
    await page.evaluate(() => document.documentElement.setAttribute("data-theme", "dark"));
    await expect(page.getByLabel("Status")).toBeVisible();
    await page.getByLabel("Status").focus();
    await expect(page.getByLabel("Status")).toBeFocused();
    const dimensoes = await page.evaluate(() => ({ largura: document.documentElement.clientWidth, rolagem: document.documentElement.scrollWidth }));
    expect(dimensoes.rolagem).toBeLessThanOrEqual(dimensoes.largura);
    await expect(page.getByRole("button", { name: "Atualizar", exact: true })).toBeVisible();
  });
}

test("resposta antiga não sobrescreve filtro novo e polling não sobrepõe consultas", async ({ page }) => {
  await page.clock.install({ time: new Date("2026-10-15T12:00:00Z") });
  let emAndamento = 0;
  let maximoSimultaneo = 0;
  const controle = await prepararApi(page, {
    listar: async (url, chamada, rota) => {
      emAndamento++;
      maximoSimultaneo = Math.max(maximoSimultaneo, emAndamento);
      if (chamada === 1) await new Promise((resolve) => setTimeout(resolve, 800));
      const tipo = url.searchParams.get("tipo") ?? "Confirmacao";
      try {
        await responderJson(rota, { itens: [{ ...itemBase, tipo }], pagina: 1, tamanho: 50, total: 1 });
      } catch {
        // A consulta anterior pode ser cancelada quando os filtros mudam.
      } finally {
        emAndamento--;
      }
    },
  });
  await entrar(page);
  await page.getByLabel("Tipo").selectOption("LembreteRetorno");
  await page.getByRole("button", { name: "Aplicar filtros" }).click();
  await expect(page.getByRole("table").getByText("Lembrete de retorno", { exact: true })).toBeVisible();
  await page.waitForTimeout(900);
  await expect(page.getByRole("table").getByText("Confirmação", { exact: true })).toHaveCount(0);
  const resumosAntesDoPolling = controle.chamadasResumo;
  const listasAntesDoPolling = controle.chamadasLista;
  await page.clock.fastForward(60_000);
  await expect.poll(() => controle.chamadasResumo).toBe(resumosAntesDoPolling + 1);
  await expect.poll(() => controle.chamadasLista).toBe(listasAntesDoPolling + 1);
  expect(maximoSimultaneo).toBeLessThanOrEqual(2); // a troca de filtro cancela a consulta anterior; polling/manual não cria uma terceira.
});
