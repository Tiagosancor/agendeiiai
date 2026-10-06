import { test, expect, type Page } from "@playwright/test";

const BASE = "http://app.agendeiiai.localhost:3000";

async function entrar(page: Page) {
  await page.goto(`${BASE}/painel/login`);
  await page.getByLabel("E-mail").fill("admin@acme.dev");
  await page.getByLabel("Senha").fill("Admin!123");
  await page.getByRole("button", { name: "Entrar", exact: true }).click();
  await expect(page).toHaveURL(/\/painel$/);
}

async function semOverflow(page: Page) {
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
}

for (const tema of ["light", "dark"]) {
  test(`shell e Agenda reais responsivos no tema ${tema}`, async ({ page }, testInfo) => {
    test.setTimeout(90_000);
    const erros: string[] = [];
    page.on("pageerror", erro => erros.push(erro.message));
    await page.addInitScript(tema => localStorage.setItem("tema", tema), tema);
    await entrar(page);
    page.on("console", mensagem => { if (mensagem.type() === "error") erros.push(mensagem.text()); });
    await page.goto(`${BASE}/painel/agenda`);
    for (const largura of [375, 430, 768, 1024, 1440, 1920]) {
      await page.setViewportSize({ width: largura, height: 900 });
      await expect(page.getByRole("heading", { name: "Agenda", exact: true })).toBeVisible();
      await expect(page.getByRole("tab", { name: "Dia (todos)" })).toHaveAttribute("aria-selected", "true");
      await expect(page.getByLabel("Data")).toBeVisible();
      await expect(page.getByRole("button", { name: "Novo agendamento" })).toBeVisible();
      await semOverflow(page);
      if (largura < 1024) await page.getByRole("button", { name: "Abrir menu" }).click();
      const nav = page.getByRole("navigation", { name: "Navegação do painel" });
      await expect(nav.getByRole("link")).toHaveCount(14);
      await expect(nav.getByRole("link", { name: "Agenda", exact: true })).toHaveAttribute("aria-current", "page");
      if (largura < 1024) { await page.keyboard.press("Escape"); await expect(page.getByRole("button", { name: "Abrir menu" })).toBeFocused(); }
      await page.getByRole("tab", { name: "Por profissional" }).click();
      await expect(page.getByLabel("Profissional")).toBeVisible();
      await semOverflow(page);
      if ([430, 1440].includes(largura)) {
        const caminho = testInfo.outputPath(`agenda-${tema}-${largura}.png`);
        await page.screenshot({ path: caminho });
        await testInfo.attach(`Agenda ${tema} ${largura}`, { path: caminho, contentType: "image/png" });
      }
      await page.getByRole("tab", { name: "Semana", exact: true }).click();
      await expect(page.getByRole("list", { name: "Semana", exact: true })).toBeVisible();
      await semOverflow(page);
      await page.getByRole("tab", { name: "Dia (todos)" }).click();
    }
    expect(erros).toEqual([]);
  });
}

test("drawer permite teclado, fecha ao navegar e restaura foco; modal responde a Escape", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await entrar(page);
  const abrirMenu = page.getByRole("button", { name: "Abrir menu" });
  await abrirMenu.click();
  const menu = page.getByRole("dialog", { name: "Menu principal" });
  await expect(menu.getByRole("button", { name: "Fechar menu" })).toBeFocused();
  await page.keyboard.press("Shift+Tab");
  await expect(menu.getByRole("button", { name: "Sair" })).toBeFocused();
  await page.keyboard.press("Tab");
  await expect(menu.getByRole("button", { name: "Fechar menu" })).toBeFocused();
  await menu.getByRole("link", { name: "Agenda", exact: true }).click();
  await expect(page).toHaveURL(/\/painel\/agenda$/);
  await expect(menu).toBeHidden();
  await expect(abrirMenu).toBeFocused();
  const novo = page.getByRole("button", { name: "Novo agendamento" });
  await expect(novo).toBeEnabled();
  await novo.click();
  const modal = page.getByRole("dialog", { name: "Novo agendamento" });
  await expect(modal).toBeVisible();
  expect(await modal.evaluate(elemento => elemento.contains(document.activeElement))).toBe(true);
  await semOverflow(page);
  await page.keyboard.press("Escape");
  await expect(modal).toBeHidden();
  await expect(novo).toBeFocused();
  await page.emulateMedia({ reducedMotion: "reduce" });
  await abrirMenu.click();
  await expect(menu.getByRole("button", { name: "Fechar menu" })).toBeFocused();
  expect(await menu.evaluate(elemento => getComputedStyle(elemento).transitionDuration)).toBe("0s");
  await menu.getByRole("button", { name: "Sair" }).click();
  await expect(page).toHaveURL(/\/painel\/login$/);
});

test("login preserva senha visível, tema e link de recuperação, sem overflow", async ({ page }, testInfo) => {
  for (const tema of ["dark", "light"]) {
    await page.addInitScript(tema => localStorage.setItem("tema", tema), tema);
    await page.goto(`${BASE}/painel/login`);
    for (const largura of [375, 430, 768, 1024, 1440, 1920]) {
      await page.setViewportSize({ width: largura, height: 900 });
      await expect(page.getByRole("heading", { name: "Entrar no painel" })).toBeVisible();
      await expect(page.getByRole("link", { name: "Esqueci minha senha" })).toHaveAttribute("href", "/painel/esqueci-senha");
      await semOverflow(page);
    }
    const senha = page.getByLabel("Senha", { exact: true });
    await senha.fill("SenhaExemplo");
    await page.getByRole("button", { name: "Mostrar senha" }).click();
    await expect(senha).toHaveAttribute("type", "text");
    await page.getByRole("button", { name: "Ocultar senha" }).click();
    await expect(senha).toHaveAttribute("type", "password");
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.screenshot({ path: testInfo.outputPath(`login-${tema}.png`) });
    await page.getByTestId("botao-tema").click();
    await expect(page.locator("html")).toHaveAttribute("data-theme", tema === "dark" ? "light" : "dark");
  }
});
