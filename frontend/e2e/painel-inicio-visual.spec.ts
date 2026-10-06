import { test, expect, type Page } from "@playwright/test";

const BASE = "http://app.agendeiiai.localhost:3000";
const API = "http://localhost:5080";
test.use({ serviceWorkers: "block" });
const passos = { servicosCadastrados: true, profissionaisCadastrados: false, horariosConfigurados: false,
  linkCopiado: false, dispensado: false, linkAgendamento: "http://acme.agendeiiai.localhost:3000/", exibir: true };
const alertas = { esgotados: [{ id: "produto-teste-1", nome: "Pomada", quantidadeEstoque: 0, quantidadeMinima: 3 }],
  estoqueBaixo: [{ id: "produto-teste-2", nome: "Shampoo", quantidadeEstoque: 2, quantidadeMinima: 3 }] };

async function entrar(page: Page, tema: string) {
  await page.addInitScript(tema => localStorage.setItem("tema", tema), tema);
  await page.goto(`${BASE}/painel/login`);
  await page.getByLabel("E-mail").fill("admin@acme.dev");
  await page.getByLabel("Senha", { exact: true }).fill("Admin!123");
  await page.getByRole("button", { name: "Entrar", exact: true }).click();
  await expect(page).toHaveURL(/\/painel$/);
}

for (const tema of ["light", "dark"]) {
  test(`Início: dados existentes, ações e custo das chamadas ${tema}`, async ({ page }, testInfo) => {
    let checklist = { ...passos };
    const chamadas: string[] = [];
    const erros: string[] = [];
    page.on("pageerror", erro => erros.push(erro.message));
    await page.route(`${API}/painel/**`, async route => {
      const req = route.request(), path = new URL(req.url()).pathname;
      if (path.startsWith("/painel/auth/")) return route.continue();
      chamadas.push(`${req.method()} ${path}`);
      if (path === "/painel/assinatura/aviso") return route.fulfill({ status: 204 });
      if (path === "/painel/estoque/alertas") return route.fulfill({ json: alertas });
      if (path === "/painel/primeiros-passos") return route.fulfill({ json: checklist });
      if (path.endsWith("/link-copiado")) {
        checklist = { ...checklist, linkCopiado: true };
        return route.fulfill({ status: 204 });
      }
      if (path.endsWith("/dispensar")) {
        checklist = { ...checklist, exibir: false, dispensado: true };
        return route.fulfill({ status: 204 });
      }
      throw new Error(`Consulta adicional inesperada: ${path}`);
    });
    await entrar(page, tema);
    await expect(page.getByRole("progressbar")).toHaveAttribute("aria-valuenow", "1");
    await expect(page.getByRole("status")).toContainText("1 produto esgotado · 1 com estoque baixo");
    expect(chamadas.filter(c => c === "GET /painel/primeiros-passos")).toHaveLength(1);
    expect(chamadas.filter(c => c === "GET /painel/estoque/alertas")).toHaveLength(1);
    const inicio = page.locator(".painel-inicio");
    await expect(inicio.getByRole("link", { name: "Cadastre a equipe" })).toHaveAttribute("href", "/painel/profissionais");
    await expect(inicio.getByRole("link", { name: "Configure os horários de trabalho" })).toHaveAttribute("href", "/painel/profissionais");
    await expect(inicio.getByRole("link", { name: "Cadastre seus serviços" })).toHaveCount(0);
    await expect(inicio.getByRole("link", { name: /Ver agenda/ })).toHaveAttribute("href", "/painel/agenda");
    await expect(inicio.getByRole("link", { name: /Estoque:/ })).toHaveAttribute("href", "/painel/estoque");
    for (const width of [375, 390, 430, 768, 1024, 1440]) {
      await page.setViewportSize({ width, height: 932 });
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `${width}px`).toBe(true);
      for (const controle of await inicio.locator("a, button").all()) {
        const caixa = (await controle.boundingBox())!;
        expect(caixa.height).toBeGreaterThanOrEqual(44);
        expect(caixa.x + caixa.width).toBeLessThanOrEqual(width);
      }
      await page.screenshot({ path: testInfo.outputPath(`inicio-${tema}-${width}.png`), fullPage: true });
    }
    const total = chamadas.length;
    await page.getByRole("button", { name: "Copiar", exact: true }).click();
    await expect(page.getByRole("button", { name: "Copiado", exact: true })).toBeVisible();
    await expect(page.getByRole("progressbar")).toHaveAttribute("aria-valuenow", "2");
    await page.getByRole("button", { name: "Dispensar", exact: true }).click();
    await expect(page.getByTestId("primeiros-passos")).toHaveCount(0);
    expect(chamadas.slice(total)).toEqual(["POST /painel/primeiros-passos/link-copiado", "POST /painel/primeiros-passos/dispensar"]);
    expect(erros).toEqual([]);
  });
}

for (const permissoes of [[], ["VenderProdutos"]]) {
  test(`Início preserva visibilidade do estoque para ${permissoes.length ? "vendedor" : "usuário sem acesso ao estoque"}`, async ({ page }) => {
    // Token somente para simular a UI; nenhuma chamada com ele chega à API real.
    const token = `e30.${Buffer.from(JSON.stringify({ permissao: permissoes })).toString("base64url")}.teste`;
    let logado = false, consultasEstoque = 0;
    await page.route("**/painel/auth/**", async route => {
      if (route.request().url().endsWith("/login")) logado = true;
      return route.fulfill(logado ? { json: { accessToken: token, expiraEm: "2099-01-01T00:00:00Z" } } : { status: 401 });
    });
    await page.route(`${API}/painel/**`, async route => {
      const path = new URL(route.request().url()).pathname;
      if (path === "/painel/assinatura/aviso") return route.fulfill({ status: 204 });
      if (path === "/painel/primeiros-passos") return route.fulfill({ json: { ...passos, exibir: false } });
      if (path === "/painel/estoque/alertas") { consultasEstoque++; return route.fulfill({ json: alertas }); }
      throw new Error(`Chamada inesperada: ${path}`);
    });
    await entrar(page, "dark");
    await expect(page.getByRole("heading", { name: "Início", exact: true })).toBeVisible();
    await expect(page.locator(".painel-inicio-alerta")).toHaveCount(permissoes.length ? 1 : 0);
    await expect(page.locator('.painel-inicio a[href="/painel/estoque"]')).toHaveCount(0);
    expect(consultasEstoque).toBe(permissoes.length ? 1 : 0);
    await expect(page.locator(".painel-inicio").getByRole("link", { name: /Ver agenda/ })).toBeVisible();
  });
}

test("Início mantém ausência de dados e falhas sem exibir números fictícios", async ({ page }) => {
  let erro = false;
  await page.route(`${API}/painel/**`, async route => {
    const path = new URL(route.request().url()).pathname;
    if (path.startsWith("/painel/auth/")) return route.continue();
    if (path === "/painel/assinatura/aviso") return route.fulfill({ status: 204 });
    if (erro) return route.fulfill({ status: 500, json: { detail: "Falha simulada" } });
    return route.fulfill({ json: path === "/painel/estoque/alertas" ? { esgotados: [], estoqueBaixo: [] } : { ...passos, exibir: false } });
  });
  await entrar(page, "light");
  await expect(page.locator(".painel-inicio-agenda")).toBeVisible();
  await expect(page.locator(".painel-inicio-alerta")).toHaveCount(0);
  await expect(page.getByTestId("primeiros-passos")).toHaveCount(0);
  erro = true;
  await page.reload();
  await expect(page.locator(".painel-inicio-agenda")).toBeVisible();
  await expect(page.locator(".painel-inicio-alerta")).toHaveCount(0);
  await expect(page.getByRole("progressbar")).toHaveCount(0);
});
