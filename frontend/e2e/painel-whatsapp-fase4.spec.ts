import { test, expect, type Page } from "@playwright/test";
import type { DetalheConversaWhatsApp, StatusWhatsApp, UsoIaMes } from "../src/lib/tipos";

const BASE = process.env.WHATSAPP_E2E_BASE_URL ?? "http://app.agendeiiai.localhost:3000";
test.use({ serviceWorkers: "block" });
const permissoes = ["GerenciarConfiguracoesDoNegocio", "AtenderConversasWhatsApp", "GerenciarClientes"];
const status: StatusWhatsApp = { status: "Conectada", telefone: "+5585999999999", agenteAtivo: true, agenteIaContratado: true, mensagemTransferencia: "Aguarde a equipe.", iaAtiva: true, agendamentoAtivo: true, atendimentoHumanoAtivo: false, gestaoModo: "Desligado" };
const uso: UsoIaMes = { mes: "2026-10", chamadas: 10, chamadasComFalha: 1, tokensEntrada: 800, tokensSaida: 300, cotaMensalTokens: 1000, percentualCota: 110, chamadasHoje: 2, limiteChamadasPorDia: 250, conversasAtendidas: 4, respostasFallback: 1, agendamentosConfirmados: 2, cancelamentos: 0, remarcacoes: 0, transferenciasHumano: 1, porDia: [{ dia: "2026-10-28", chamadas: 10, tokens: 1100 }] };
const conversa: DetalheConversaWhatsApp = { id: "conversa-a", telefone: "+5585999999999", clienteNome: "Cliente de teste", status: "Humano", humanoDesde: "2026-10-28T12:00:00Z", humanoAte: null, humanoMotivo: "PedidoDoAgente", ultimaMensagemEm: "2026-10-28T12:01:00Z", mensagensDesdeHumano: 1, aguardandoAtendimento: true, haMaisAntigas: true, mensagens: [{ id: "m2", papel: "Sistema", texto: "Transferência de teste", em: "2026-10-28T12:00:00Z" }, { id: "m3", papel: "Cliente", texto: "Mensagem fictícia", em: "2026-10-28T12:01:00Z" }] };

async function preparar(page: Page, claims = permissoes) {
  const payload = Buffer.from(JSON.stringify({ permissao: claims })).toString("base64url");
  await page.route("**/painel/auth/renovar", r => r.fulfill({ json: { accessToken: `teste.${payload}.teste`, expiraEm: "2099-01-01T00:00:00Z" } }));
  await page.route("**/painel/assinatura/aviso", r => r.fulfill({ status: 204 }));
  await page.route("**/painel/whatsapp/servico-ia", r => r.fulfill({ json: { contratado: true, origem: "Manual" } }));
  await page.route("**/painel/whatsapp/status", r => r.fulfill({ json: status }));
  await page.route("**/painel/whatsapp/uso*", r => r.fulfill({ json: uso }));
  await page.route("**/painel/whatsapp/conversas/resumo", r => r.fulfill({ json: { aguardandoAtendimento: 2, emAtendimentoHumano: 3 } }));
}

test("WhatsApp: mensagem limitada a 300, flags somente leitura e cota acima de 100%", async ({ page }) => {
  await preparar(page);
  let mensagem: unknown;
  await page.route("**/painel/whatsapp/mensagem-transferencia", r => { mensagem = r.request().postDataJSON(); return r.fulfill({ status: 204 }); });
  await page.goto(`${BASE}/painel/whatsapp`);
  const config = page.getByRole("region", { name: "Configurações do agente" });
  await expect(config.getByText("Desligado", { exact: true })).toBeVisible();
  await expect(config.getByText("Gestão de agendamentos: Desligado")).toBeVisible();
  await expect(config.getByRole("switch")).toHaveCount(0);
  const campo = page.getByLabel("Mensagem de transferência", { exact: true });
  await expect(campo).toHaveAttribute("maxlength", "300");
  await campo.fill("  Equipe disponível em breve.  ");
  await page.getByRole("button", { name: "Salvar mensagem" }).click();
  await expect(config.getByRole("status")).toHaveText("Mensagem salva.");
  expect(mensagem).toEqual({ mensagem: "Equipe disponível em breve." });
  await expect(campo).toHaveValue("Equipe disponível em breve.");
  await campo.fill("");
  await page.getByRole("button", { name: "Salvar mensagem" }).click();
  await expect.poll(() => mensagem).toEqual({ mensagem: null });
  await expect(page.getByText(/110% da cota/)).toBeVisible();
  await expect(page.getByRole("progressbar")).toHaveAttribute("value", "100");
  await expect(page.getByRole("cell", { name: "1.100", exact: true })).toBeVisible();
  await expect(page.getByLabel("Mês (UTC)")).toHaveAttribute("min", "2020-01");
  await expect(page.getByLabel("Mês (UTC)")).toHaveAttribute("max", "9999-11");
});

test("WhatsApp: mudança de mês ignora resposta obsoleta", async ({ page }) => {
  await preparar(page);
  let liberar!: () => void;
  const espera = new Promise<void>(r => { liberar = r; });
  await page.route("**/painel/whatsapp/uso*", async r => {
    const mes = new URL(r.request().url()).searchParams.get("mes");
    if (mes === "2026-09") await espera;
    await r.fulfill({ json: { ...uso, mes: mes || uso.mes, porDia: mes === "2026-08" ? [] : uso.porDia } }).catch(() => {});
  });
  await page.goto(`${BASE}/painel/whatsapp`);
  await expect(page.getByLabel("Mês (UTC)")).toHaveValue("2026-10");
  await page.getByLabel("Mês (UTC)").fill("2026-09");
  await page.getByLabel("Mês (UTC)").fill("2026-08");
  await expect(page.getByText("Sem uso neste mês.")).toBeVisible();
  liberar();
  await expect(page.getByLabel("Mês (UTC)")).toHaveValue("2026-08");
  await expect(page.getByText("Sem uso neste mês.")).toBeVisible();
});

test("WhatsApp: PUT com erro anuncia alerta e repetir consulta limpa o erro de uso", async ({ page }) => {
  await preparar(page);
  await page.route("**/painel/whatsapp/mensagem-transferencia", r => r.fulfill({ status: 400, json: { title: "Mensagem inválida." } }));
  let liberar!: () => void;
  const espera = new Promise<void>(r => { liberar = r; });
  let consultas = 0;
  await page.route("**/painel/whatsapp/uso*", async r => {
    if (++consultas === 1) return r.fulfill({ status: 503, json: { title: "Uso indisponível." } });
    await espera;
    await r.fulfill({ json: uso });
  });
  await page.goto(`${BASE}/painel/whatsapp`);
  await page.getByRole("button", { name: "Salvar mensagem" }).click();
  await expect(page.getByRole("region", { name: "Configurações do agente" }).getByRole("alert")).toHaveText("Mensagem inválida.");
  const regiao = page.getByRole("region", { name: "Uso de IA" });
  await regiao.getByRole("button", { name: "Tentar novamente" }).click();
  await expect(regiao.getByText("Carregando uso…")).toBeVisible();
  await expect(regiao.getByRole("alert")).toHaveCount(0);
  liberar();
  await expect(regiao.getByRole("progressbar")).toBeVisible();
});

for (const acao of ["assumir", "devolver"] as const) {
  test(`WhatsApp: ${acao} confirmado por 204 permanece feito se a recarga falhar`, async ({ page }) => {
    await preparar(page);
    let concluiu = false;
    await page.route("**/painel/whatsapp/conversas/conversa-a", r => {
      if (new URL(r.request().url()).origin === new URL(BASE).origin) return r.continue();
      return concluiu ? r.fulfill({ status: 503 }) : r.fulfill({ json: conversa });
    });
    await page.route(`**/painel/whatsapp/conversas/conversa-a/${acao}`, r => { concluiu = true; return r.fulfill({ status: 204 }); });
    await page.goto(`${BASE}/painel/whatsapp/conversas/conversa-a`);
    await page.getByRole("button", { name: acao === "assumir" ? "Assumir conversa" : "Devolver ao agente" }).click();
    const regiao = page.getByRole("region", { name: "Detalhe da conversa" });
    await expect(regiao.getByRole("alert")).toContainText("Ação feita; não foi possível atualizar a conversa.");
    await expect(regiao.getByText(acao === "assumir" ? "Em atendimento humano" : "Com o agente", { exact: true })).toBeVisible();
    await expect(regiao.getByRole("button", { name: acao === "assumir" ? "Assumir conversa" : "Devolver ao agente" })).toBeDisabled();
  });
}

test("WhatsApp: só atendente tem acesso estilizado e contador zero fica oculto", async ({ page }) => {
  await preparar(page, ["AtenderConversasWhatsApp"]);
  await page.route("**/painel/whatsapp/conversas/resumo", r => r.fulfill({ json: { aguardandoAtendimento: 0, emAtendimentoHumano: 1 } }));
  await page.goto(`${BASE}/painel/whatsapp`);
  await expect(page.getByRole("heading", { name: "WhatsApp", exact: true })).toBeVisible();
  await expect(page.getByRole("link", { name: "Abrir conversas", exact: true })).toHaveClass(/bg-/);
  await expect(page.locator(".painel-sidebar .sr-only").filter({ hasText: /aguardando atendimento/ })).toHaveCount(0);
});

test("WhatsApp: lista paginada, filtros, contador e ausência de overflow mobile", async ({ page }) => {
  await preparar(page);
  await page.setViewportSize({ width: 390, height: 844 });
  const consultas: string[] = [];
  await page.route("**/painel/whatsapp/conversas?*", r => {
    const u = new URL(r.request().url()); consultas.push(u.search);
    return r.fulfill({ json: { itens: [conversa], total: 21, pagina: Number(u.searchParams.get("pagina")), tamanhoPagina: 20 } });
  });
  await page.goto(`${BASE}/painel/whatsapp/conversas`);
  await expect(page.getByRole("link", { name: "Cliente de teste" })).toBeVisible();
  await page.getByRole("button", { name: "Próxima", exact: true }).click();
  await expect(page.getByText("Página 2 de 2")).toBeVisible();
  await page.getByLabel("Mostrar", { exact: true }).selectOption("aguardando");
  await expect(page.getByText("Página 1 de 2")).toBeVisible();
  expect(consultas).toContain("?filtro=aguardando&pagina=1");
  await page.getByLabel("Mostrar", { exact: true }).selectOption("humano");
  await expect.poll(() => consultas.at(-1)).toBe("?filtro=humano&pagina=1");
  await page.getByRole("button", { name: "Abrir menu" }).click();
  await expect(page.getByRole("link", { name: /WhatsApp\s*, 2 aguardando atendimento/ })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});

test("WhatsApp: detalhe usa cursor composto, assume e devolve sem corpo 204", async ({ page }) => {
  await preparar(page);
  let estado = { ...conversa };
  const acoes: string[] = [];
  await page.route("**/painel/whatsapp/conversas/conversa-a**", r => {
    const u = new URL(r.request().url());
    if (u.origin === new URL(BASE).origin) return r.continue();
    if (r.request().method() === "POST") {
      acoes.push(u.pathname.split("/").at(-1)!);
      estado = { ...estado, status: u.pathname.endsWith("devolver") ? "Agente" : "Humano", aguardandoAtendimento: false };
      return r.fulfill({ status: 204 });
    }
    if (u.searchParams.has("antesDe")) {
      expect(u.searchParams.get("antesDe")).toBe(conversa.mensagens[0].em);
      expect(u.searchParams.get("antesDeId")).toBe("m2");
      return r.fulfill({ json: { ...estado, haMaisAntigas: false, mensagens: [{ id: "m1", papel: "Assistente", texto: "Mensagem anterior fictícia", em: conversa.mensagens[0].em }] } });
    }
    return r.fulfill({ json: estado });
  });
  await page.goto(`${BASE}/painel/whatsapp/conversas/conversa-a`);
  await page.getByRole("button", { name: "Carregar anteriores" }).click();
  await expect(page.getByRole("list", { name: "Mensagens da conversa" }).getByRole("listitem")).toHaveCount(3);
  await expect(page.getByRole("button", { name: "Carregar anteriores" })).toHaveCount(0);
  await page.getByRole("button", { name: "Assumir conversa" }).click();
  await expect(page.getByText("Em atendimento humano", { exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "Assumir conversa" })).toBeDisabled();
  await page.getByRole("button", { name: "Devolver ao agente" }).click();
  await expect(page.getByText("Com o agente", { exact: true })).toBeVisible();
  expect(acoes).toEqual(["assumir", "devolver"]);
});

test("WhatsApp: exclusão exige confirmação e volta à lista", async ({ page }) => {
  await preparar(page);
  let apagou = false;
  await page.route("**/painel/whatsapp/conversas/conversa-a", r => new URL(r.request().url()).origin === new URL(BASE).origin ? r.continue() : r.fulfill({ json: conversa }));
  await page.route("**/painel/whatsapp/conversas/conversa-a/apagar", r => { expect(r.request().method()).toBe("POST"); apagou = true; return r.fulfill({ status: 204 }); });
  await page.route("**/painel/whatsapp/conversas?*", r => r.fulfill({ json: { itens: [], pagina: 1, total: 0, tamanhoPagina: 20 } }));
  await page.goto(`${BASE}/painel/whatsapp/conversas/conversa-a`);
  await page.getByRole("button", { name: "Apagar conversa", exact: true }).click();
  await page.getByRole("dialog").getByRole("button", { name: "Cancelar", exact: true }).click();
  expect(apagou).toBe(false);
  await page.getByRole("button", { name: "Apagar conversa", exact: true }).click();
  await page.getByRole("button", { name: "Confirmar exclusão" }).click();
  await expect(page).toHaveURL(`${BASE}/painel/whatsapp/conversas`);
  expect(apagou).toBe(true);
});

test("WhatsApp: só atendente acessa conversas sem acesso às configurações nem exclusão", async ({ page }) => {
  await preparar(page, ["AtenderConversasWhatsApp"]);
  let configuracoes = 0;
  await page.route("**/painel/whatsapp/status", r => { configuracoes++; return r.fulfill({ status: 403 }); });
  await page.route("**/painel/whatsapp/conversas/conversa-a", r => new URL(r.request().url()).origin === new URL(BASE).origin ? r.continue() : r.fulfill({ json: conversa }));
  await page.goto(`${BASE}/painel/whatsapp/conversas/conversa-a`);
  await expect(page.getByRole("button", { name: "Assumir conversa" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Apagar conversa", exact: true })).toHaveCount(0);
  expect(configuracoes).toBe(0);
});

test("WhatsApp: sem permissão não consulta resumo nem conversas; 404 exibe erro humano", async ({ page }) => {
  await preparar(page, ["GerenciarConfiguracoesDoNegocio"]);
  let consultas = 0;
  await page.route("**/painel/whatsapp/conversas/**", r => { if (new URL(r.request().url()).origin === new URL(BASE).origin) return r.continue(); consultas++; return r.fulfill({ status: 403 }); });
  await page.goto(`${BASE}/painel/whatsapp/conversas/conversa-a`);
  await expect(page.getByText("Sem permissão para atender conversas do WhatsApp.")).toBeVisible();
  expect(consultas).toBe(0);
  await preparar(page);
  await page.route("**/painel/whatsapp/conversas/conversa-a", r => new URL(r.request().url()).origin === new URL(BASE).origin ? r.continue() : r.fulfill({ status: 404, json: { title: "Conversa não encontrada." } }));
  await page.reload();
  await expect(page.getByRole("region", { name: "Detalhe da conversa" }).getByRole("alert")).toHaveText("Conversa não encontrada.");
  await expect(page.getByRole("button", { name: "Assumir conversa" })).toHaveCount(0);
});

test("WhatsApp: filtro novo não recebe lista atrasada do anterior", async ({ page }) => {
  await preparar(page);
  let liberar!: () => void;
  const espera = new Promise<void>(r => { liberar = r; });
  await page.route("**/painel/whatsapp/conversas?*", async r => {
    const filtro = new URL(r.request().url()).searchParams.get("filtro");
    if (filtro === "todas") await espera;
    await r.fulfill({ json: { itens: filtro === "todas" ? [conversa] : [], total: filtro === "todas" ? 1 : 0, pagina: 1, tamanhoPagina: 20 } }).catch(() => {});
  });
  await page.goto(`${BASE}/painel/whatsapp/conversas`);
  await page.getByLabel("Mostrar", { exact: true }).selectOption("humano");
  await expect(page.getByText("Nenhuma conversa neste filtro.")).toBeVisible();
  liberar();
  await expect(page.getByRole("link", { name: "Cliente de teste" })).toHaveCount(0);
});

test("WhatsApp: agenda por profissional mostra origem e prazo da reserva", async ({ page }) => {
  await preparar(page, [...permissoes, "GerenciarAgenda"]);
  await page.route("**/painel/agenda/profissionais", r => r.fulfill({ json: [{ id: "profissional-a", nome: "Profissional teste", ativo: true }] }));
  await page.route("**/painel/agenda/grade?*", r => r.fulfill({ json: { data: "2026-10-28", horarios: [], profissionais: [] } }));
  await page.route("**/painel/agenda?*", r => r.fulfill({ json: [{ id: "reserva-a", profissionalId: "profissional-a", clienteId: null, clienteNome: "Reserva", inicio: "2026-10-28T14:00:00Z", fim: "2026-10-28T14:30:00Z", status: "Reservado", observacoes: null, servicos: ["Corte"], total: 40, forcado: false, forcadoMotivo: null, forcadoPor: null, forcadoRegras: null, origem: "WhatsApp", reservadoAte: "2026-10-28T13:50:00Z" }] }));
  await page.goto(`${BASE}/painel/agenda`);
  await page.getByRole("tab", { name: "Por profissional" }).click();
  await expect(page.getByText(/Aguardando confirmação no WhatsApp \(até/)).toBeVisible();
  await expect(page.locator(".painel-agenda-item").getByText("WhatsApp", { exact: true })).toBeVisible();
});

test("WhatsApp: 403 em resumo não mantém contador e falha de ação permite repetir", async ({ page }) => {
  await preparar(page);
  await page.route("**/painel/whatsapp/conversas/resumo", r => r.fulfill({ status: 403 }));
  await page.route("**/painel/whatsapp/conversas/conversa-a", r => new URL(r.request().url()).origin === new URL(BASE).origin ? r.continue() : r.fulfill({ json: conversa }));
  let falhar = true;
  await page.route("**/painel/whatsapp/conversas/conversa-a/assumir", r => falhar ? r.fulfill({ status: 403, json: { title: "Sem permissão para assumir." } }) : r.fulfill({ status: 204 }));
  await page.goto(`${BASE}/painel/whatsapp/conversas/conversa-a`);
  await expect(page.locator(".painel-sidebar .sr-only").filter({ hasText: /\d+ aguardando atendimento/ })).toHaveCount(0);
  await page.getByRole("button", { name: "Assumir conversa" }).click();
  await expect(page.getByRole("region", { name: "Detalhe da conversa" }).getByRole("alert")).toHaveText("Sem permissão para assumir.");
  falhar = false;
  await page.getByRole("button", { name: "Assumir conversa" }).click();
  await expect(page.getByRole("region", { name: "Detalhe da conversa" }).getByRole("alert")).toHaveCount(0);
});
