import { test, expect } from "@playwright/test";

const RAIZ = "http://agendeiiai.localhost:3000";

test("microdemo percorre somente capturas reais, espera a entrada e pausa fora da tela", async ({ page }) => {
  await page.clock.install();
  await page.emulateMedia({ reducedMotion: "no-preference" });
  await page.goto(RAIZ);
  const demo = page.locator(".site-microdemo");
  await expect(demo).toHaveAttribute("data-autoplay", "ativo");
  await page.clock.runFor(2000);
  await expect(demo).toHaveAttribute("data-etapa", "0");
  await page.clock.runFor(400);
  await expect(demo).toHaveAttribute("data-etapa", "1");
  await page.clock.runFor(3400);
  await expect(demo).toHaveAttribute("data-etapa", "2");
  await expect(demo.locator('.site-demo-resumo')).toHaveAttribute("src", "/site/assistente-resumo.webp");
  await expect(demo.locator('.site-demo-resumo')).toHaveAttribute("aria-hidden", "false");
  await page.clock.runFor(4200);
  await expect(demo).toHaveAttribute("data-etapa", "0");
  await page.evaluate(() => document.querySelector("#planos")!.scrollIntoView());
  await expect(demo).toHaveAttribute("data-autoplay", "pausado");
  await page.clock.runFor(15000);
  await expect(demo).toHaveAttribute("data-etapa", "0");
  await page.evaluate(() => window.scrollTo(0, 0));
  await expect(demo).toHaveAttribute("data-autoplay", "ativo");
  await page.emulateMedia({ reducedMotion: "reduce" });
  await expect(demo).toHaveAttribute("data-autoplay", "pausado");
  await page.clock.runFor(15000);
  await expect(demo).toHaveAttribute("data-etapa", "0");
});

test("controles por teclado funcionam com movimento reduzido e sem anúncios automáticos", async ({ page }) => {
  await page.emulateMedia({ reducedMotion: "reduce" });
  await page.goto(RAIZ);
  const demo = page.locator(".site-microdemo");
  const resumo = page.getByRole("button", { name: "Ver etapa: revisar e confirmar" });
  await resumo.focus();
  await page.keyboard.press("Enter");
  await expect(resumo).toHaveAttribute("aria-pressed", "true");
  await expect(demo).toHaveAttribute("data-etapa", "2");
  await expect(demo.locator(".site-demo-resumo")).toHaveCSS("opacity", "1");
  await page.getByRole("button", { name: "Ver etapa: escolher serviços" }).focus();
  await page.keyboard.press("Space");
  await expect(demo).toHaveAttribute("data-etapa", "0");
  await expect(demo.locator("[aria-live]")).toHaveCount(0);
});

test("seleção manual touch tem prioridade; todas as etapas ficam completas sem overflow", async ({ browser }) => {
  const contexto = await browser.newContext({ viewport: { width: 430, height: 932 }, isMobile: true, hasTouch: true });
  const page = await contexto.newPage();
  await page.clock.install();
  await page.goto(RAIZ);
  const demo = page.locator(".site-microdemo");
  await page.getByRole("button", { name: "Ver etapa: revisar e confirmar" }).tap();
  await expect(demo).toHaveAttribute("data-autoplay", "pausado");
  await page.clock.runFor(20000);
  await expect(demo).toHaveAttribute("data-etapa", "2");
  // As capturas de todas as etapas estão decodificadas antes de reutilizar a moldura.
  for (const img of await demo.locator("img").all()) {
    await expect.poll(() => img.evaluate(el => (el as HTMLImageElement).naturalWidth)).toBeGreaterThan(0);
  }
  await page.emulateMedia({ reducedMotion: "reduce" });
  for (const largura of [375, 430, 768, 1024, 1440, 1920]) {
    await page.setViewportSize({ width: largura, height: 932 });
    for (const [indice, nome] of ["escolher serviços", "escolher profissional, data e horário", "revisar e confirmar"].entries()) {
      const controle = page.getByRole("button", { name: `Ver etapa: ${nome}` });
      await controle.click();
      await expect(demo).toHaveAttribute("data-etapa", String(indice));
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBeTruthy();
      const area = (await controle.boundingBox())!;
      expect(area.height).toBeGreaterThanOrEqual(44);
      expect(area.x).toBeGreaterThanOrEqual(0);
      expect(area.x + area.width).toBeLessThanOrEqual(largura);
    }
  }
  await contexto.close();
});
