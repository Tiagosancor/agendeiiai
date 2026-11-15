import { expect, test, type Page } from "@playwright/test";

const RAIZ = "http://agendeiiai.localhost:3000";
test.use({ serviceWorkers: "block" });

async function preparar(page: Page) {
  await page.route("**/*", rota => {
    const caminho = new URL(rota.request().url()).pathname;
    if (caminho === "/plataforma/auth/login") return rota.fulfill({ json: { accessToken: "token-layout" } });
    if (caminho === "/plataforma/negocios") return rota.fulfill({ json: [{ id: "negocio-layout", nome: "Negócio de teste do layout", slug: "layout", tipo: "Salao", plano: null, estado: null, fimTeste: null, proximoVencimento: null, cadastradoEm: "2026-10-01T12:00:00Z", agenteIaContratado: false, agenteIaOrigem: null, whatsAppConectado: false }] });
    if (caminho === "/plataforma/whatsapp/saude") return rota.fulfill({ json: { estado: "NaoSeAplica", provedor: "Fake", detalhe: null } });
    if (caminho === "/plataforma/notificacoes/resumo") return rota.fulfill({ json: { pendentes: 0, enviando: 0, falhas: 0, desconhecidas: 0, idadeDaPendenciaMaisAntigaSegundos: null, falhasRecentes: [] } });
    if (caminho === "/plataforma/notificacoes/outbox") return rota.fulfill({ json: { itens: [], pagina: 1, tamanho: 50, total: 0 } });
    return rota.continue();
  });
  await page.goto(`${RAIZ}/plataforma`);
}

async function entrar(page: Page) {
  await page.getByLabel("E-mail").fill("layout@plataforma.dev");
  await page.getByLabel("Senha", { exact: true }).fill("SenhaSegura123");
  await page.getByRole("button", { name: "Entrar", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Negócios", exact: true })).toBeVisible();
}

test("sidebar mantém a sessão entre rotas, rota ativa e saída", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await preparar(page);
  await expect(page.getByRole("navigation", { name: "Navegação da plataforma" })).toHaveCount(0);
  const temaAnterior = await page.locator("html").getAttribute("data-theme");
  await page.getByTestId("botao-tema").click();
  await expect(page.locator("html")).not.toHaveAttribute("data-theme", temaAnterior ?? "");
  await entrar(page);
  const menu = page.getByRole("navigation", { name: "Navegação da plataforma" });
  await expect(menu.getByRole("link", { name: "Negócios" })).toHaveAttribute("aria-current", "page");
  await menu.getByRole("link", { name: "Notificações" }).click();
  await expect(page.getByRole("heading", { name: "Notificações", exact: true })).toBeVisible();
  await expect(menu.getByRole("link", { name: "Notificações" })).toHaveAttribute("aria-current", "page");
  await expect(menu.getByRole("link", { name: "Negócios" })).not.toHaveAttribute("aria-current");
  await expect(page.getByRole("main")).toHaveCount(1);
  await page.getByRole("button", { name: "Sair" }).click();
  await expect(page.getByRole("heading", { name: "Administração da plataforma" })).toBeVisible();
});

for (const largura of [375, 768]) {
  test(`gaveta prende o foco, fecha com Esc e navega em ${largura}px`, async ({ page }) => {
    await page.setViewportSize({ width: largura, height: 900 });
    await preparar(page);
    await entrar(page);
    const tabela = page.getByRole("region", { name: "Lista de negócios" });
    await expect(tabela).toBeVisible();
    expect(await tabela.evaluate(elemento => elemento.scrollWidth > elemento.clientWidth)).toBe(true);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    const abrir = page.getByRole("button", { name: "Abrir menu" });
    await abrir.click();
    const gaveta = page.getByRole("dialog", { name: "Menu principal" });
    const fechar = gaveta.getByRole("button", { name: "Fechar menu" });
    await expect(fechar).toBeFocused();
    await page.keyboard.press("Shift+Tab");
    await expect(gaveta.getByRole("button", { name: "Sair" })).toBeFocused();
    await page.keyboard.press("Tab");
    await expect(fechar).toBeFocused();
    await page.keyboard.press("Escape");
    await expect(gaveta).toHaveCount(0);
    await expect(abrir).toBeFocused();
    await abrir.click();
    await page.getByRole("navigation", { name: "Navegação da plataforma" }).getByRole("link", { name: "Notificações" }).click();
    await expect(page.getByRole("heading", { name: "Notificações", exact: true })).toBeVisible();
    await expect(abrir).toHaveAttribute("aria-expanded", "false");
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  });
}
