import { test, expect } from "@playwright/test";

const BASE = "http://app.agendeiiai.localhost:3000";

for (const tema of ["light", "dark"]) {
  test(`Meu negócio preserva dados, controles e salvamento no tema ${tema}`, async ({ page }, testInfo) => {
    test.setTimeout(120_000);
    const erros: string[] = [];
    page.on("pageerror", erro => erros.push(erro.message));
    await page.addInitScript(tema => localStorage.setItem("tema", tema), tema);
    await page.goto(`${BASE}/painel/login`);
    await page.getByLabel("E-mail").fill("admin@acme.dev");
    await page.getByLabel("Senha").fill("Admin!123");
    await page.getByRole("button", { name: "Entrar", exact: true }).click();
    await expect(page).toHaveURL(/\/painel$/);
    const carregado = page.waitForResponse(r => r.url().endsWith("/painel/negocio") && r.request().method() === "GET" && r.headers()["content-type"]?.includes("application/json") && r.ok());
    await page.goto(`${BASE}/painel/negocio`);
    const perfil = await (await carregado).json();
    await expect(page.getByRole("heading", { name: "Meu negócio", exact: true })).toBeVisible();
    for (const [label, chave] of [["Nome exibido", "nomeExibido"], ["URL do logo", "logoUrl"], ["Bairro", "bairro"], ["Cidade", "cidade"], ["Telefone", "telefone"], ["E-mail de contato (Fale Conosco)", "emailContato"], ["WhatsApp", "whatsApp"]]) {
      await expect(page.getByLabel(label, { exact: true })).toHaveValue(perfil[chave] ?? "");
    }
    await expect(page.getByTestId("link-atual")).toBeVisible();
    await expect(page.getByLabel("Imagem de fundo", { exact: true })).toHaveAttribute("accept", "image/jpeg,image/png,image/webp");
    for (const width of [375, 390, 430, 768, 1024, 1440]) {
      await page.setViewportSize({ width, height: 932 });
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `${width}px`).toBe(true);
      for (const label of ["Nome exibido", "Cor primária", "Cor secundária", "Número", "CEP", "WhatsApp", "Cor de fundo"]) {
        const campo = page.getByLabel(label, { exact: true });
        await expect(campo).toBeVisible();
        expect((await campo.boundingBox())!.height).toBeGreaterThanOrEqual(44);
      }
      expect((await page.getByRole("button", { name: "Salvar alterações", exact: true }).boundingBox())!.height).toBeGreaterThanOrEqual(44);
      for (const campo of await page.locator('input[type="time"]').all()) {
        await expect(campo).toHaveAttribute("aria-label", /^(Abertura|Fechamento) — /);
        const caixa = (await campo.boundingBox())!;
        expect(caixa.x).toBeGreaterThanOrEqual(0);
        expect(caixa.x + caixa.width).toBeLessThanOrEqual(width);
      }
      await page.screenshot({ path: testInfo.outputPath(`negocio-${tema}-${width}.png`), fullPage: true });
    }
    // A prévia usa contraste do fundo escolhido, independente do tema do painel.
    const cor = page.getByLabel("Cor de fundo", { exact: true });
    await cor.fill("#ffffff");
    expect(await page.getByTestId("previa-fundo").locator("span").evaluate(el => getComputedStyle(el).color)).toBe(perfil.imagemFundoUrl ? "rgb(255, 255, 255)" : "rgb(17, 24, 39)");
    await cor.fill(perfil.corFundo ?? "");

    // Salva os mesmos dados reais do ambiente local, sem trocar link ou enviar imagem.
    const gravado = page.waitForResponse(r => r.url().endsWith("/painel/negocio") && r.request().method() === "PUT");
    await page.getByRole("button", { name: "Salvar alterações", exact: true }).click();
    const resposta = await gravado;
    expect(resposta.status()).toBe(204);
    const corpo = resposta.request().postDataJSON();
    for (const chave of ["nomeExibido", "whatsAppAtivoParaConfirmacoes", "whatsAppAvisoProfissional"]) expect(corpo[chave]).toEqual(perfil[chave]);
    expect(corpo.horarioFuncionamento).toEqual(perfil.horarioFuncionamento);
    await expect(page.getByRole("status")).toHaveText("Perfil atualizado.");
    await page.reload();
    await expect(page.getByLabel("Nome exibido", { exact: true })).toHaveValue(perfil.nomeExibido);
    expect(erros).toEqual([]);
  });
}
