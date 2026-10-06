import { test, expect } from "@playwright/test";

const BASE = "http://app.agendeiiai.localhost:3000";

for (const tema of ["light", "dark"]) {
  test(`Cupons e Fidelidade preservam ações e apresentação no tema ${tema}`, async ({ page }, testInfo) => {
    test.setTimeout(120_000);
    const erros: string[] = [];
    page.on("pageerror", erro => erros.push(erro.message));
    await page.addInitScript(tema => localStorage.setItem("tema", tema), tema);
    await page.goto(`${BASE}/painel/login`);
    await page.getByLabel("E-mail").fill("admin@acme.dev");
    await page.getByLabel("Senha").fill("Admin!123");
    await page.getByRole("button", { name: "Entrar", exact: true }).click();
    await expect(page).toHaveURL(/\/painel$/);

    await page.goto(`${BASE}/painel/cupons`);
    await page.getByRole("button", { name: "Novo cupom", exact: true }).click();
    const modal = page.getByRole("dialog", { name: "Novo cupom", exact: true });
    expect(await modal.evaluate(el => el.contains(document.activeElement))).toBe(true);
    for (const width of [375, 390, 430, 768, 1024, 1440]) {
      await page.setViewportSize({ width, height: 932 });
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      expect(await modal.evaluate(el => el.scrollWidth <= el.clientWidth)).toBe(true);
      await expect(modal.getByLabel("Limite de usos (opcional)")).toBeVisible();
    }
    await page.keyboard.press("Escape");
    await expect(modal).toBeHidden();
    await expect(page.getByRole("button", { name: "Novo cupom", exact: true })).toBeFocused();
    await page.setViewportSize({ width: 390, height: 932 });
    await page.getByRole("button", { name: "Novo cupom", exact: true }).click();
    const codigo = `VISUAL${tema.toUpperCase()}${Date.now()}`;
    await modal.getByLabel("Código", { exact: true }).fill(codigo);
    await modal.getByRole("combobox", { name: "Tipo", exact: true }).selectOption(tema === "light" ? "Percentual" : "ValorFixo");
    await modal.getByLabel("Valor", { exact: true }).fill("10");
    await modal.getByLabel("Limite de usos (opcional)").fill("5");
    await modal.getByRole("button", { name: "Criar", exact: true }).click();
    await expect(modal).toBeHidden();
    const linha = page.getByRole("row").filter({ hasText: codigo });
    await expect(linha).toContainText(tema === "light" ? "10%" : /R\$\s*10,00/);
    await expect(linha).toContainText("Sem validade");
    await expect(linha).toContainText("0 / 5");
    await linha.getByRole("button", { name: "Desativar", exact: true }).click();
    await expect(linha).toContainText("Inativo");
    await linha.getByRole("button", { name: "Ativar", exact: true }).click();
    await expect(linha).toContainText("Ativo");

    for (const rota of ["cupons", "fidelidade"]) {
      await page.goto(`${BASE}/painel/${rota}`);
      await expect(page.getByRole("heading", { name: rota === "cupons" ? "Cupons" : "Fidelidade", exact: true })).toBeVisible();
      await expect(page.getByText("Carregando...", { exact: true })).toBeHidden();
      for (const width of [375, 390, 430, 768, 1024, 1440]) {
        await page.setViewportSize({ width, height: 932 });
        expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `${rota} ${width}px`).toBe(true);
        if (rota === "cupons" && width < 768) {
          const registro = page.getByRole("row").filter({ hasText: codigo });
          expect((await registro.getByRole("button").boundingBox())!.height).toBeGreaterThanOrEqual(44);
        }
        if ([390, 1440].includes(width)) await page.screenshot({ path: testInfo.outputPath(`${rota}-${tema}-${width}.png`) });
      }
      if (rota === "fidelidade") {
        const selos = page.getByLabel("Selos necessários para resgatar", { exact: true });
        const recompensa = page.getByLabel("Recompensa", { exact: true });
        const quantidade = await selos.inputValue();
        const descricao = await recompensa.inputValue() || "Um corte grátis";
        await recompensa.fill(descricao);
        await page.getByRole("button", { name: "Salvar", exact: true }).click();
        await expect(page.getByRole("status")).toHaveText("Programa de fidelidade salvo.");
        await page.reload();
        await expect(selos).toHaveValue(quantidade);
        await expect(recompensa).toHaveValue(descricao);
      }
    }
    expect(erros).toEqual([]);
  });
}
