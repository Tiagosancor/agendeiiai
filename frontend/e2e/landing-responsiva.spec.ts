import { test, expect } from "@playwright/test";

const RAIZ = "http://agendeiiai.localhost:3000";

for (const tema of ["light", "dark"] as const) {
  for (const largura of [375, 390, 430, 768, 1024, 1440, 1920]) {
    test(`landing ${tema} em ${largura}px preserva conteúdo, CTAs e não transborda`, async ({ page }) => {
      await page.setViewportSize({ width: largura, height: largura === 375 ? 812 : largura === 430 ? 932 : 900 });
      await page.emulateMedia({ colorScheme: tema, reducedMotion: "reduce" });
      const erros: string[] = [];
      page.on("pageerror", erro => erros.push(erro.message));
      page.on("console", mensagem => {
        if (mensagem.type() === "error") erros.push(mensagem.text());
      });
      await page.goto(RAIZ);
      await expect(page.locator("html")).toHaveAttribute("data-theme", tema);
      await expect(page.locator(".site-header .site-logo strong")).toHaveText("agendei ai");
      await expect(page.locator(".site-final .site-logo strong")).toHaveText("agendei ai");
      await expect(page.locator(".site-recurso-lista-painel h3")).toHaveText([
        "Agenda por profissional", "Link de agendamento sem app", "Confirmação por código",
      ]);
      const alternador = page.getByTestId("botao-tema");
      await expect(alternador.locator("svg")).toHaveCount(2);
      for (const icone of await alternador.locator("svg").all()) await expect(icone).toBeVisible();
      await alternador.focus();
      await page.keyboard.press("Space");
      await expect(page.locator("html")).toHaveAttribute("data-theme", tema === "light" ? "dark" : "light");
      await expect(alternador).toHaveAttribute("aria-label", tema === "light" ? "Ativar tema claro" : "Ativar tema escuro");
      await page.keyboard.press("Enter");
      await expect(page.locator("html")).toHaveAttribute("data-theme", tema);
      const atalho = page.getByRole("link", { name: "Pular para o conteúdo" });
      await expect(atalho).toHaveCSS("clip-path", "inset(50%)");
      await atalho.focus();
      await expect(atalho).toHaveCSS("clip-path", "none");
      await expect.poll(() => atalho.evaluate(el => el.getBoundingClientRect().top)).toBe(16);
      await atalho.evaluate(el => (el as HTMLElement).blur());
      await expect(atalho).toHaveCSS("clip-path", "inset(50%)");
      await expect(page.getByRole("heading", { level: 1 })).toContainText("Sua agenda cheia");
      await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBeTruthy();
      await expect(page.locator(".site-produto")).toHaveCSS("background-color", tema === "dark" ? "rgb(2, 19, 46)" : "rgb(250, 253, 255)");

      const fontes = await page.getByRole("heading", { level: 1 }).evaluate(el => getComputedStyle(el).fontFamily);
      expect(fontes).not.toMatch(/Roboto[ _]Slab/);
      const hero = page.locator(".site-hero");
      const copy = await page.locator(".site-hero-texto").boundingBox();
      const celulares = await page.locator(".site-hero-celulares").boundingBox();
      expect(copy).not.toBeNull();
      expect(celulares).not.toBeNull();
      if (largura >= 1280) {
        await expect(page.locator(".site-navbar")).toHaveCSS("min-height", "80px");
        expect((await hero.boundingBox())!.height).toBeGreaterThanOrEqual(620);
        expect((await hero.boundingBox())!.height).toBeLessThanOrEqual(720);
        expect((await page.locator(".site-hero-grid").boundingBox())!.width).toBe(1240);
        expect(celulares!.width).toBeGreaterThanOrEqual(480);
        expect(celulares!.width).toBeLessThanOrEqual(560);
        expect(celulares!.x).toBeGreaterThan(copy!.x + copy!.width);
        const linhas = await page.getByRole("heading", { level: 1 }).evaluate(el =>
          Math.round(el.getBoundingClientRect().height / parseFloat(getComputedStyle(el).lineHeight)));
        expect(linhas).toBe(3);
      } else if (largura < 768) {
        expect(celulares!.y).toBeGreaterThanOrEqual(copy!.y + copy!.height);
      }
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
      // A prova do produto ganha escala sem alterar os assets reais nem o Hero aprovado.
      await expect(page.locator(".site-segmentos > li")).toHaveCount(4);
      // Contraste do texto secundário sobre a superfície efetiva dos quatro cards.
      const contrastes = await page.locator(".site-segmentos .site-card p").evaluateAll(elementos => {
        const canvas = document.createElement("canvas");
        canvas.width = canvas.height = 1;
        const contexto = canvas.getContext("2d")!;
        const luminancia = (cor: string) => {
          contexto.clearRect(0, 0, 1, 1);
          contexto.fillStyle = cor;
          contexto.fillRect(0, 0, 1, 1);
          const canais = [...contexto.getImageData(0, 0, 1, 1).data].slice(0, 3).map(canal => {
            const valor = canal / 255;
            return valor <= 0.04045 ? valor / 12.92 : ((valor + 0.055) / 1.055) ** 2.4;
          });
          return canais[0] * 0.2126 + canais[1] * 0.7152 + canais[2] * 0.0722;
        };
        return elementos.map(el => {
          const texto = luminancia(getComputedStyle(el).color);
          const fundo = luminancia(getComputedStyle(el.closest(".site-card")!).backgroundColor);
          return (Math.max(texto, fundo) + 0.05) / (Math.min(texto, fundo) + 0.05);
        });
      });
      for (const contraste of contrastes) expect(contraste).toBeGreaterThanOrEqual(4.5);
      await expect(page.locator(".site-passos > li")).toHaveCount(3);
      await expect(page.locator(".site-numero")).toHaveText(["01", "02", "03"]);
      await expect(page.locator(".site-beneficios-grid > li")).toHaveCount(6);
      await expect(page.getByRole("figure", { name: "Demonstração visual da Agenda" })).toBeVisible();
      await expect(page.locator(".agenda-demo-agendamentos > li")).toHaveCount(2);
      await expect(page.locator(".agenda-demo-modulo")).toHaveText([
        "Agenda", "Usuários", "Profissionais", "Serviços", "Cupons", "Clientes", "Financeiro", "Fidelidade", "Meu negócio",
      ]);
      await expect(page.locator(".agenda-demo-agendamentos")).toContainText("Pedro Costa");
      await expect(page.locator(".agenda-demo-agendamentos")).toContainText("André Rocha");
      await expect(page.locator(".agenda-demo-agendamentos")).toContainText("R$ 45,00");
      await expect(page.locator(".agenda-demo-agendamentos")).toContainText("R$ 35,00");
      await expect(page.locator(".site-agenda-demo a")).toHaveCount(0);
      await expect(page.locator(".agenda-demo-filtros select")).toHaveValue("Diego Lima");
      await expect(page.locator(".agenda-demo-sidebar")).toHaveCSS("color", tema === "dark" ? "rgb(195, 212, 233)" : "rgb(52, 75, 105)");
      await expect(page.locator(".site-agenda-demo")).toHaveCSS("background-color", "rgb(247, 249, 252)");
      const preview = (await page.locator(".site-agenda-demo").boundingBox())!;
      const sidebar = (await page.locator(".agenda-demo-sidebar").boundingBox())!;
      if (largura >= 768) expect(Math.abs(sidebar.height - preview.height)).toBeLessThan(1);
      if (largura >= 1024) {
        expect(sidebar.width / preview.width).toBeGreaterThanOrEqual(.18);
        expect(sidebar.width / preview.width).toBeLessThanOrEqual(.24);
        await expect(page.locator(".agenda-demo-marca strong")).toBeVisible();
      } else if (largura < 768) {
        expect(Math.abs(sidebar.width - preview.width)).toBeLessThan(1);
        expect(sidebar.height).toBeLessThan(110);
        await expect(page.locator(".agenda-demo-marca strong")).toBeVisible();
        for (const controle of await page.locator(".site-navbar button, .site-navbar a, .site-demo-controles button, .agenda-demo-acoes button, .agenda-demo-utilitarios button").all()) {
          if (await controle.isVisible()) expect((await controle.boundingBox())!.height).toBeGreaterThanOrEqual(44);
        }
        await expect(page.locator(".agenda-demo-campo").first()).toHaveCSS("font-size", "16px");
      }
      const painel = await page.locator(".site-painel-frame").boundingBox();
      const demonstracao = await page.locator(".site-demonstracao").first().boundingBox();
      if (largura >= 1024) {
        expect(painel!.width / demonstracao!.width).toBeGreaterThanOrEqual(.85);
        expect(painel!.width / demonstracao!.width).toBeLessThanOrEqual(.92);
        expect(preview.height).toBeGreaterThanOrEqual(530);
        const telefone = await page.locator(".site-telas-mobile .site-celular").first().boundingBox();
        expect(telefone!.width).toBeGreaterThan(200);
      } else if (largura < 768) {
        const cards = await page.locator(".site-segmentos > li").all();
        expect((await cards[1].boundingBox())!.y).toBeGreaterThan((await cards[0].boundingBox())!.y);
        const planos = await page.getByTestId("card-plano").all();
        expect((await planos[1].boundingBox())!.y).toBeGreaterThan((await planos[0].boundingBox())!.y);
      }
      // O accordion nativo continua acessível pelo teclado em ambos os temas.
      const resumo = page.locator(".site-faq summary").first();
      await resumo.focus();
      await page.keyboard.press("Enter");
      await expect(page.locator(".site-faq details").first()).toHaveAttribute("open", "");
      await page.keyboard.press("Enter");
      await expect(page.locator(".site-faq details").first()).not.toHaveAttribute("open");
      await page.locator('#planos [role="radio"]').filter({ hasText: "Anual" }).click();
      for (const link of await page.locator('.site-plano-cta a').all()) {
        await expect(link).toHaveAttribute("href", /periodicidade=Anual/);
      }
      await page.locator('#planos [role="radio"]').filter({ hasText: "Mensal" }).click();
      for (const imagem of await page.locator(".site-produto img").all()) {
        if (!(await imagem.isVisible())) continue;
        await imagem.scrollIntoViewIfNeeded();
        await expect.poll(() => imagem.evaluate(el => (el as HTMLImageElement).naturalWidth)).toBeGreaterThan(0);
      }
      // A captura completa também precisa funcionar depois de navegar/rolar até o meio da página.
      await page.locator("#planos").scrollIntoViewIfNeeded();
      await page.screenshot({ path: `test-results/navbar-apos-scroll-${tema}-${largura}.png`, fullPage: true });
      await expect.poll(() => page.locator(".site-header").evaluate(el =>
        Math.round(el.getBoundingClientRect().top + window.scrollY))).toBe(0);
      await expect(page.locator(".site-header")).toHaveCSS("position", "static");
      await expect(page.locator(".site-header")).not.toBeInViewport();
      await page.evaluate(() => window.scrollTo(0, 0));
      await expect(page.locator(".site-header")).toBeInViewport();
      await expect.poll(() => page.locator(".site-header").evaluate(el => el.getBoundingClientRect().top)).toBe(0);
      await page.mouse.move(0, 0);
      await page.screenshot({ path: `test-results/landing-hero-${tema}-${largura}.png` });
      await page.screenshot({ path: `test-results/landing-${tema}-${largura}.png`, fullPage: true });
      for (const classe of ["site-demonstracao", "site-demonstracao-mobile", "site-planos-grid", "site-final"]) {
        await page.locator(`.${classe}`).first().screenshot({ path: `test-results/fase2-${classe}-${tema}-${largura}.png` });
      }
      if ([375, 430, 1440].includes(largura)) {
        for (const classe of ["site-segmentos", "site-passos", "site-beneficios-grid", "site-faq"]) {
          await page.locator(`.${classe}`).screenshot({ path: `test-results/fase2-${classe}-${tema}-${largura}.png` });
        }
      }
      await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBeTruthy();
      expect(erros).toEqual([]);
    });
  }
}


test("switch de tema preserva o indicador com movimento reduzido", async ({ page }) => {
  await page.emulateMedia({ colorScheme: "light", reducedMotion: "reduce" });
  await page.goto(RAIZ);
  const alternador = page.getByTestId("botao-tema");
  await expect.poll(() => alternador.evaluate(el => getComputedStyle(el, "::before").left)).toBe("5px");
  await alternador.click();
  await expect(page.locator("html")).toHaveAttribute("data-theme", "dark");
  await expect.poll(() => alternador.evaluate(el => getComputedStyle(el, "::before").left)).toBe("25px");
});
