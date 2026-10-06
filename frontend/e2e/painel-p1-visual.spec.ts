import { test, expect, type Page } from "@playwright/test";

const BASE = "http://app.agendeiiai.localhost:3000";
const API = "http://localhost:5080";
const LARGURAS = [375, 390, 430, 768, 1024, 1440];
test.use({ serviceWorkers: "block" });

async function responsivo(page: Page) {
  for (const width of LARGURAS) {
    await page.setViewportSize({ width, height: 812 });
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    for (const controle of await page.locator(".painel-recuperacao input, .painel-recuperacao button, .painel-recuperacao a, .painel-login input, .painel-login button").all()) {
      const caixa = await controle.boundingBox();
      if (caixa) expect(caixa.height).toBeGreaterThanOrEqual(44);
    }
  }
}

for (const tema of ["light", "dark"]) {
  test(`família de autenticação: layout e estados preservados ${tema}`, async ({ page }, testInfo) => {
    await page.addInitScript(t => localStorage.setItem("tema", t), tema);
    let liberar: () => void = () => {};
    let corpo: unknown;
    let pendentes = 0;
    let status = 202;
    await page.route(`${API}/painel/auth/esqueci-senha`, async route => {
      corpo = route.request().postDataJSON();
      await new Promise<void>(resolve => { liberar = resolve; pendentes++; });
      await route.fulfill(status === 202 ? { status } : { status, json: { title: "Muitas tentativas" } });
    });
    for (const rota of ["login", "esqueci-senha", "redefinir-senha"]) {
      await page.goto(`${BASE}/painel/${rota}`);
      await expect(page.locator(".painel-marca strong")).toHaveText("agendei ai");
      await responsivo(page);
      await page.screenshot({ path: testInfo.outputPath(`${rota}-${tema}.png`), fullPage: true });
    }
    await expect(page.getByText("Este link está incompleto.", { exact: false })).toBeVisible();
    await page.goto(`${BASE}/painel/esqueci-senha`);
    await page.getByLabel("E-mail").fill("recepcao@teste.dev");
    await page.getByRole("button", { name: "Enviar link" }).click();
    await expect(page.getByRole("button", { name: "Enviando..." })).toBeDisabled();
    await expect.poll(() => pendentes).toBe(1);
    expect(corpo).toEqual({ email: "recepcao@teste.dev" });
    status = 429; liberar();
    await expect(page.locator(".painel-recuperacao").getByRole("alert")).toHaveText("Muitas tentativas. Aguarde um minuto e tente de novo.");
    status = 202;
    await page.getByRole("button", { name: "Enviar link" }).click();
    await expect(page.getByRole("button", { name: "Enviando..." })).toBeDisabled();
    await expect.poll(() => pendentes).toBe(2);
    liberar();
    await expect(page.getByText("vai receber um e-mail", { exact: false })).toBeVisible();
    await responsivo(page);

    let redefinicoes = 0;
    status = 400;
    await page.route(`${API}/painel/auth/redefinir-senha`, async route => {
      redefinicoes++; corpo = route.request().postDataJSON();
      await new Promise<void>(resolve => { liberar = resolve; pendentes++; });
      await route.fulfill(status === 204 ? { status } : { status, json: { title: "Link inválido ou expirado." } });
    });
    await page.goto(`${BASE}/painel/redefinir-senha?token=token-local-simulado`);
    await expect(page).toHaveURL(/\/painel\/redefinir-senha$/);
    await responsivo(page);
    await page.getByLabel("Senha nova", { exact: true }).fill("curta");
    await page.getByRole("button", { name: "Salvar senha nova" }).click();
    await expect(page.locator(".painel-recuperacao").getByRole("alert")).toHaveText("A senha precisa ter pelo menos 8 caracteres, com letras e números.");
    expect(redefinicoes).toBe(0);
    await page.getByLabel("Senha nova", { exact: true }).fill("SenhaNova2026");
    await page.getByRole("button", { name: "Salvar senha nova" }).click();
    await expect(page.getByRole("button", { name: "Salvando..." })).toBeDisabled();
    await expect.poll(() => pendentes).toBe(3);
    expect(corpo).toEqual({ token: "token-local-simulado", novaSenha: "SenhaNova2026" });
    liberar();
    await expect(page.locator(".painel-recuperacao").getByRole("alert")).toHaveText("Link inválido ou expirado.");
    status = 204;
    await page.getByRole("button", { name: "Salvar senha nova" }).click();
    await expect(page.getByRole("button", { name: "Salvando..." })).toBeDisabled();
    await expect.poll(() => pendentes).toBe(4);
    liberar();
    await expect(page.getByText("Senha alterada.", { exact: false })).toBeVisible();
    await responsivo(page);
    await expect(page.getByRole("link", { name: "Ir para a entrada" })).toHaveAttribute("href", "/painel/login");
  });

  for (const permissao of ["GerenciarComissoes", "VerComissoesDeTodos", "sem-comissao"]) {
    test(`comissão da ficha: apresentação e permissão ${permissao} ${tema}`, async ({ page, request }, testInfo) => {
      await page.addInitScript(t => localStorage.setItem("tema", t), tema);
      const login = await request.post(`${API}/painel/auth/login`, { data: { email: "admin@acme.dev", senha: "Admin!123" } });
      const { accessToken } = await login.json();
      const auth = { Authorization: `Bearer ${accessToken}` };
      const profissionais = await (await request.get(`${API}/painel/profissionais`, { headers: auth })).json();
      const profissional = profissionais.find((p: { ativo: boolean }) => p.ativo);
      expect(profissional).toBeTruthy();
      // Claims simuladas somente na UI; leituras locais usam o token real. Nenhuma gravação chega à API.
      const token = `e30.${Buffer.from(JSON.stringify({ permissao: permissao === "sem-comissao" ? [] : [permissao] })).toString("base64url")}.teste`;
      await page.route("**/painel/auth/**", route => route.fulfill({ json: { accessToken: token, expiraEm: "2099-01-01T00:00:00Z" } }));
      let consultas = 0, gravacoes = 0, corpo: unknown;
      await page.route(`${API}/painel/**`, async route => {
        const req = route.request();
        if (req.method() !== "GET") {
          expect(req.method()).toBe("PUT");
          expect(new URL(req.url()).pathname).toBe(`/painel/profissionais/${profissional.id}/comissao`);
          gravacoes++; corpo = req.postDataJSON();
          return route.fulfill({ status: 204 });
        }
        if (req.url().endsWith("/comissao")) consultas++;
        const resposta = await route.fetch({ headers: { ...req.headers(), authorization: auth.Authorization } });
        return route.fulfill({ response: resposta });
      });
      await page.goto(`${BASE}/painel/profissionais/${profissional.id}`);
      await expect(page.getByRole("heading", { name: profissional.nome, exact: true })).toBeVisible();
      const secao = page.locator("section").filter({ has: page.getByRole("heading", { name: "Comissão", exact: true }) });
      if (permissao === "sem-comissao") {
        await expect(secao).toHaveCount(0); expect(consultas).toBe(0); return;
      }
      const campo = secao.getByLabel("Comissão (%)", { exact: true });
      await expect(campo).toBeVisible();
      for (const width of LARGURAS) {
        await page.setViewportSize({ width, height: 812 });
        expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
        expect((await campo.boundingBox())!.height).toBeGreaterThanOrEqual(44);
        expect((await page.getByRole("link", { name: "← Voltar para profissionais" }).boundingBox())!.height).toBeGreaterThanOrEqual(44);
        if ([390, 1440].includes(width)) await secao.screenshot({ path: testInfo.outputPath(`comissao-${tema}-${permissao}-${width}.png`) });
      }
      if (permissao === "VerComissoesDeTodos") {
        await expect(campo).toBeDisabled();
        await expect(secao.getByLabel("Comissão de produto (%)")).toBeDisabled();
        await expect(secao.getByLabel("Acerto por quinzena", { exact: false })).toBeDisabled();
        await expect(secao.getByRole("button", { name: "Salvar comissão" })).toHaveCount(0);
        expect(gravacoes).toBe(0); return;
      }
      await campo.fill("12,5");
      await secao.getByLabel("Comissão de produto (%)").fill("5");
      await secao.getByLabel("Acerto por quinzena", { exact: false }).check();
      await secao.getByRole("button", { name: "Salvar comissão" }).click();
      await expect(secao.getByRole("status")).toContainText("Comissão salva.");
      expect(corpo).toEqual({ percentual: 12.5, acertoPorQuinzena: true, percentualProdutoVenda: 5 });
      expect(gravacoes).toBe(1);
    });
  }
}
