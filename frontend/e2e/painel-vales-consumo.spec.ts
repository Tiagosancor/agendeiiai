import { test, expect } from "@playwright/test";

/**
 * Vale e consumo interno (seção 7, item 12 d/e): o consumo lançado no Estoque (pelo custo, editável) e o vale lançado em
 * Comissões viram saldo devedor do profissional, com editar/excluir na aba "Vales e consumo". A quitação no fechamento
 * da quinzena é coberta pelos testes de integração do backend. Vale também para quem vende sem cadastro de profissional
 * (com acerto por quinzena na comissão de produto do usuário).
 */

const PAINEL_BASE = "http://app.agendeiiai.localhost:3000";
const API = "http://localhost:5080";

test("consumo no estoque e vale viram saldo devedor do profissional", async ({ page, request }) => {
  test.setTimeout(90_000);
  const login = await request.post(`${API}/painel/auth/login`, { data: { email: "admin@acme.dev", senha: "Admin!123" } });
  const auth = { Authorization: `Bearer ${(await login.json()).accessToken}` };
  const sufixo = Date.now().toString().slice(-6);
  const nomeProfissional = `Zz Vale ${sufixo}`;
  const nomeProduto = `Zz Gel consumo ${sufixo}`;

  const profissionalId: string = await (await request.post(`${API}/painel/profissionais`, { headers: auth, data: { nome: nomeProfissional } })).json();
  const produtoId: string = await (
    await request.post(`${API}/painel/estoque/produtos`, {
      headers: auth,
      data: { nome: nomeProduto, categoria: null, precoCusto: 8, precoVenda: 25, quantidadeInicial: 5, quantidadeMinima: 1 },
    })
  ).json();

  await page.goto(`${PAINEL_BASE}/painel/login`);
  await page.getByLabel("E-mail").fill("admin@acme.dev");
  await page.getByLabel("Senha").fill("Admin!123");
  await page.getByRole("button", { name: "Entrar" }).click();
  await expect(page).toHaveURL(/\/painel$/);

  // Consumo interno: sugerido pelo custo, 2 unidades = R$ 16,00 de saldo devedor.
  await page.goto(`${PAINEL_BASE}/painel/estoque`);
  await page.getByRole("row", { name: new RegExp(nomeProduto) }).getByRole("button", { name: "Consumo" }).click();
  let modal = page.getByRole("dialog", { name: `Consumo interno — ${nomeProduto}` });
  await modal.getByLabel("Profissional").selectOption({ label: nomeProfissional });
  await expect(modal.getByLabel("Valor por unidade (R$)")).toHaveValue("8.00");
  await modal.getByLabel("Quantidade").fill("2");
  await expect(modal.getByText("Saldo devedor: R$ 16,00")).toBeVisible();
  await modal.getByRole("button", { name: "Lançar consumo" }).click();
  await expect(modal).toBeHidden();
  await expect(page.getByRole("row", { name: new RegExp(nomeProduto) }).getByRole("cell", { name: /^3/ })).toBeVisible();

  // Vale em Comissões → "Vales e consumo".
  await page.goto(`${PAINEL_BASE}/painel/comissoes`);
  await page.getByRole("tab", { name: "Vales e consumo" }).click();
  await page.getByRole("button", { name: "Lançar vale" }).click();
  modal = page.getByRole("dialog", { name: "Lançar vale" });
  await modal.getByLabel("Profissional").selectOption({ label: nomeProfissional });
  await modal.getByLabel("Valor (R$)").fill("50");
  await modal.getByLabel("Motivo (opcional)").fill("Adiantamento");
  await modal.getByRole("button", { name: "Lançar vale" }).click();
  await expect(modal).toBeHidden();

  const linha = page.getByRole("button", { name: new RegExp(nomeProfissional) });
  await expect(linha).toContainText("Vales R$ 50,00 · consumo R$ 16,00");
  await expect(linha).toContainText("R$ 66,00");

  // Detalhe: editar o vale e excluir o consumo (o produto volta ao estoque).
  await linha.click();
  const vale = page.getByRole("listitem").filter({ hasText: "Adiantamento" });
  await vale.getByRole("button", { name: "Editar" }).click();
  modal = page.getByRole("dialog", { name: "Editar vale" });
  await modal.getByLabel("Valor (R$)").fill("40");
  await modal.getByRole("button", { name: "Salvar" }).click();
  await expect(modal).toBeHidden();
  await expect(vale).toContainText("R$ 40,00");

  page.once("dialog", (dialogo) => dialogo.accept());
  await page.getByRole("listitem").filter({ hasText: nomeProduto }).getByRole("button", { name: "Excluir" }).click();
  await expect(page.getByRole("listitem").filter({ hasText: nomeProduto })).toHaveCount(0);

  const produtos = await (await request.get(`${API}/painel/estoque/produtos`, { headers: auth })).json();
  expect(produtos.find((p: { id: string }) => p.id === produtoId).quantidadeEstoque).toBe(5);

  // Limpa: o vale sai e o profissional/produto de teste saem de uso.
  const saldo = await (await request.get(`${API}/painel/saldos/profissionais/${profissionalId}`, { headers: auth })).json();
  for (const l of saldo.lancamentos) await request.delete(`${API}/painel/saldos/${l.id}`, { headers: auth });
  await request.post(`${API}/painel/profissionais/${profissionalId}/desativar`, { headers: auth });
  await request.post(`${API}/painel/estoque/produtos/${produtoId}/desativar`, { headers: auth });
});

test("vendedor sem cadastro de profissional com acerto por quinzena recebe vale", async ({ page, request }) => {
  test.setTimeout(90_000);
  const login = await request.post(`${API}/painel/auth/login`, { data: { email: "admin@acme.dev", senha: "Admin!123" } });
  const auth = { Authorization: `Bearer ${(await login.json()).accessToken}` };
  const sufixo = Date.now().toString().slice(-6);
  const nome = `Zz Recepcao ${sufixo}`;
  const criado = await request.post(`${API}/painel/usuarios`, {
    headers: auth,
    data: { nome, email: `zz.recepcao.${sufixo}@acme.dev`, senha: "Recepcao!123", perfil: "Recepcionista" },
  });
  expect(criado.status(), await criado.text()).toBe(201);
  const usuarioId: string = await criado.json();

  await page.goto(`${PAINEL_BASE}/painel/login`);
  await page.getByLabel("E-mail").fill("admin@acme.dev");
  await page.getByLabel("Senha").fill("Admin!123");
  await page.getByRole("button", { name: "Entrar" }).click();
  await expect(page).toHaveURL(/\/painel$/);

  // Acerto por quinzena na comissão de produto do usuário.
  await page.goto(`${PAINEL_BASE}/painel/usuarios`);
  await page.getByRole("row", { name: new RegExp(nome) }).getByRole("button", { name: "Comissão de produto" }).click();
  let modal = page.getByRole("dialog", { name: `Comissão de produto — ${nome}` });
  await modal.getByLabel("Comissão de produto (%)").fill("5");
  await modal.getByRole("checkbox", { name: /Acerto por quinzena/ }).check();
  await modal.getByRole("button", { name: "Salvar" }).click();
  await expect(modal).toBeHidden();

  // Ele aparece nos saldos e recebe o vale, no grupo dos vendedores.
  await page.goto(`${PAINEL_BASE}/painel/comissoes`);
  await page.getByRole("tab", { name: "Vales e consumo" }).click();
  await page.getByRole("button", { name: "Lançar vale" }).click();
  modal = page.getByRole("dialog", { name: "Lançar vale" });
  await modal.getByLabel("Profissional ou vendedor").selectOption({ label: nome });
  await modal.getByLabel("Valor (R$)").fill("30");
  await modal.getByRole("button", { name: "Lançar vale" }).click();
  await expect(modal).toBeHidden();

  const linha = page.getByRole("button", { name: new RegExp(nome) });
  await expect(linha).toContainText("Vales R$ 30,00");
  await linha.click();
  await expect(page.getByRole("listitem").filter({ hasText: "R$ 30,00" })).toBeVisible();

  // Limpa: o vale sai e o usuário é excluído (com o vale no histórico, a exclusão é lógica).
  const saldo = await (await request.get(`${API}/painel/saldos/usuarios/${usuarioId}`, { headers: auth })).json();
  for (const l of saldo.lancamentos) await request.delete(`${API}/painel/saldos/${l.id}`, { headers: auth });
  await request.delete(`${API}/painel/usuarios/${usuarioId}`, { headers: auth });
});
