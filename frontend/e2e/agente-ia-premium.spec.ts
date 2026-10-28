import { expect, test, type Page } from "@playwright/test";

const BASE = process.env.PREMIUM_E2E_BASE_URL ?? "http://app.agendeiiai.localhost:3000";
const AVISO = "Serviço de agente de IA não contratado. Fale com o suporte para contratar.";
const TITULO = "O serviço de atendimento automático não está contratado para este negócio.";
test.use({ serviceWorkers: "block" });

async function painel(page: Page, contratado: boolean, instancia = true, recusar = false, falharServico = false) {
  const payload = Buffer.from(JSON.stringify({ permissao: ["GerenciarConfiguracoesDoNegocio"] })).toString("base64url");
  let conexoes = 0;
  let alteracoes = 0;
  const corpos: unknown[] = [];
  await page.route("**/painel/auth/renovar", r => r.fulfill({ json: { accessToken: `teste.${payload}.teste` } }));
  await page.route("**/painel/assinatura/aviso", r => r.fulfill({ status: 204 }));
  await page.route("**/painel/whatsapp/*", r => {
    const caminho = new URL(r.request().url()).pathname;
    if (caminho.endsWith("/servico-ia")) return falharServico ? r.fulfill({ status: 502, json: { title: "Serviço temporariamente indisponível." } }) : r.fulfill({ json: { contratado, origem: contratado ? "Manual" : null } });
    if (caminho.endsWith("/status")) return instancia ? r.fulfill({ json: { status: "Conectada", agenteAtivo: !recusar, agenteIaContratado: contratado, telefone: "+5585999999999" } }) : r.fulfill({ status: 404 });
    if (caminho.endsWith("/conectar")) { conexoes++; return r.fulfill({ json: { status: "AguardandoQrCode", qrCode: null } }); }
    alteracoes++;
    corpos.push(r.request().postDataJSON());
    return recusar ? r.fulfill({ status: 409, json: { title: TITULO, codigo: "agente_ia_nao_contratado" } }) : r.fulfill({ status: 204 });
  });
  await page.goto(`${BASE}/painel/whatsapp`);
  return { get conexoes() { return conexoes; }, get alteracoes() { return alteracoes; }, corpos };
}

test("sem contratação permite apenas desligar a preferência ativa", async ({ page }) => {
  const api = await painel(page, false);
  await expect(page.getByText(AVISO)).toBeVisible();
  const controle = page.getByRole("switch", { name: "Agente ativo" });
  await expect(controle).toBeEnabled();
  await expect(controle).toHaveAttribute("aria-checked", "true");
  await expect(controle).toHaveText("Desativar agente");
  await controle.click();
  await expect(controle).toHaveAttribute("aria-checked", "false");
  await expect(controle).toBeDisabled();
  await expect(page.getByText(AVISO)).toBeVisible();
  expect(api.corpos).toEqual([{ ativo: false }]);
});

test("sem instância e sem serviço permite conectar no celular", async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 812 });
  const api = await painel(page, false, false);
  await expect(page.getByText(AVISO)).toBeVisible();
  await page.getByRole("button", { name: "Conectar WhatsApp" }).click();
  expect(api.conexoes).toBe(1);
  await expect(page.getByRole("button", { name: "Gerar novo QR" })).toBeEnabled();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

test("409 após perda da contratação mostra title e bloqueia novas ativações", async ({ page }) => {
  const api = await painel(page, true, true, true);
  const controle = page.getByRole("switch", { name: "Agente ativo" });
  await controle.click();
  await expect(page.getByRole("region", { name: "Conexão do WhatsApp" }).getByRole("alert")).toHaveText(TITULO);
  await expect(page.getByText(AVISO)).toBeVisible();
  await expect(controle).toBeDisabled();
  await expect(controle).toHaveAttribute("aria-checked", "false");
  expect(api.alteracoes).toBe(1);
});

test("falha ao consultar serviço mantém controle bloqueado e permite tentar novamente", async ({ page }) => {
  await painel(page, true, true, true, true);
  await expect(page.getByRole("region", { name: "Conexão do WhatsApp" }).getByRole("alert")).toContainText("Serviço temporariamente indisponível.");
  await expect(page.getByRole("switch", { name: "Agente ativo" })).toBeDisabled();
  await expect(page.getByRole("button", { name: "Consultar serviço novamente" })).toBeEnabled();
});

async function plataforma(page: Page, falhar = false, falharRecarga = false) {
  let contratado = false;
  const motivos: unknown[] = [];
  const filtros: string[] = [];
  const historico: { em: string; autor: string; acao: string; detalhes: string }[] = [];
  const servico = () => ({ contratado, origem: contratado ? "Manual" : null, alteradoEm: historico[0]?.em ?? null, alteradoPor: historico[0]?.autor ?? null });
  const negocio = () => ({ id: "negocio-1", nome: "Salão Teste", slug: "salao", tipo: "Salao", plano: null, estado: null, fimTeste: null, proximoVencimento: null, cadastradoEm: "2026-10-01T12:00:00Z", agenteIaContratado: contratado, agenteIaOrigem: contratado ? "Manual" : null, whatsAppConectado: true });
  await page.route("**/*", r => {
    const url = new URL(r.request().url());
    if (url.pathname === "/plataforma/auth/login") return r.fulfill({ json: { accessToken: "token-plataforma" } });
    if (url.pathname === "/plataforma/negocios") {
      filtros.push(url.search);
      if (falharRecarga && motivos.length > 0 && filtros.length === 2) return r.fulfill({ status: 502 });
      const filtro = url.searchParams.get("agenteIa");
      return r.fulfill({ json: filtro && filtro !== String(contratado) ? [] : [negocio()] });
    }
    if (url.pathname.endsWith("/agente-ia/historico")) return r.fulfill({ json: historico });
    if (url.pathname.endsWith("/agente-ia")) {
      expect(r.request().method()).toBe("PUT");
      expect(r.request().headers().authorization).toBe("Bearer token-plataforma");
      const corpo = r.request().postDataJSON();
      motivos.push(corpo);
      if (falhar) return r.fulfill({ status: 400, json: { title: "Não foi possível alterar o serviço." } });
      contratado = corpo.contratado;
      historico.unshift({ em: "2026-10-28T12:00:00Z", autor: "plataforma:admin@teste.dev", acao: contratado ? "LigarAgenteIa" : "DesligarAgenteIa", detalhes: `origem Manual; motivo: ${corpo.motivo ?? "sem motivo"}` });
      return r.fulfill({ json: servico() });
    }
    if (url.pathname === "/plataforma/negocios/negocio-1") return r.fulfill({ json: { negocio: negocio(), agenteIa: servico(), planoId: null, periodicidade: null, precoMensalTravado: null, valorDoPeriodo: null, carenciaAte: null, profissionaisAtivos: 1, historico: [], cobrancas: [] } });
    if (url.pathname === "/cadastro/planos") return r.fulfill({ json: [] });
    if (url.pathname === "/plataforma/whatsapp/saude") return r.fulfill({ json: { estado: "NaoSeAplica", provedor: "Fake" } });
    if (url.pathname === "/plataforma/notificacoes/resumo") return r.fulfill({ status: 503 });
    return r.continue();
  });
  await page.goto(`${BASE}/plataforma`);
  await page.getByLabel("E-mail").fill("admin@teste.dev");
  await page.getByLabel("Senha", { exact: true }).fill("Teste123!");
  await page.getByRole("button", { name: "Entrar", exact: true }).click();
  return { motivos, filtros };
}

test("plataforma confirma, liga/desliga, registra motivo e atualiza histórico aberto", async ({ page }) => {
  const api = await plataforma(page);
  await page.getByRole("button", { name: "Salão Teste", exact: true }).click();
  const historico = page.getByRole("region", { name: "Histórico do agente de IA" });
  await expect(historico.getByText("Nenhuma alteração registrada.")).toBeVisible();
  const controle = page.getByRole("switch", { name: "Agente de IA — Salão Teste" });
  await controle.click();
  await expect(page.getByLabel("Motivo (opcional)")).toBeFocused();
  await expect(page.getByLabel("Motivo (opcional)")).toBeFocused();
  await page.getByRole("button", { name: "Cancelar", exact: true }).click();
  await expect(controle).toBeFocused();
  await expect(controle).toBeFocused();
  expect(api.motivos).toHaveLength(0);
  await controle.click();
  await page.getByLabel("Motivo (opcional)").fill("Demonstração autorizada");
  await expect(page.getByLabel("Motivo (opcional)")).toHaveAttribute("maxlength", "500");
  await page.getByRole("button", { name: "Confirmar", exact: true }).click();
  await expect(controle).toHaveAttribute("aria-checked", "true");
  await expect(historico).toContainText("Demonstração autorizada");
  await expect(controle).toBeFocused();
  await controle.click();
  await page.getByRole("button", { name: "Confirmar", exact: true }).click();
  await expect(controle).toHaveAttribute("aria-checked", "false");
  await expect(historico).toContainText("Serviço desligado");
  expect(api.motivos).toEqual([{ contratado: true, motivo: "Demonstração autorizada" }, { contratado: false }]);
});

test("plataforma combina filtro de assinatura com contratação", async ({ page }) => {
  const api = await plataforma(page);
  await page.getByLabel("Filtrar por agente de IA").selectOption("true");
  await expect(page.getByText("Nenhum negócio neste filtro.")).toBeVisible();
  await page.getByLabel("Filtrar por estado", { exact: true }).selectOption("Ativa");
  await expect.poll(() => api.filtros.at(-1)).toBe("?estado=Ativa&agenteIa=true");
  await page.getByLabel("Filtrar por agente de IA").selectOption("false");
  await expect(page.getByRole("switch", { name: "Agente de IA — Salão Teste" })).toBeVisible();
});

test("falha de alteração mantém estado e motivo para nova tentativa", async ({ page }) => {
  const api = await plataforma(page, true);
  const controle = page.getByRole("switch", { name: "Agente de IA — Salão Teste" });
  await controle.click();
  await page.getByLabel("Motivo (opcional)").fill("Teste");
  await page.getByRole("button", { name: "Confirmar", exact: true }).click();
  await expect(page.getByText("Não foi possível alterar o serviço.")).toBeVisible();
  await expect(controle).toHaveAttribute("aria-checked", "false");
  await expect(page.getByLabel("Motivo (opcional)")).toHaveValue("Teste");
  expect(api.motivos).toHaveLength(1);
});


test("PUT concluído e recarga com falha oferece repetir só a leitura", async ({ page }) => {
  const api = await plataforma(page, false, true);
  await page.getByRole("button", { name: "Salão Teste", exact: true }).click();
  await page.getByLabel("Valor (R$)").fill("123,45");
  const controle = page.getByRole("switch", { name: "Agente de IA — Salão Teste" });
  await controle.click();
  await page.getByRole("button", { name: "Confirmar", exact: true }).click();
  const erroLista = page.getByRole("alert").filter({ hasText: "Não foi possível carregar os negócios." });
  await expect(erroLista).toBeVisible();
  await expect(controle).toHaveAttribute("aria-checked", "true");
  await expect(controle).toHaveText("Desligar");
  await expect(controle).toBeFocused();
  await expect(page.getByLabel("Motivo (opcional)")).toHaveCount(0);
  await expect(page.getByLabel("Valor (R$)")).toHaveValue("123.45");
  await erroLista.getByRole("button", { name: "Tentar novamente" }).click();
  await expect(erroLista).toHaveCount(0);
  await expect(controle).toHaveAttribute("aria-checked", "true");
  expect(api.motivos).toEqual([{ contratado: true }]);
});
