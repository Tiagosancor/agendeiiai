import { test, expect } from "@playwright/test";
import type { GradeAgenda } from "../src/lib/tipos";

const BASE = "http://app.agendeiiai.localhost:3000";

for (const tema of ["light", "dark"]) {
  test(`grade mobile navega entre profissionais reais sem overflow no tema ${tema}`, async ({ page }, testInfo) => {
    test.setTimeout(90_000);
    await page.addInitScript(tema => localStorage.setItem("tema", tema), tema);
    await page.goto(`${BASE}/painel/login`);
    await page.getByLabel("E-mail").fill("admin@acme.dev");
    await page.getByLabel("Senha").fill("Admin!123");
    await page.getByRole("button", { name: "Entrar", exact: true }).click();
    await expect(page).toHaveURL(/\/painel$/);
    const respostaGrade = page.waitForResponse(resposta => resposta.url().includes("/painel/agenda/grade?") && resposta.status() === 200);
    await page.goto(`${BASE}/painel/agenda`);
    const dados: GradeAgenda = await (await respostaGrade).json();
    expect(dados.profissionais.length).toBeGreaterThan(1);
    const ultimo = dados.profissionais.at(-1)!;
    const area = page.getByRole("region", { name: "Grade de profissionais" });
    const tabela = area.getByRole("table", { name: "Agenda do dia" });
    const navegador = page.locator(".painel-grade-navegacao select");

    for (const largura of [375, 390, 430, 768]) {
      await page.setViewportSize({ width: largura, height: 932 });
      await expect(navegador).toBeVisible();
      await expect(page.locator(".painel-grade-navegacao").getByRole("combobox", { name: "Profissional", exact: true })).toBeVisible();
      await expect(navegador.locator("option")).toHaveCount(dados.profissionais.length);
      if (largura === 375) await expect(navegador).toHaveValue(dados.profissionais[0].profissionalId);
      await expect(page.getByText("Deslize para ver outros profissionais")).toBeVisible();
      await expect(tabela.locator("tbody tr")).toHaveCount(dados.horarios.length);
      await navegador.selectOption(ultimo.profissionalId);
      await expect(navegador).toHaveValue(ultimo.profissionalId);
      const cabecalho = tabela.locator(`th[data-profissional-id="${ultimo.profissionalId}"]`);
      await expect(cabecalho).toBeInViewport();
      await expect.poll(async () => area.evaluate(elemento => elemento.scrollLeft)).toBeGreaterThan(0);
      const medidas = await tabela.evaluate(elemento => {
        const hora = elemento.querySelector("thead th")!;
        const coluna = elemento.querySelector("thead th:nth-child(2)")!;
        const pai = elemento.parentElement!;
        return { horaX: hora.getBoundingClientRect().left, horaLargura: hora.getBoundingClientRect().width, areaX: pai.getBoundingClientRect().left,
          largura: coluna.getBoundingClientRect().width, sticky: getComputedStyle(hora).position };
      });
      expect(Math.abs(medidas.horaX - medidas.areaX)).toBeLessThan(3);
      expect(medidas.sticky).toBe("sticky");
      expect(medidas.horaLargura).toBeCloseTo(70, 0);
      expect(medidas.largura).toBeGreaterThanOrEqual(248);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      for (const nome of ["Novo agendamento", "Atendimento sem agendamento"]) {
        const botao = page.getByRole("button", { name: nome });
        await expect(botao).toBeVisible();
        expect((await botao.boundingBox())!.height).toBeGreaterThanOrEqual(44);
      }
      if (largura === 390) {
        await page.screenshot({ path: testInfo.outputPath(`grade-${tema}-390.png`) });
      }
    }

    // O atalho só rola a grade; clicar continua abrindo o mesmo fluxo real.
    const livre = dados.profissionais.find(profissional => profissional.celulas.some(celula => celula.estado === "Livre"));
    expect(livre).toBeTruthy();
    await page.emulateMedia({ reducedMotion: "reduce" });
    await navegador.selectOption(livre!.profissionalId);
    const hora = livre!.celulas.find(celula => celula.estado === "Livre")!;
    const criar = tabela.getByRole("button", { name: `Livre: ${livre!.nome} às ${hora.hora}` });
    expect((await criar.boundingBox())!.height).toBeGreaterThanOrEqual(44);
    await criar.click();
    const modal = page.getByRole("dialog", { name: "Novo agendamento" });
    await expect(modal.getByText(`Com ${livre!.nome}`)).toBeVisible();
    await expect(modal.getByLabel(/^Horário/)).toHaveValue(hora.inicio);
    await page.keyboard.press("Escape");
    await expect(modal).toBeHidden();
    await page.getByRole("tab", { name: "Por profissional" }).click();
    await expect(page.getByRole("combobox", { name: "Profissional", exact: true })).toBeVisible();
    await page.getByRole("tab", { name: "Dia (todos)" }).click();
    await page.getByLabel("Esconder quem está de folga").check();
    await expect(tabela.locator("th[data-profissional-id]")).toHaveCount(dados.profissionais.filter(p => !p.deFolga).length);
    await page.getByLabel("Esconder quem está de folga").uncheck();

    await page.setViewportSize({ width: 1024, height: 900 });
    await expect(navegador).toBeHidden();
    expect(await tabela.locator("thead th").first().evaluate(elemento => getComputedStyle(elemento).position)).toBe("static");
    expect(await tabela.locator("td > button").first().evaluate(elemento => getComputedStyle(elemento).height)).toBe("34px");
  });
}
