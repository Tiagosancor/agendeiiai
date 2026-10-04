import { test, expect } from "@playwright/test";

const RAIZ = "http://agendeiiai.localhost:3000";

for (const tema of ["light", "dark"] as const) {
  for (const largura of [375, 430, 768, 1024, 1440, 1920]) {
    test(`landing ${tema} em ${largura}px preserva conteúdo, CTAs e não transborda`, async ({ page }) => {
      await page.setViewportSize({ width: largura, height: 900 });
      await page.emulateMedia({ colorScheme: tema });
      const erros: string[] = [];
      page.on("pageerror", erro => erros.push(erro.message));
      page.on("console", mensagem => {
        if (mensagem.type() === "error") erros.push(mensagem.text());
      });
      await page.goto(RAIZ);
      await expect(page.locator("html")).toHaveAttribute("data-theme", tema);
      await expect(page.getByRole("heading", { level: 1 })).toContainText("Sua agenda cheia");
      await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBeTruthy();
      await expect(page.locator(".site-produto")).toHaveCSS("background-color", tema === "dark" ? "rgb(2, 19, 46)" : "rgb(250, 253, 255)");

      const fontes = await page.getByRole("heading", { level: 1 }).evaluate(el => getComputedStyle(el).fontFamily);
      expect(fontes).not.toContain("Roboto_Slab");
      await expect(page.getByRole("link", { name: "Já tenho uma conta" })).toHaveAttribute("href", "/painel/login");
      const ctas = page.locator('a[href="/cadastro"]');
      expect(await ctas.count()).toBeGreaterThanOrEqual(3);
      for (const cta of await ctas.all()) await expect(cta).toBeVisible();

      for (const id of ["como-funciona", "recursos", "planos", "duvidas"]) {
        await page.getByRole("contentinfo").locator(`a[href="#${id}"]`).click();
        await expect(page).toHaveURL(new RegExp(`#${id}$`));
        const topo = await page.locator(`section#${id}`).evaluate(el => el.getBoundingClientRect().top);
        expect(topo).toBeGreaterThanOrEqual(await page.locator("header").evaluate(el => el.getBoundingClientRect().height));
      }
      const pergunta = page.getByText("Meu cliente precisa baixar algum aplicativo?");
      await pergunta.click();
      await expect(page.getByText(/abre o link do seu negócio no navegador/)).toBeVisible();
      await pergunta.click();
      await expect(page.getByText(/abre o link do seu negócio no navegador/)).toBeHidden();
      for (const imagem of await page.locator(".site-produto img").all()) {
        if (!(await imagem.isVisible())) continue;
        await imagem.scrollIntoViewIfNeeded();
        await expect.poll(() => imagem.evaluate(el => (el as HTMLImageElement).naturalWidth)).toBeGreaterThan(0);
      }
      await page.evaluate(() => window.scrollTo(0, 0));
      await page.screenshot({ path: `test-results/landing-hero-${tema}-${largura}.png` });
      await page.screenshot({ path: `test-results/landing-${tema}-${largura}.png`, fullPage: true });
      expect(erros).toEqual([]);
    });
  }
}
