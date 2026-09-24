import { expect, test, type Page } from "@playwright/test";

// Domínio raiz, sem tenant — a tela de fallback com o logotipo completo.
const URL_RAIZ = "http://agendeiiai.localhost:3000/";

const FUNDO_CLARO = "rgb(250, 249, 246)"; // #FAF9F6
const FUNDO_ESCURO = "rgb(18, 24, 31)"; // #12181F

/** Fundo e logo precisam SEMPRE refletir o mesmo data-theme — nunca lógicas separadas. */
async function esperarTema(pagina: Page, tema: "light" | "dark") {
  await expect(pagina.locator("html")).toHaveAttribute("data-theme", tema);
  await expect
    .poll(() => pagina.evaluate(() => getComputedStyle(document.body).backgroundColor))
    .toBe(tema === "dark" ? FUNDO_ESCURO : FUNDO_CLARO);

  const logoClaro = pagina.getByTestId("logo-claro");
  const logoEscuro = pagina.getByTestId("logo-escuro");
  if (tema === "dark") {
    await expect(logoEscuro).toBeVisible();
    await expect(logoClaro).toBeHidden();
  } else {
    await expect(logoClaro).toBeVisible();
    await expect(logoEscuro).toBeHidden();
  }
}

test("primeira visita segue o sistema (claro) e o botão troca e persiste a escolha", async ({ browser }) => {
  const contexto = await browser.newContext({ colorScheme: "light" });
  const pagina = await contexto.newPage();

  await pagina.goto(URL_RAIZ);
  await esperarTema(pagina, "light");

  await pagina.getByTestId("botao-tema").click();
  await esperarTema(pagina, "dark");

  // Escolha manual sobrepõe o sistema, inclusive depois de recarregar.
  await pagina.reload();
  await esperarTema(pagina, "dark");

  await pagina.getByTestId("botao-tema").click();
  await esperarTema(pagina, "light");

  await contexto.close();
});

test("primeira visita segue o sistema (escuro) e o botão sobrepõe", async ({ browser }) => {
  const contexto = await browser.newContext({ colorScheme: "dark" });
  const pagina = await contexto.newPage();

  await pagina.goto(URL_RAIZ);
  await esperarTema(pagina, "dark");

  await pagina.getByTestId("botao-tema").click();
  await esperarTema(pagina, "light");

  await pagina.reload();
  await esperarTema(pagina, "light");

  await contexto.close();
});

test("sem escolha manual, sistema trocando com a página aberta leva fundo e logo juntos", async ({ browser }) => {
  const contexto = await browser.newContext({ colorScheme: "light" });
  const pagina = await contexto.newPage();

  await pagina.goto(URL_RAIZ);
  await esperarTema(pagina, "light");

  // Cenário do bug: antes, o fundo seguia a media query e a logo ficava na classe antiga.
  await pagina.emulateMedia({ colorScheme: "dark" });
  await esperarTema(pagina, "dark");

  await pagina.emulateMedia({ colorScheme: "light" });
  await esperarTema(pagina, "light");

  await contexto.close();
});

test("com escolha manual, trocar o tema do sistema não muda nada", async ({ browser }) => {
  const contexto = await browser.newContext({ colorScheme: "light" });
  const pagina = await contexto.newPage();

  await pagina.goto(URL_RAIZ);
  await pagina.getByTestId("botao-tema").click();
  await esperarTema(pagina, "dark");

  await pagina.emulateMedia({ colorScheme: "dark" });
  await pagina.emulateMedia({ colorScheme: "light" });
  await esperarTema(pagina, "dark");

  await contexto.close();
});
