import { test, expect } from "@playwright/test";

const BASE = "http://app.agendeiiai.localhost:3000";
const reais = (valor: number) => new Intl.NumberFormat("pt-BR", { style: "currency", currency: "BRL" }).format(valor);

for (const tema of ["light", "dark"]) {
  test(`módulos operacionais preservam valores, filtros e controles no tema ${tema}`, async ({ page }, testInfo) => {
    test.setTimeout(180_000);
    const erros: string[] = [];
    page.on("pageerror", erro => erros.push(erro.message));
    await page.addInitScript(tema => localStorage.setItem("tema", tema), tema);
    await page.goto(`${BASE}/painel/login`);
    await page.getByLabel("E-mail").fill("admin@acme.dev");
    await page.getByLabel("Senha").fill("Admin!123");
    await page.getByRole("button", { name: "Entrar", exact: true }).click();
    await expect(page).toHaveURL(/\/painel$/);

    for (const [rota, titulo] of [["financeiro", "Financeiro"], ["comissoes", "Comissões"], ["vendas", "Vendas"], ["estoque", "Estoque"]]) {
      const resposta = rota === "financeiro" ? page.waitForResponse(r => r.url().includes("/painel/financeiro/resumo?") && r.ok()) : null;
      await page.goto(`${BASE}/painel/${rota}`);
      await expect(page.getByRole("heading", { name: titulo, exact: true })).toBeVisible();
      if (resposta) {
        const resumo = await (await resposta).json();
        await expect(page.getByLabel("Faturamento total", { exact: true })).toHaveText(reais(resumo.total));
        await expect(page.getByLabel("Faturamento de serviços", { exact: true })).toHaveText(reais(resumo.totalServicos));
        await expect(page.getByLabel("Faturamento de produtos", { exact: true })).toHaveText(reais(resumo.totalProdutos));
      }
      if (rota === "estoque") await expect(page.getByRole("table", { name: "Produtos em estoque" }).locator("tbody tr").first()).toBeVisible();
      if (rota === "comissoes") await expect(page.getByRole("tab", { name: "Equipe", exact: true })).toBeVisible();
      for (const largura of [375, 390, 430, 768, 1024, 1440]) {
        await page.setViewportSize({ width: largura, height: 932 });
        expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `${rota} ${largura}px`).toBe(true);
        if (rota === "estoque" && largura < 768) {
          const registro = page.locator(".painel-operacional-cards tbody tr").first();
          expect(await registro.evaluate(el => getComputedStyle(el).display)).toBe("grid");
          for (const botao of await registro.getByRole("button").all()) expect((await botao.boundingBox())!.height).toBeGreaterThanOrEqual(44);
        }
        if ([390, 1440].includes(largura)) await page.screenshot({ path: testInfo.outputPath(`${rota}-${tema}-${largura}.png`) });
      }
      await page.setViewportSize({ width: 390, height: 932 });
      if (rota === "financeiro") {
        const fim = page.waitForResponse(r => r.url().includes("fim=2099-01-31") && r.ok());
        await page.getByLabel("Até", { exact: true }).fill("2099-01-31");
        await fim;
        const filtrado = page.waitForResponse(r => r.url().includes("/painel/financeiro/resumo?") && r.url().includes("inicio=2099-01-01") && r.ok());
        await page.getByLabel("De", { exact: true }).fill("2099-01-01");
        const vazio = await (await filtrado).json();
        await expect(page.getByLabel("Faturamento total", { exact: true })).toHaveText(reais(vazio.total));
        await expect(page.getByRole("combobox", { name: "Profissional", exact: true })).toBeVisible();
        await expect(page.getByRole("combobox", { name: "Serviço", exact: true })).toBeVisible();
      }
      if (rota === "comissoes") {
        await page.getByRole("button", { name: "Mês anterior", exact: true }).click();
        await expect(page.getByRole("button", { name: "Mês anterior", exact: true })).toHaveAttribute("aria-pressed", "true");
        for (const aba of ["Equipe", "Quinzenas", "Vales e consumo"]) {
          await page.getByRole("tab", { name: aba, exact: true }).click();
          await expect(page.getByRole("tab", { name: aba, exact: true })).toHaveAttribute("aria-selected", "true");
          expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
        }
        await page.getByRole("button", { name: "Lançar vale", exact: true }).click();
      }
      if (rota === "vendas") await page.getByRole("button", { name: "Nova venda", exact: true }).click();
      if (rota === "estoque") await page.getByRole("button", { name: "Novo produto", exact: true }).click();
      if (rota !== "financeiro") {
        const dialogo = page.getByRole("dialog");
        await expect(dialogo).toBeVisible();
        expect(await dialogo.evaluate(el => el.contains(document.activeElement))).toBe(true);
        expect(await dialogo.evaluate(el => el.scrollWidth <= el.clientWidth)).toBe(true);
        await page.keyboard.press("Escape");
        await expect(dialogo).toBeHidden();
      }
    }
    expect(erros).toEqual([]);
  });
}
