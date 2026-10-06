import { test, expect } from "@playwright/test";

const RAIZ = "http://agendeiiai.localhost:3000";

test("Hero limita parallax, retorna ao repouso e pausa fora da tela", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.emulateMedia({ reducedMotion: "no-preference" });
  await page.goto(RAIZ);
  const hero = page.locator(".site-hero");
  await expect(hero).toHaveAttribute("data-parallax", "ativo");
  const area = (await hero.boundingBox())!;
  await page.mouse.move(area.x + area.width - 2, area.y + area.height / 2);
  const x = () => hero.evaluate(el => parseFloat((el as HTMLElement).style.getPropertyValue("--hero-cursor-x")) || 0);
  await expect.poll(x).toBeGreaterThan(7);
  expect(await x()).toBeLessThanOrEqual(8);
  await expect(page.locator(".site-celular-frente")).toHaveCSS("animation-duration", "8s");
  await expect(page.locator(".site-celular-atras")).toHaveCSS("animation-duration", "9.5s");
  await page.mouse.move(0, 0);
  await expect.poll(async () => Math.abs(await x())).toBeLessThan(.1);
  await page.locator("#planos").scrollIntoViewIfNeeded();
  await expect(hero).toHaveAttribute("data-visivel", "false");
  await expect(page.locator(".site-celular-frente")).toHaveCSS("animation-play-state", "paused");
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBeTruthy();
});

test("Hero fica completo e estático com movimento reduzido, inclusive após mudança de preferência", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.emulateMedia({ reducedMotion: "reduce" });
  await page.goto(RAIZ);
  const hero = page.locator(".site-hero");
  await expect(hero).toHaveAttribute("data-parallax", "inativo");
  for (const classe of ["site-badge", "site-hero-celulares", "site-celular-frente", "site-celular-atras"]) {
    await expect(hero.locator(`.${classe}`)).toHaveCSS("animation-name", "none");
  }
  await expect(page.getByRole("heading", { level: 1 })).toHaveCSS("opacity", "1");
  await expect.poll(() => hero.locator(".site-hero-celulares").evaluate(el => getComputedStyle(el, "::before").animationName)).toBe("none");
  await page.emulateMedia({ reducedMotion: "no-preference" });
  await expect(hero).toHaveAttribute("data-parallax", "ativo");
  await page.emulateMedia({ reducedMotion: "reduce" });
  await expect(hero).toHaveAttribute("data-parallax", "inativo");
  await page.mouse.move(1300, 300);
  expect(await hero.evaluate(el => (el as HTMLElement).style.getPropertyValue("--hero-cursor-x"))).toBe("");
});

test("Hero não executa cursor parallax nem floating no mobile touch", async ({ browser }) => {
  const contexto = await browser.newContext({ viewport: { width: 375, height: 812 }, isMobile: true, hasTouch: true });
  const page = await contexto.newPage();
  await page.goto(RAIZ);
  await expect(page.locator(".site-hero")).toHaveAttribute("data-parallax", "inativo");
  await expect(page.locator(".site-celular-frente")).toHaveCSS("animation-name", "none");
  await expect(page.locator(".site-celular-atras")).toHaveCSS("animation-name", "none");
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBeTruthy();
  await contexto.close();
});


test("movimento ativo não causa overflow nas seis larguras", async ({ page }) => {
  await page.emulateMedia({ reducedMotion: "no-preference" });
  await page.goto(RAIZ);
  for (const largura of [375, 430, 768, 1024, 1440, 1920]) {
    await page.setViewportSize({ width: largura, height: 900 });
    const hero = page.locator(".site-hero");
    await expect(hero).toHaveAttribute("data-parallax", largura >= 901 ? "ativo" : "inativo");
    const area = (await hero.boundingBox())!;
    await page.mouse.move(area.width - 2, area.y + area.height / 2);
    const semOverflow = await page.evaluate(async () => {
      for (let quadro = 0; quadro < 30; quadro++) {
        await new Promise<void>(resolve => requestAnimationFrame(() => resolve()));
        if (document.documentElement.scrollWidth > innerWidth) return false;
      }
      return true;
    });
    expect(semOverflow, `overflow com movimento em ${largura}px`).toBeTruthy();
  }
});
