import { test, expect } from "@playwright/test";

const BASE = "http://app.agendeiiai.localhost:3000";

for (const tema of ["light", "dark"]) {
  test(`cadastros preservam dados, ações e modais responsivos no tema ${tema}`, async ({ page }, testInfo) => {
    test.setTimeout(120_000);
    const erros: string[] = [];
    page.on("pageerror", erro => erros.push(erro.message));
    await page.addInitScript(tema => localStorage.setItem("tema", tema), tema);
    await page.goto(`${BASE}/painel/login`);
    await page.getByLabel("E-mail").fill("admin@acme.dev");
    await page.getByLabel("Senha").fill("Admin!123");
    await page.getByRole("button", { name: "Entrar", exact: true }).click();
    await expect(page).toHaveURL(/\/painel$/);

    for (const [rota, titulo, novo, quantidade] of [
      ["usuarios", "Usuários", "Novo usuário", 5],
      ["profissionais", "Profissionais", "Novo profissional", 3],
      ["servicos", "Serviços", "Novo serviço", 8],
      ["clientes", "Clientes", "Novo cliente", 4],
    ] as const) {
      await page.goto(`${BASE}/painel/${rota}`);
      const tabela = page.locator(".painel-cadastro-tabela");
      const registro = tabela.locator("tbody tr").filter({ has: page.getByRole("button", { name: "Editar", exact: true }) }).first();
      await expect(registro.locator("td")).toHaveCount(quantidade);
      const conteudo = await registro.locator("td").allTextContents();
      for (const largura of [375, 390, 430, 768, 1024, 1440]) {
        await page.setViewportSize({ width: largura, height: 932 });
        await expect(page.locator("h1")).toHaveText(titulo);
        await expect(registro.locator("td")).toHaveText(conteudo);
        await expect(registro.getByRole("button", { name: "Editar", exact: true })).toBeVisible();
        const dimensoes = await page.evaluate(() => ({ pagina: document.documentElement.scrollWidth, viewport: innerWidth,
          modulo: document.querySelector(".painel-cadastro")!.getBoundingClientRect().width,
          secao: document.querySelector(".painel-cadastro > section:last-of-type")?.getBoundingClientRect().width }));
        expect(dimensoes.pagina, `${rota} ${largura}px: ${JSON.stringify(dimensoes)}`).toBeLessThanOrEqual(dimensoes.viewport);
        expect(await registro.evaluate(el => getComputedStyle(el).display)).toBe(largura < 768 ? "grid" : "table-row");
        if (largura < 768) {
          for (const controle of await registro.locator("button, a").all()) {
            expect((await controle.boundingBox())!.height).toBeGreaterThanOrEqual(44);
          }
        }
        if ([390, 1440].includes(largura)) await page.screenshot({ path: testInfo.outputPath(`${rota}-${tema}-${largura}.png`) });
      }
      await page.setViewportSize({ width: 390, height: 932 });
      const criar = page.getByRole("button", { name: novo, exact: true });
      await criar.click();
      const dialogo = page.getByRole("dialog", { name: novo, exact: true });
      await expect(dialogo.getByLabel("Nome", { exact: true })).toBeVisible();
      expect(await dialogo.evaluate(el => el.contains(document.activeElement))).toBe(true);
      expect(await dialogo.evaluate(el => el.scrollWidth <= el.clientWidth)).toBe(true);
      await page.keyboard.press("Escape");
      await expect(dialogo).toBeHidden();
      await expect(criar).toBeFocused();
      await registro.getByRole("button", { name: "Editar", exact: true }).click();
      const editar = page.getByRole("dialog");
      await expect(editar.getByLabel("Nome", { exact: true })).toBeVisible();
      expect(await editar.evaluate(el => el.scrollWidth <= el.clientWidth)).toBe(true);
      await page.keyboard.press("Escape");
      if (rota === "usuarios") {
        await registro.getByRole("button", { name: "Permissões", exact: true }).click();
        await expect(page.getByRole("dialog").getByRole("checkbox").first()).toBeVisible();
        await page.keyboard.press("Escape");
      }
      if (rota === "profissionais") {
        await registro.getByRole("link", { name: "Horários e serviços" }).click();
        await expect(page.getByRole("heading", { name: "Horário de trabalho" })).toBeVisible();
        await expect(page.getByTestId("dia-horario-1")).toBeVisible();
        expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
        await page.screenshot({ path: testInfo.outputPath(`ficha-${tema}-390.png`) });
      }
    }
    expect(erros).toEqual([]);
  });
}

test("cliente pode ser criado e editado pelos formulários reais no mobile", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 932 });
  await page.goto(`${BASE}/painel/login`);
  await page.getByLabel("E-mail").fill("admin@acme.dev");
  await page.getByLabel("Senha").fill("Admin!123");
  await page.getByRole("button", { name: "Entrar", exact: true }).click();
  await expect(page).toHaveURL(/\/painel$/);
  await page.goto(`${BASE}/painel/clientes`);
  await page.getByRole("button", { name: "Novo cliente" }).click();
  const sufixo = Date.now().toString();
  const nome = `Cliente Visual ${sufixo}`;
  const modal = page.getByRole("dialog");
  await modal.getByLabel("Nome", { exact: true }).fill(nome);
  await modal.getByLabel("Telefone (E.164)").fill(`719${sufixo.slice(-8)}`);
  await modal.getByLabel("Observações (opcional)").fill("Cadastro de teste visual local");
  await modal.getByRole("button", { name: "Salvar", exact: true }).click();
  await expect(modal).toBeHidden();
  const registro = page.getByRole("row", { name: new RegExp(nome) });
  await registro.getByRole("button", { name: "Editar", exact: true }).click();
  await expect(modal.getByLabel("Telefone (E.164)")).toBeDisabled();
  await modal.getByLabel("Nome", { exact: true }).fill(`${nome} editado`);
  await modal.getByRole("button", { name: "Salvar", exact: true }).click();
  await expect(modal).toBeHidden();
  await expect(registro).toContainText(`${nome} editado`);
});

test("categorias podem ser expandidas sem perder a ativação e sem overflow", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 932 });
  await page.goto(`${BASE}/painel/login`);
  await page.getByLabel("E-mail").fill("admin@acme.dev");
  await page.getByLabel("Senha").fill("Admin!123");
  await page.getByRole("button", { name: "Entrar", exact: true }).click();
  await expect(page).toHaveURL(/\/painel$/);
  const resposta = page.waitForResponse(r => r.url().endsWith("/painel/categorias") && r.request().method() === "GET");
  await page.goto(`${BASE}/painel/servicos`);
  let total = (await (await resposta).json()).length;
  const nome = `Categoria expansão ${Date.now()}`;
  // Garante um conjunto expandível mesmo em uma base de desenvolvimento pequena.
  for (let i = 0; i < Math.max(1, 7 - total); i++) {
    await page.getByRole("button", { name: "Nova categoria", exact: true }).click();
    const modal = page.getByRole("dialog", { name: "Nova categoria" });
    await modal.getByLabel("Nome", { exact: true }).fill(`${nome} ${i}`);
    await modal.getByRole("button", { name: "Criar", exact: true }).click();
    await expect(modal).toBeHidden();
  }
  total += Math.max(1, 7 - total);
  const categorias = page.locator("#categorias-cadastro button");
  const expandir = page.getByRole("button", { name: `Ver todas as categorias (${total})`, exact: true });
  await expect(categorias).toHaveCount(6);
  for (const largura of [375, 390, 430, 768, 1024, 1440]) {
    await page.setViewportSize({ width: largura, height: 932 });
    await expandir.focus();
    await page.keyboard.press("Enter");
    await expect(categorias).toHaveCount(total);
    await expect(page.getByRole("button", { name: "Recolher categorias" })).toHaveAttribute("aria-expanded", "true");
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.getByRole("button", { name: "Recolher categorias" }).click();
    await expect(categorias).toHaveCount(6);
  }
  await expandir.click();
  const criada = page.locator("#categorias-cadastro").getByRole("button", { name: `${nome} 0`, exact: true });
  await expect(criada).toHaveAttribute("title", "Clique para desativar");
  await criada.click();
  await expect(criada).toHaveAttribute("title", "Clique para ativar");
  await expect(categorias).toHaveCount(total);
  await criada.click();
  await expect(criada).toHaveAttribute("title", "Clique para desativar");
});
