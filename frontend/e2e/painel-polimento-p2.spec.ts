import { test, expect, type Page } from "@playwright/test";

const BASE = "http://app.agendeiiai.localhost:3000";
test.use({ serviceWorkers: "block" });

type Aviso = {
  estado: "EmTeste" | "Ativa" | "Atrasada" | "Suspensa";
  prazo: string | null;
  diasRestantes: number | null;
  destacado: boolean;
};

async function entrar(page: Page, tema: string) {
  await page.addInitScript(valor => localStorage.setItem("tema", valor), tema);
  await page.goto(`${BASE}/painel/login`);
  await page.getByLabel("E-mail").fill("admin@acme.dev");
  await page.getByLabel("Senha", { exact: true }).fill("Admin!123");
  await page.getByRole("button", { name: "Entrar", exact: true }).click();
  await expect(page).toHaveURL(/\/painel$/);
}

for (const tema of ["light", "dark"]) {
  test(`aviso de assinatura preserva estados, ação e responsividade no tema ${tema}`, async ({ page }, testInfo) => {
    let aviso: Aviso | null = { estado: "EmTeste", prazo: "2026-10-12", diasRestantes: 3, destacado: false };
    await page.route("**/painel/assinatura/aviso", async route => {
      return aviso ? route.fulfill({ json: aviso }) : route.fulfill({ status: 204 });
    });
    await entrar(page, tema);

    const cenarios: Array<{ aviso: Aviso; texto: RegExp }> = [
      { aviso: { estado: "EmTeste", prazo: "2026-10-12", diasRestantes: 3, destacado: false }, texto: /teste grátis termina em 3 dias/i },
      { aviso: { estado: "Ativa", prazo: "2026-10-10", diasRestantes: 1, destacado: false }, texto: /assinatura vence amanhã/i },
      { aviso: { estado: "Atrasada", prazo: null, diasRestantes: null, destacado: true }, texto: /pagamento da assinatura está pendente/i },
      { aviso: { estado: "Suspensa", prazo: null, diasRestantes: null, destacado: true }, texto: /assinatura está suspensa/i },
    ];

    for (const largura of [375, 390, 768, 1440]) {
      await page.setViewportSize({ width: largura, height: 932 });
      for (const cenario of cenarios) {
        aviso = cenario.aviso;
        await page.reload();
        const componente = page.getByTestId("aviso-assinatura");
        await expect(componente).toContainText(cenario.texto);
        await expect(componente).toHaveAttribute("data-estado", cenario.aviso.estado);
        await expect(componente.getByRole("link", { name: "Assinar agora" })).toHaveAttribute("href", "/painel/assinatura");
        const acao = componente.getByRole("link", { name: "Assinar agora" });
        expect((await acao.boundingBox())!.height).toBeGreaterThanOrEqual(44);
        await acao.focus();
        expect(await acao.evaluate(elemento => getComputedStyle(elemento).outlineStyle)).not.toBe("none");
        expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `${cenario.aviso.estado} em ${largura}px`).toBe(true);
      }
      await page.screenshot({ path: testInfo.outputPath(`aviso-${tema}-${largura}.png`), fullPage: true });
    }

    aviso = null;
    await page.reload();
    await expect(page.getByTestId("aviso-assinatura")).toHaveCount(0);
  });
}

test("painel respeita preferência por movimento reduzido", async ({ page }) => {
  await page.emulateMedia({ reducedMotion: "reduce" });
  await entrar(page, "dark");
  const duracoes = await page.locator(".painel-visual").evaluate(elemento => {
    const estilo = getComputedStyle(elemento);
    return { animacao: estilo.animationDuration, transicao: estilo.transitionDuration };
  });
  expect(["0s", "0.01ms"]).toContain(duracoes.animacao);
  expect(["0s", "0.01ms"]).toContain(duracoes.transicao);
});
