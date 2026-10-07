import { test, expect } from "@playwright/test";
import { execFileSync } from "node:child_process";
import { resolve } from "node:path";

// Smoke exclusivamente local: usa o contrato real D2 e os provedores Fake.
test("D3 full-stack local: capabilities, slots, conflito, 200 canônico e cancelamento", async ({ page, request, playwright }) => {
  test.setTimeout(90_000);
  const provedores = execFileSync("docker", ["compose", "exec", "-T", "api", "printenv", "Email__Provedor", "WhatsApp__Provedor"], { cwd: resolve(".."), encoding: "utf8" }).trim().split(/\r?\n/);
  expect(provedores).toEqual(["Fake", "Fake"]);
  const api = "http://localhost:5080";
  // Node não resolve *.localhost como o Chromium; o Host mantém o tenant real.
  const publico = "http://localhost:3000/api/publico";
  const requisicaoPublica = await playwright.request.newContext({ extraHTTPHeaders: { Host: "acme.agendeiiai.localhost:3000" } });
  const login = await request.post(`${api}/painel/auth/login`, { data: { email: "admin@acme.dev", senha: "Admin!123" } });
  expect(login.ok()).toBe(true);
  const headers = { Authorization: `Bearer ${(await login.json()).accessToken}` };
  const sufixo = Date.now().toString().slice(-8), telefone = `+55719${sufixo}`;
  const data = (dias: number) => new Date(Date.now() + dias * 86400000).toISOString().slice(0, 10);
  let profissionalId: string | undefined;
  const agendamentos: string[] = [];
  try {
    const profissional = await request.post(`${api}/painel/profissionais`, { headers, data: { nome: `D3 Local ${sufixo}`, funcao: "Barbeiro" } });
    expect(profissional.ok()).toBe(true); profissionalId = await profissional.json();
    expect((await request.put(`${api}/painel/profissionais/${profissionalId}/horarios`, { headers, data: [0, 1, 2, 3, 4, 5, 6].map(diaSemana => ({ diaSemana, inicio: "08:00:00", fim: "20:00:00" })) })).ok()).toBe(true);
    const categoria = await request.post(`${api}/painel/categorias`, { headers, data: { nome: `D3 Local ${sufixo}` } }); expect(categoria.ok()).toBe(true);
    const servico = await request.post(`${api}/painel/servicos`, { headers, data: { categoriaId: await categoria.json(), nome: `Corte D3 ${sufixo}`, preco: 45, duracaoMinutos: 30 } }); expect(servico.ok()).toBe(true);
    const servicoId = await servico.json();
    expect((await request.post(`${api}/painel/profissionais/${profissionalId}/servicos`, { headers, data: { servicoId } })).ok()).toBe(true);
    const disponivel = await requisicaoPublica.get(`${publico}/horarios-livres?data=${data(3)}&duracaoMinutos=30&profissionalId=${profissionalId}&servicoIds=${servicoId}`); expect(disponivel.ok()).toBe(true);
    const inicio = (await disponivel.json())[0].inicio;
    const reserva = await requisicaoPublica.post(`${publico}/reservas`, { data: { profissionalId, servicoIds: [servicoId], inicio } }); expect(reserva.status()).toBe(201);
    const agendamentoId = (await reserva.json()).agendamentoId; agendamentos.push(agendamentoId);
    expect((await requisicaoPublica.post(`${publico}/codigos`, { data: { telefone, email: `d3-${sufixo}@teste.com` } })).status()).toBe(202);
    const log = execFileSync("docker", ["compose", "logs", "api", "--tail", "200"], { cwd: resolve(".."), encoding: "utf8" });
    const linha = log.split("\n").filter(l => l.includes(telefone) && l.includes("MensageriaWhatsAppFake")).at(-1);
    const codigo = linha?.match(/: (\d{6})\. Válido/)?.[1]; expect(codigo).toBeTruthy();
    const verificar = await requisicaoPublica.post(`${publico}/codigos/validar`, { data: { telefone, codigo } }); expect(verificar.ok()).toBe(true);
    const confirmado = await requisicaoPublica.post(`${publico}/agendamentos`, { data: { agendamentoId, tokenVerificacao: (await verificar.json()).tokenVerificacao, nome: "Cliente D3 Local", telefone, email: `d3-${sufixo}@teste.com` } }); expect(confirmado.status()).toBe(201);
    const token = (await confirmado.json()).tokenAgendamento;
    const detalhe = await requisicaoPublica.get(`${publico}/meus-agendamentos/${token}`); expect(detalhe.ok()).toBe(true);
    const contexto = await detalhe.json(); expect(contexto.acoes.remarcar.permitido).toBe(true); expect(contexto.acoes.cancelar.permitido).toBe(true); expect(contexto.fuso).toBe("America/Sao_Paulo"); expect(contexto.profissional.id).toBe(profissionalId);
    await page.goto(`/agendamentos/${token}`);
    await expect(page.getByText(`Profissional: D3 Local ${sufixo}`)).toBeVisible();
    await page.getByRole("button", { name: "Remarcar agendamento" }).click(); await page.getByLabel("Data da remarcação").fill(data(4));
    const consulta = await requisicaoPublica.get(`${publico}/meus-agendamentos/${token}/horarios-livres?data=${data(4)}`); expect(consulta.ok()).toBe(true);
    const slots = await consulta.json(); expect(slots.fuso).toBe(contexto.fuso); expect(slots.horarios.length).toBeGreaterThan(1);
    const horario = (iso: string) => new Intl.DateTimeFormat("pt-BR", { timeZone: slots.fuso, hour: "2-digit", minute: "2-digit" }).format(new Date(iso));
    await page.getByRole("button", { name: horario(slots.horarios[0]), exact: true }).click(); await page.getByRole("button", { name: "Revisar remarcação" }).click();
    // Ocupa o slot depois da seleção para exercitar a proteção de concorrência real.
    const cliente = await request.post(`${api}/painel/clientes`, { headers, data: { nome: `Conflito D3 ${sufixo}`, telefone: `+55718${sufixo}` } }); expect(cliente.ok()).toBe(true);
    const ocupar = await request.post(`${api}/painel/agendamentos`, { headers, data: { profissionalId, clienteId: await cliente.json(), servicoIds: [servicoId], inicio: slots.horarios[0] } }); expect(ocupar.ok()).toBe(true); agendamentos.push(await ocupar.json());
    const conflito = page.waitForResponse(r => r.url().endsWith(`/${token}/remarcar`) && r.request().method() === "POST");
    await page.getByRole("button", { name: "Confirmar remarcação" }).click(); expect((await conflito).status()).toBe(409);
    await expect(page.getByRole("main").getByRole("alert")).toContainText("acabou de ficar indisponível");
    const livres = await requisicaoPublica.get(`${publico}/meus-agendamentos/${token}/horarios-livres?data=${data(4)}`); const novoInicio = (await livres.json()).horarios[0];
    await page.getByRole("button", { name: horario(novoInicio), exact: true }).click(); await page.getByRole("button", { name: "Revisar remarcação" }).click();
    const resposta200 = page.waitForResponse(r => r.url().endsWith(`/${token}/remarcar`) && r.request().method() === "POST"); await page.getByRole("button", { name: "Confirmar remarcação" }).click();
    const canonico = await resposta200; expect(canonico.status()).toBe(200); expect(canonico.request().headers().prefer).toBe("return=representation"); expect(new Date((await canonico.json()).inicio).getTime()).toBe(new Date(novoInicio).getTime());
    await expect(page.getByRole("status")).toHaveText("Agendamento remarcado.");
    page.once("dialog", d => d.accept()); const cancelado = page.waitForResponse(r => r.url().endsWith(`/${token}/cancelar`)); await page.getByRole("button", { name: "Cancelar agendamento" }).click(); expect((await cancelado).status()).toBe(200);
    await expect(page.getByText("Status: Cancelado")).toBeVisible(); await expect(page.getByRole("button", { name: "Remarcar agendamento" })).toHaveCount(0);
  } finally {
    for (const id of agendamentos) await request.post(`${api}/painel/agendamentos/${id}/cancelar`, { headers });
    if (profissionalId) await request.post(`${api}/painel/profissionais/${profissionalId}/desativar`, { headers });
    await requisicaoPublica.dispose();
  }
});
