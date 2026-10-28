import { test, expect, type Page } from "@playwright/test";

// Permite homologar este spec com APIs interceptadas no next start do worktree,
// em uma porta livre, sem depender da stack nem da Evolution real.
const BASE = process.env.WHATSAPP_E2E_BASE_URL ?? "http://app.agendeiiai.localhost:3000";
const QR = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aD1sAAAAASUVORK5CYII=";
test.use({ serviceWorkers: "block" });

async function sessao(page: Page, permitido = true, aoRenovar?: (token: string) => void) {
  const payload = Buffer.from(JSON.stringify({ permissao: permitido ? ["GerenciarConfiguracoesDoNegocio"] : [] })).toString("base64url");
  let renovacoes = 0;
  await page.route("**/painel/auth/renovar", route => {
    const token = `teste.${payload}.teste${++renovacoes}`;
    aoRenovar?.(token);
    return route.fulfill({ json: { accessToken: token, expiraEm: "2099-01-01T00:00:00Z" } });
  });
  await page.route("**/painel/assinatura/aviso", route => route.fulfill({ status: 204 }));
}

test("404, QR, conexão e alteração do agente com falha e sucesso", async ({ page }) => {
  await sessao(page);
  await page.clock.install();
  let conectado = false;
  let falhar = true;
  let consultas = 0;
  await page.route("**/painel/whatsapp/*", async route => {
    const caminho = new URL(route.request().url()).pathname;
    if (caminho.endsWith("/status")) {
      consultas++;
      return conectado ? route.fulfill({ json: { status: "open", telefone: "+5585999999999", agenteAtivo: false } }) : route.fulfill({ status: 404 });
    }
    if (caminho.endsWith("/conectar")) return route.fulfill({ json: { status: "AguardandoQrCode", qrCode: QR } });
    expect(route.request().method()).toBe("PUT");
    expect(route.request().postDataJSON()).toEqual({ ativo: true });
    return falhar ? route.fulfill({ status: 502, json: { mensagem: "WhatsApp indisponível agora." } }) : route.fulfill({ status: 204 });
  });
  await page.goto(`${BASE}/painel/whatsapp`);
  await expect(page.getByRole("status")).toHaveText("Ainda não conectado");
  await expect(page.getByRole("link", { name: "WhatsApp", exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Conectar WhatsApp" }).click();
  await expect(page.getByAltText("QR Code para conectar o WhatsApp do negócio")).toBeVisible();
  conectado = true;
  await page.clock.runFor(3000);
  await expect(page.getByRole("status")).toHaveText("Conectado", { timeout: 10000 });
  await expect(page.getByText("Número: +5585999999999")).toBeVisible();
  await expect(page.getByRole("img", { name: /QR Code/ })).toHaveCount(0);
  const controle = page.getByRole("switch", { name: /Agente ativo/ });
  await controle.click();
  await expect(page.getByRole("region", { name: "Conexão do WhatsApp" }).getByRole("alert")).toHaveText("WhatsApp indisponível agora.");
  await expect(controle).toHaveAttribute("aria-checked", "false");
  falhar = false;
  await controle.click();
  await expect(controle).toHaveAttribute("aria-checked", "true");
  const total = consultas;
  await page.clock.runFor(6000);
  expect(consultas).toBe(total);
});

test("expira em dois minutos, ignora consulta atrasada e permite novo QR", async ({ page }) => {
  await sessao(page);
  await page.clock.install();
  let consultas = 0;
  let liberar!: () => void;
  const pendente = new Promise<void>(resolve => { liberar = resolve; });
  await page.route("**/painel/whatsapp/*", async route => {
    if (route.request().url().endsWith("/conectar")) return route.fulfill({ json: { status: "connecting", qrCode: QR } });
    consultas++;
    if (consultas === 1) return route.fulfill({ status: 404 });
    await pendente;
    await route.fulfill({ json: { status: "open", telefone: null, agenteAtivo: true } }).catch(() => {});
  });
  await page.goto(`${BASE}/painel/whatsapp`);
  await page.getByRole("button", { name: "Conectar WhatsApp" }).click();
  await expect(page.getByAltText(/QR Code para conectar/)).toBeVisible();
  await page.clock.runFor(3000);
  await expect.poll(() => consultas).toBe(2);
  await page.clock.runFor(117000);
  await expect(page.getByRole("status")).toHaveText("O código expirou");
  liberar();
  await expect(page.getByRole("status")).toHaveText("O código expirou");
  await page.getByRole("button", { name: "Gerar novo QR" }).click();
  await expect(page.getByAltText(/QR Code para conectar/)).toBeVisible();
  await page.getByRole("link", { name: "Meu negócio", exact: true }).click();
  const total = consultas;
  await page.clock.runFor(6000);
  expect(consultas).toBe(total);
});

test("permissão protege menu e acesso direto", async ({ page }) => {
  await sessao(page, false);
  let chamadas = 0;
  await page.route("**/painel/whatsapp/*", route => { chamadas++; return route.fulfill({ status: 403 }); });
  await page.goto(`${BASE}/painel/whatsapp`);
  await expect(page.getByText("Sem permissão para gerenciar a conexão do WhatsApp.")).toBeVisible();
  await expect(page.getByRole("link", { name: "WhatsApp", exact: true })).toHaveCount(0);
  expect(chamadas).toBe(0);
});

test("estado real Conectada permite desativar e estado desconhecido permite reconectar", async ({ page }) => {
  await sessao(page);
  let estado = "Conectada";
  await page.route("**/painel/whatsapp/*", route => {
    if (route.request().method() === "PUT") {
      expect(route.request().postDataJSON()).toEqual({ ativo: false });
      return route.fulfill({ status: 204 });
    }
    return route.fulfill({ json: { status: estado, agenteAtivo: true, telefone: null } });
  });
  await page.goto(`${BASE}/painel/whatsapp`);
  await expect(page.getByRole("status")).toHaveText("Conectado");
  const controle = page.getByRole("switch", { name: /Agente ativo/ });
  await controle.click();
  await expect(controle).toHaveAttribute("aria-checked", "false");
  estado = "novo-estado-do-provedor";
  await page.reload();
  await expect(page.getByRole("status")).toHaveText("Desconectado/indisponível");
  await expect(page.getByRole("button", { name: "Conectar WhatsApp" })).toBeVisible();
});

test("502 inicial e ao conectar permite nova tentativa no celular", async ({ page }) => {
  await sessao(page);
  await page.setViewportSize({ width: 375, height: 812 });
  let falhar = true;
  await page.route("**/painel/whatsapp/*", route => falhar
    ? route.fulfill({ status: 502, json: { mensagem: "Tente de novo em instantes." } })
    : route.fulfill({ json: { status: "AguardandoQrCode", qrCode: QR } }));
  await page.goto(`${BASE}/painel/whatsapp`);
  await expect(page.getByRole("region", { name: "Conexão do WhatsApp" }).getByRole("alert")).toHaveText("Tente de novo em instantes.");
  await expect(page.getByRole("status")).toHaveText("Situação desconhecida");
  await page.getByRole("button", { name: "Conectar WhatsApp" }).click();
  await expect(page.getByRole("region", { name: "Conexão do WhatsApp" }).getByRole("alert")).toHaveText("Tente de novo em instantes.");
  falhar = false;
  await page.getByRole("button", { name: "Conectar WhatsApp" }).click();
  await expect(page.getByAltText(/QR Code para conectar/)).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

for (const operacao of ["conectar", "agente"] as const) {
  test(`401 em ${operacao} renova o token e libera o controle`, async ({ page }) => {
    const tokens: string[] = [];
    await sessao(page, true, token => tokens.push(token));
    let consultas = 0;
    let tentativas = 0;
    await page.route("**/painel/whatsapp/*", async route => {
      if (route.request().url().endsWith("/status")) {
        consultas++;
        return operacao === "conectar" ? route.fulfill({ status: 404 })
          : route.fulfill({ json: { status: "open", agenteAtivo: false, telefone: null } });
      }
      expect(route.request().url()).toContain(`/${operacao}`);
      tentativas++;
      expect(route.request().headers().authorization).toBe(`Bearer ${tokens.at(-1)}`);
      if (tentativas === 1) return route.fulfill({ status: 401 });
      return operacao === "conectar" ? route.fulfill({ json: { status: "connecting", qrCode: QR } })
        : route.fulfill({ status: 204 });
    });
    await page.goto(`${BASE}/painel/whatsapp`);
    if (operacao === "conectar") {
      await page.getByRole("button", { name: "Conectar WhatsApp" }).click();
      await expect(page.getByAltText(/QR Code para conectar/)).toBeVisible();
      await expect(page.getByRole("button", { name: "Gerar novo QR" })).toBeEnabled();
    } else {
      const controle = page.getByRole("switch", { name: "Agente ativo", exact: true });
      await controle.click();
      await expect(controle).toHaveAttribute("aria-checked", "true");
      await expect(controle).toBeEnabled();
    }
    expect(tentativas).toBe(2);
    expect(tokens).toHaveLength(2);
    expect(tokens[0]).not.toBe(tokens[1]);
    expect(consultas).toBe(1);
  });
}

test("renovação durante polling preserva o prazo original do QR", async ({ page }) => {
  const tokens: string[] = [];
  await sessao(page, true, token => tokens.push(token));
  await page.clock.install();
  let consultas = 0;
  await page.route("**/painel/whatsapp/*", route => {
    if (route.request().url().endsWith("/conectar")) return route.fulfill({ json: { status: "connecting", qrCode: QR } });
    consultas++;
    if (consultas === 1) return route.fulfill({ status: 404 });
    if (consultas === 2) return route.fulfill({ status: 401 });
    expect(route.request().headers().authorization).toBe(`Bearer ${tokens.at(-1)}`);
    return route.fulfill({ json: { status: "AguardandoQrCode", agenteAtivo: false, telefone: null } });
  });
  await page.goto(`${BASE}/painel/whatsapp`);
  await page.getByRole("button", { name: "Conectar WhatsApp" }).click();
  await expect(page.getByAltText(/QR Code para conectar/)).toBeVisible();
  await page.clock.runFor(60000);
  await expect.poll(() => tokens.length).toBe(2);
  await expect.poll(() => consultas).toBeGreaterThanOrEqual(3);
  await page.clock.runFor(60000);
  await expect(page.getByRole("status")).toHaveText("O código expirou");
  await expect(page.getByAltText(/QR Code para conectar/)).toHaveCount(0);
  const total = consultas;
  await page.clock.runFor(6000);
  expect(consultas).toBe(total);
});

test("carga inicial aguardando QR oferece gerar código sem polling cego", async ({ page }) => {
  await sessao(page);
  await page.clock.install();
  let consultas = 0;
  await page.route("**/painel/whatsapp/*", route => {
    consultas++;
    return route.fulfill({ json: { status: "AguardandoQrCode", agenteAtivo: false, telefone: null } });
  });
  await page.goto(`${BASE}/painel/whatsapp`);
  await expect(page.getByRole("status")).toHaveText("Gere um novo QR para continuar");
  await expect(page.getByRole("button", { name: "Gerar novo QR" })).toBeEnabled();
  await page.clock.runFor(126000);
  expect(consultas).toBe(1);
});

test("Desconectada durante polling remove QR e encerra consultas", async ({ page }) => {
  await sessao(page);
  await page.clock.install();
  let consultas = 0;
  await page.route("**/painel/whatsapp/*", route => {
    if (route.request().url().endsWith("/conectar")) return route.fulfill({ json: { status: "connecting", qrCode: QR } });
    consultas++;
    return consultas === 1 ? route.fulfill({ status: 404 })
      : route.fulfill({ json: { status: "Desconectada", agenteAtivo: false, telefone: null } });
  });
  await page.goto(`${BASE}/painel/whatsapp`);
  await page.getByRole("button", { name: "Conectar WhatsApp" }).click();
  await expect(page.getByAltText(/QR Code para conectar/)).toBeVisible();
  await page.clock.runFor(3000);
  await expect(page.getByRole("status")).toHaveText("Desconectado/indisponível");
  await expect(page.getByAltText(/QR Code para conectar/)).toHaveCount(0);
  const total = consultas;
  await page.clock.runFor(126000);
  expect(consultas).toBe(total);
  await expect(page.getByRole("status")).toHaveText("Desconectado/indisponível");
});
