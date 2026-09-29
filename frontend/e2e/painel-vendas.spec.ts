import { test, expect, type APIRequestContext, type Page } from "@playwright/test";

/**
 * Venda de produto (seção 7, item 12 c): venda avulsa no balcão com vendedor escolhido e desconto pontual, e a seção
 * "Produtos vendidos" do "Concluir atendimento". Comissão, concorrência e faturamento em detalhe ficam nos testes de
 * integração do backend.
 */

const PAINEL_BASE = "http://app.agendeiiai.localhost:3000";
const API = "http://localhost:5080";

async function entrar(page: Page) {
  await page.goto(`${PAINEL_BASE}/painel/login`);
  await page.getByLabel("E-mail").fill("admin@acme.dev");
  await page.getByLabel("Senha").fill("Admin!123");
  await page.getByRole("button", { name: "Entrar" }).click();
  await expect(page).toHaveURL(/\/painel$/);
}

async function autenticar(request: APIRequestContext) {
  const login = await request.post(`${API}/painel/auth/login`, { data: { email: "admin@acme.dev", senha: "Admin!123" } });
  return { Authorization: `Bearer ${(await login.json()).accessToken}` };
}

async function criarProduto(request: APIRequestContext, auth: Record<string, string>, nome: string) {
  const resposta = await request.post(`${API}/painel/estoque/produtos`, {
    headers: auth,
    data: { nome, categoria: null, precoCusto: 10, precoVenda: 30, quantidadeInicial: 10, quantidadeMinima: 3 },
  });
  expect(resposta.status()).toBe(201);
  return (await resposta.json()) as string;
}

test("venda avulsa no balcão com vendedor escolhido e desconto pontual", async ({ page, request }) => {
  test.setTimeout(90_000);
  const auth = await autenticar(request);
  const sufixo = Date.now().toString().slice(-6);
  const produto = `Zz Gel ${sufixo}`;
  const produtoId = await criarProduto(request, auth, produto);
  const opcoes = await (await request.get(`${API}/painel/vendas/opcoes`, { headers: auth })).json();
  const vendedor = opcoes.vendedores.find((v: { nome: string }) => v.nome !== opcoes.vendedorSugerido?.nome) ?? opcoes.vendedores[0];

  await entrar(page);
  await page.goto(`${PAINEL_BASE}/painel/vendas`);
  await page.getByRole("button", { name: "Nova venda" }).click();
  const modal = page.getByRole("dialog", { name: "Nova venda" });

  await modal.getByRole("button", { name: "+ Cadastrar cliente novo" }).click();
  await modal.getByLabel("Nome", { exact: true }).fill(`Cliente ${sufixo}`);
  await modal.getByRole("combobox", { name: "Produto", exact: true }).selectOption(produtoId);
  await expect(modal.getByLabel("Valor unit. (R$)")).toHaveValue("30.00");
  await modal.getByLabel("Qtd.").fill("2");
  await modal.getByLabel("Valor unit. (R$)").fill("27.5");
  await expect(modal.getByText("Preço de tabela: R$ 30,00")).toBeVisible();
  await modal.getByLabel("Vendido por").selectOption({ label: vendedor.nome });
  await expect(modal.getByText("Total dos produtos: R$ 55,00")).toBeVisible();
  await modal.getByRole("button", { name: "Lançar venda" }).click();
  await expect(modal).toBeHidden();

  const venda = page.getByRole("article").filter({ hasText: produto });
  await expect(venda).toContainText(`${produto} × 2`);
  await expect(venda).toContainText(`Vendido por ${vendedor.nome}`);
  await expect(venda).toContainText(`Cliente ${sufixo}`);
  await expect(venda).toContainText("R$ 55,00");

  // Estoque baixou pela venda (histórico com o movimento de venda).
  const produtos = await (await request.get(`${API}/painel/estoque/produtos`, { headers: auth })).json();
  expect(produtos.find((p: { id: string }) => p.id === produtoId).quantidadeEstoque).toBe(8);

  // Estorno: a venda fica na lista, marcada, e os produtos voltam ao estoque.
  await venda.getByRole("button", { name: "Estornar" }).click();
  const estorno = page.getByRole("dialog", { name: "Estornar venda" });
  await estorno.getByLabel("Motivo (obrigatório)").fill("Cliente desistiu");
  await estorno.getByRole("button", { name: "Estornar" }).click();
  await expect(estorno).toBeHidden();
  await expect(venda).toContainText("Estornada: Cliente desistiu");
  await expect(venda.getByRole("button", { name: "Estornar" })).toHaveCount(0);
  const depois = await (await request.get(`${API}/painel/estoque/produtos`, { headers: auth })).json();
  expect(depois.find((p: { id: string }) => p.id === produtoId).quantidadeEstoque).toBe(10);

  await request.post(`${API}/painel/estoque/produtos/${produtoId}/desativar`, { headers: auth });
});

test("concluir atendimento lança os produtos vendidos, com o profissional sugerido como vendedor", async ({ page, request }) => {
  test.setTimeout(120_000);
  const auth = await autenticar(request);
  const sufixo = Date.now().toString().slice(-6);
  const produto = `Zz Balm ${sufixo}`;
  const produtoId = await criarProduto(request, auth, produto);

  const profissional = (await (await request.get(`${API}/painel/profissionais`, { headers: auth })).json()).find(
    (p: { ativo: boolean }) => p.ativo,
  );
  const servico = (await (await request.get(`${API}/painel/profissionais/${profissional.id}/servicos`, { headers: auth })).json())[0];

  // Atendimento num horário livre daqui a algumas semanas (encaixe pela API, cliente de balcão).
  let horario: string | undefined;
  let data = "";
  for (let dias = 30; !horario && dias < 80; dias++) {
    data = new Date(Date.now() + dias * 86400000).toISOString().slice(0, 10);
    const livres = await (
      await request.get(`${API}/painel/profissionais/${profissional.id}/horarios-livres?data=${data}&duracaoMinutos=${servico.duracaoMinutos}`, {
        headers: auth,
      })
    ).json();
    horario = livres[0];
  }
  expect(horario).toBeTruthy();
  const cliente = `Cliente balm ${sufixo}`;
  const encaixe = await request.post(`${API}/painel/encaixes`, {
    headers: auth,
    data: {
      profissionalId: profissional.id,
      clienteId: null,
      novoCliente: { nome: cliente, telefone: null },
      servicoIds: [servico.servicoId],
      inicio: horario,
      iniciarAtendimento: false,
      clienteAutorizouMensagens: false,
    },
  });
  expect(encaixe.status(), await encaixe.text()).toBe(201);
  const { agendamentoId } = await encaixe.json();

  await entrar(page);
  await page.goto(`${PAINEL_BASE}/painel/agenda`);
  await page.getByRole("tab", { name: "Por profissional" }).click();
  await page.getByLabel("Profissional").selectOption(profissional.id);
  await page.getByLabel("Data").fill(data);
  const linha = page.locator("div.flex.flex-wrap.items-center.justify-between").filter({ hasText: cliente });
  await linha.getByRole("button", { name: "Concluir" }).click();

  const modal = page.getByRole("dialog", { name: "Concluir atendimento" });
  const secao = modal.getByRole("region", { name: "Produtos vendidos" });
  await secao.getByRole("button", { name: "+ Adicionar produto" }).click();
  await secao.getByRole("combobox", { name: "Produto", exact: true }).selectOption(produtoId);
  await expect(secao.getByLabel("Vendido por")).toHaveValue(`p:${profissional.id}`);
  await modal.getByRole("button", { name: "Concluir atendimento" }).click();
  await expect(modal).toBeHidden();
  await expect(linha).toContainText("Concluído");

  await page.goto(`${PAINEL_BASE}/painel/vendas`);
  const venda = page.getByRole("article").filter({ hasText: produto });
  await expect(venda).toContainText(`Vendido por ${profissional.nome}`);
  await expect(venda).toContainText("no atendimento");
  await expect(venda).toContainText("R$ 30,00");

  // Limpa: o atendimento de teste volta e é cancelado (a venda fica no histórico); o produto sai de uso.
  await request.post(`${API}/painel/agendamentos/${agendamentoId}/reabrir`, { headers: auth });
  await request.post(`${API}/painel/agendamentos/${agendamentoId}/cancelar`, { headers: auth });
  await request.post(`${API}/painel/estoque/produtos/${produtoId}/desativar`, { headers: auth });
});
