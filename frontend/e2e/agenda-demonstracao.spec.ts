import { test, expect } from "@playwright/test";

const RAIZ = "http://agendeiiai.localhost:3000";

test("Agenda filtra, altera status, cria e reinicia somente em memória, sem API", async ({ page }) => {
  await page.emulateMedia({ reducedMotion: "reduce" });
  await page.goto(RAIZ);
  const chamadas: string[] = [];
  page.on("request", req => {
    // O Next pré-carrega links existentes da landing; isso não é tráfego da microdemo.
    const prefetchNext = req.method() === "GET" && new URL(req.url()).searchParams.has("_rsc");
    if (["fetch", "xhr"].includes(req.resourceType()) && !prefetchNext) chamadas.push(req.url());
  });
  const demo = page.getByRole("figure", { name: "Demonstração visual da Agenda" });
  const lista = demo.locator(".agenda-demo-agendamentos");
  const profissional = demo.getByRole("combobox", { name: "Profissional", exact: true });
  const data = demo.getByLabel("Data", { exact: true });
  await profissional.selectOption("Mariana Alves");
  await expect(lista.locator("li")).toHaveCount(1);
  await expect(lista).toContainText("10:00–10:30");
  await profissional.selectOption("Carlos Santos");
  await expect(lista).toContainText("11:00–11:20");
  await data.fill("2026-09-26");
  await expect(lista).toHaveText("Nenhum agendamento para esta data");
  await profissional.selectOption("Diego Lima");
  await expect(lista).toContainText("10:30–11:00");
  await demo.getByRole("button", { name: "Reiniciar demo" }).click();
  for (const [acao, status] of [["Concluir", "Concluído"], ["Faltou", "Faltou"], ["Cancelar", "Cancelado"]]) {
    await lista.locator("li").first().getByRole("button", { name: acao, exact: true }).click();
    await expect(lista.locator("li").first().locator("[data-status]")).toHaveText(status);
  }
  await demo.getByRole("button", { name: "Novo agendamento", exact: true }).click();
  const modal = page.getByRole("dialog", { name: "Novo agendamento", exact: true });
  await expect(modal).toBeVisible();
  await modal.getByRole("combobox", { name: "Cliente", exact: true }).selectOption("André Rocha");
  await modal.getByRole("combobox", { name: "Serviço", exact: true }).selectOption("1");
  await modal.getByRole("combobox", { name: "Horário", exact: true }).selectOption("15:00");
  await modal.getByRole("button", { name: "Criar agendamento" }).click();
  await expect(modal).not.toBeVisible();
  await expect(lista.locator("li")).toHaveCount(3);
  await expect(lista).toContainText("15:00–15:20");
  await expect(demo.getByRole("status")).toHaveText("Agendamento criado na demonstração");
  await expect(demo.getByRole("button", { name: "Novo agendamento", exact: true })).toBeFocused();
  await demo.getByRole("button", { name: "Reiniciar demo" }).click();
  await expect(lista.locator("li")).toHaveCount(2);
  await expect(lista.locator("[data-status]")).toHaveText(["Agendado", "Agendado"]);
  expect(chamadas).toEqual([]);
  await page.reload();
  await expect(demo.locator(".agenda-demo-agendamentos > li")).toHaveCount(2);
});

test("modal mantém foco, fecha com Escape e controles funcionam com touch em ambos os temas", async ({ browser }) => {
  const contexto = await browser.newContext({ viewport: { width: 375, height: 812 }, hasTouch: true, isMobile: true });
  const page = await contexto.newPage();
  await page.emulateMedia({ reducedMotion: "reduce" });
  await page.goto(RAIZ);
  const demo = page.locator(".site-agenda-demo");
  for (const tema of ["light", "dark"] as const) {
    await page.emulateMedia({ colorScheme: tema, reducedMotion: "reduce" });
    await demo.getByRole("button", { name: "Novo agendamento", exact: true }).tap();
    const modal = page.getByRole("dialog", { name: "Novo agendamento", exact: true });
    await expect(modal.getByRole("combobox", { name: "Cliente", exact: true })).toBeFocused();
    await page.keyboard.press("Shift+Tab");
    await expect(modal.getByRole("button", { name: "Criar agendamento" })).toBeFocused();
    await page.keyboard.press("Tab");
    await expect(modal.getByRole("combobox", { name: "Cliente", exact: true })).toBeFocused();
    const area = (await modal.boundingBox())!;
    expect(area.x).toBeGreaterThanOrEqual(0);
    expect(area.x + area.width).toBeLessThanOrEqual(375);
    expect(area.y).toBeGreaterThanOrEqual(0);
    expect(area.y + area.height).toBeLessThanOrEqual(812);
    await page.screenshot({ path: `test-results/agenda-modal-${tema}-375.png` });
    await page.keyboard.press("Escape");
    await expect(modal).not.toBeVisible();
    await expect(demo.getByRole("button", { name: "Novo agendamento", exact: true })).toBeFocused();
    await demo.locator(".agenda-demo-agendamentos > li").first().getByRole("button", { name: "Concluir" }).tap();
    await expect(demo.locator("[data-status]").first()).toHaveText("Concluído");
    await demo.getByRole("button", { name: "Reiniciar demo" }).tap();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBeTruthy();
  }
  await contexto.close();
});
