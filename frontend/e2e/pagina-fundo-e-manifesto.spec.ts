import { test, expect, type APIRequestContext, type Page } from "@playwright/test";

/**
 * Item 15 da seção 14: cor e imagem de fundo do negócio na página (6.1) e no assistente (6.2), e os dois PWAs (seção 5,
 * "Dois PWAs instaláveis separados"), cada um com o manifesto do próprio host. Roda contra o `docker compose up`.
 */

const API_BASE = "http://localhost:5080";
const PAINEL_BASE = "http://app.agendeiiai.localhost:3000";
const PAGINA_BASE = "http://acme.agendeiiai.localhost:3000";
const ADMIN_EMAIL = "admin@acme.dev";
const ADMIN_SENHA = "Admin!123";

async function tokenAdmin(request: APIRequestContext): Promise<string> {
  const resposta = await request.post(`${API_BASE}/painel/auth/login`, { data: { email: ADMIN_EMAIL, senha: ADMIN_SENHA } });
  expect(resposta.ok()).toBeTruthy();
  return (await resposta.json()).accessToken as string;
}

/** O PUT do perfil substitui tudo: parte do que está gravado e muda só a cor. */
async function definirCorFundo(request: APIRequestContext, token: string, corFundo: string | null) {
  const cabecalhos = { Authorization: `Bearer ${token}` };
  const perfil = await (await request.get(`${API_BASE}/painel/negocio`, { headers: cabecalhos })).json();
  const resposta = await request.put(`${API_BASE}/painel/negocio`, { headers: cabecalhos, data: { ...perfil, corFundo } });
  expect(resposta.status()).toBe(204);
}

async function removerImagem(request: APIRequestContext, token: string) {
  await request.delete(`${API_BASE}/painel/negocio/imagem-fundo`, { headers: { Authorization: `Bearer ${token}` } });
}

async function estiloFundo(page: Page, testId: string) {
  return page.getByTestId(testId).evaluate((elemento) => {
    const estilo = getComputedStyle(elemento);
    return { cor: estilo.backgroundColor, imagem: estilo.backgroundImage };
  });
}

async function entrarNoPainel(page: Page) {
  await page.goto(`${PAINEL_BASE}/painel/login`);
  await page.getByLabel("E-mail").fill(ADMIN_EMAIL);
  await page.getByLabel("Senha").fill(ADMIN_SENHA);
  await page.getByRole("button", { name: "Entrar" }).click();
  await expect(page).toHaveURL(/\/painel$/);
}

test.describe("fundo da página do negócio", () => {
  test.afterEach(async ({ request }) => {
    const token = await tokenAdmin(request);
    await removerImagem(request, token);
    await definirCorFundo(request, token, null);
  });

  test("cor configurada vale na página e no assistente; sem imagem, só a cor", async ({ page, request }) => {
    const token = await tokenAdmin(request);
    await removerImagem(request, token);
    await definirCorFundo(request, token, "#123456");

    await page.goto(PAGINA_BASE);
    const pagina = await estiloFundo(page, "pagina-negocio");
    expect(pagina.cor).toBe("rgb(18, 52, 86)");
    expect(pagina.imagem).toBe("none");
    // O conteúdo continua num painel do tema (legível), por cima do fundo.
    await expect(page.getByTestId("conteudo-pagina")).toBeVisible();

    await page.getByRole("button", { name: "Agendar Agora" }).first().click();
    const assistente = await estiloFundo(page, "assistente-agendamento");
    expect(assistente.cor).toBe("rgb(18, 52, 86)");
    expect(assistente.imagem).toBe("none");
    await expect(page.getByTestId("assistente-marca")).toBeVisible();
  });

  test("imagem enviada pelo painel aparece na página e no assistente; remover volta para a cor", async ({ page, request }) => {
    const token = await tokenAdmin(request);
    await removerImagem(request, token);
    await definirCorFundo(request, token, "#123456");

    // Uma imagem de verdade qualquer: a captura da tela de login do painel (PNG).
    await entrarNoPainel(page);
    const png = await page.screenshot();
    await page.goto(`${PAINEL_BASE}/painel/negocio`);

    // Arquivo que não é imagem: recusado pela API, com a mensagem na tela.
    await page.getByLabel("Imagem de fundo").setInputFiles({ name: "falso.png", mimeType: "image/png", buffer: Buffer.from("<html></html>") });
    await expect(page.getByText(/não é uma imagem válida/)).toBeVisible();

    await page.getByLabel("Imagem de fundo").setInputFiles({ name: "fundo.png", mimeType: "image/png", buffer: png });
    await expect(page.getByRole("button", { name: "Remover imagem" })).toBeVisible();
    expect((await estiloFundo(page, "previa-fundo")).imagem).toContain("/arquivos/");

    await page.goto(PAGINA_BASE);
    const pagina = await estiloFundo(page, "pagina-negocio");
    const url = /url\("([^"]+)"\)/.exec(pagina.imagem)?.[1];
    expect(url).toMatch(/\/arquivos\/[0-9a-f]{32}$/);
    expect(pagina.cor).toBe("rgb(18, 52, 86)");

    // Reprocessada: sai em WebP, qualquer que tenha sido o formato enviado.
    const arquivo = await request.get(url!);
    expect(arquivo.status()).toBe(200);
    expect(arquivo.headers()["content-type"]).toBe("image/webp");

    await page.getByRole("button", { name: "Agendar Agora" }).first().click();
    expect((await estiloFundo(page, "assistente-agendamento")).imagem).toContain(url!);

    // Remover pelo painel: a página volta a usar só a cor, sem quebrar o layout.
    await page.goto(`${PAINEL_BASE}/painel/negocio`);
    page.once("dialog", (dialogo) => dialogo.accept());
    await page.getByRole("button", { name: "Remover imagem" }).click();
    await expect(page.getByRole("button", { name: "Remover imagem" })).not.toBeVisible();

    await page.goto(PAGINA_BASE);
    const semImagem = await estiloFundo(page, "pagina-negocio");
    expect(semImagem.imagem).toBe("none");
    expect(semImagem.cor).toBe("rgb(18, 52, 86)");
    await expect(page.getByRole("heading", { level: 1 })).toBeVisible();
    expect((await request.get(url!)).status()).toBe(404);
  });

  test("cor de fundo salva pelo formulário do painel", async ({ page }) => {
    await entrarNoPainel(page);
    await page.goto(`${PAINEL_BASE}/painel/negocio`);
    await page.locator("#cor-fundo").fill("#0a7a3c");
    await page.getByRole("button", { name: "Salvar alterações" }).click();
    await expect(page.getByText("Perfil atualizado.")).toBeVisible();

    await page.goto(PAGINA_BASE);
    expect((await estiloFundo(page, "pagina-negocio")).cor).toBe("rgb(10, 122, 60)");
  });
});

test.describe("manifestos dos dois PWAs", () => {
  async function manifesto(page: Page, base: string) {
    const resposta = await page.goto(`${base}/manifest.webmanifest`);
    expect(resposta!.status()).toBe(200);
    expect(resposta!.headers()["content-type"]).toContain("application/manifest+json");
    expect(resposta!.headers()["vary"]).toContain("Host");
    return { json: await resposta!.json(), texto: await resposta!.text() };
  }

  test("o do painel traz a marca do produto e abre em /painel/login", async ({ page }) => {
    const { json } = await manifesto(page, PAINEL_BASE);

    expect(json.name).toBe("Agendeiiai");
    expect(json.start_url).toBe("/painel/login");
    expect(json.icons.map((i: { src: string }) => i.src)).toEqual(expect.arrayContaining(["/brand/icone-192.png", "/brand/maskable-512.png"]));
    expect(json.icons.some((i: { purpose?: string }) => i.purpose === "maskable")).toBeTruthy();

    // A página do painel aponta para ele, e para o ícone do produto.
    await page.goto(`${PAINEL_BASE}/painel/login`);
    await expect(page.locator('link[rel="manifest"]')).toHaveAttribute("href", "/manifest.webmanifest");
    await expect(page.locator('link[rel="apple-touch-icon"]')).toHaveAttribute("href", "/brand/apple-icon.png");
  });

  test("o do negócio traz nome e ícone dele, nunca a marca do produto", async ({ page, request }) => {
    const token = await tokenAdmin(request);
    const negocio = await (await request.get(`${API_BASE}/painel/negocio`, { headers: { Authorization: `Bearer ${token}` } })).json();

    const { json, texto } = await manifesto(page, PAGINA_BASE);

    expect(json.name).toBe(negocio.nomeExibido);
    expect(json.start_url).toBe("/");
    expect(json.icons.every((i: { src: string }) => i.src.startsWith("/icone/"))).toBeTruthy();
    expect(texto).not.toContain("Agendeiiai");
    expect(texto).not.toContain("/brand/");

    // Os ícones do negócio existem nesse host e não no do painel (os dois nunca se misturam).
    for (const tamanho of ["180", "192", "512", "maskable-512"]) {
      const icone = await page.goto(`${PAGINA_BASE}/icone/${tamanho}`);
      expect(icone!.status()).toBe(200);
      expect(icone!.headers()["content-type"]).toBe("image/png");
    }
    expect((await page.goto(`${PAINEL_BASE}/icone/192`))!.status()).toBe(404);

    await page.goto(PAGINA_BASE);
    await expect(page.locator('link[rel="manifest"]')).toHaveAttribute("href", "/manifest.webmanifest");
    await expect(page.locator('link[rel="apple-touch-icon"]')).toHaveAttribute("href", "/icone/180");
  });
});
