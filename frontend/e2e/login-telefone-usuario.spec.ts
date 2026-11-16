import { test, expect, type Page, type Route } from "@playwright/test";
import type { MinhaConta } from "../src/lib/tipos";

const PAINEL = process.env.E2E_PAINEL_URL ?? "http://app.agendeiiai.localhost:3000";
const token = `e30.${Buffer.from(JSON.stringify({ sub: "usuario-atual", permissao: ["GerenciarUsuarios", "EditarCadastros", "GerenciarProfissionais"] })).toString("base64url")}.assinatura`;
const contaInicial: MinhaConta = { nome: "Pessoa de teste", perfil: "Recepcionista", email: null, emailVerificado: false, emailPendenteMascarado: null, nomeUsuario: "pessoa.teste", telefoneLogin: "+5571988887777", senhaDefinidaPorTerceiro: true };
test.use({ serviceWorkers: "block" });

async function preparar(page: Page, interceptar?: (route: Route, caminho: string) => Promise<boolean>, conta: MinhaConta = { ...contaInicial }) {
  await page.route("**/*", async (route) => {
    const pedido = route.request();
    const url = new URL(pedido.url());
    // Preserva documentos, assets e navegação do Next. Só substitui chamadas de API.
    if (!(url.port === "5080" || url.pathname.startsWith("/painel/auth/"))) return route.continue();
    const caminho = url.pathname;
    if (interceptar && await interceptar(route, caminho)) return;
    if (caminho === "/painel/auth/renovar" || caminho === "/painel/auth/login") return route.fulfill({ json: { accessToken: token, expiraEm: "2030-01-01T00:00:00Z" } });
    if (caminho === "/painel/minha-conta") return route.fulfill({ json: conta });
    if (caminho === "/painel/assinatura/aviso") return route.fulfill({ status: 204 });
    if (caminho === "/painel/usuarios") return route.fulfill({ json: [] });
    return route.fulfill({ json: [] });
  });
}

for (const caso of [
  { status: 401, corpo: undefined, mensagem: "Dados de acesso ou senha incorretos." },
  { status: 429, corpo: undefined, mensagem: "Muitas tentativas. Aguarde alguns minutos e tente de novo." },
  { status: 429, corpo: { codigo: "muitas_tentativas", detail: "Bloqueio por identificador" }, mensagem: "Muitas tentativas. Aguarde alguns minutos e tente de novo." },
]) test(`login exibe mensagem neutra: HTTP ${caso.status}, código ${caso.corpo?.codigo ?? "ausente"}`, async ({ page }) => {
  let enviado: unknown;
  await preparar(page, async (route, caminho) => {
    if (caminho === "/painel/auth/renovar") { await route.fulfill({ status: 401 }); return true; }
    if (caminho === "/painel/auth/login") {
      enviado = route.request().postDataJSON();
      await route.fulfill({ status: caso.status, ...(caso.corpo ? { json: caso.corpo } : { body: "" }) }); return true;
    }
    return false;
  });
  await page.goto(`${PAINEL}/painel/login`);
  const campo = page.getByLabel("E-mail, telefone ou usuário", { exact: true });
  await expect(campo).toHaveAttribute("type", "text");
  await expect(campo).toHaveAttribute("autocomplete", "username");
  await campo.fill("(71) 98888-7777");
  await page.getByLabel("Senha", { exact: true }).fill("Errada123");
  await page.getByRole("button", { name: "Entrar", exact: true }).click();
  await expect(page.getByRole("alert").filter({ hasText: caso.mensagem })).toHaveText(caso.mensagem);
  expect(enviado).toEqual({ identificador: "(71) 98888-7777", senha: "Errada123" });
});

test("esqueci senha orienta quem não tem e-mail confirmado", async ({ page }) => {
  await preparar(page, async (route, caminho) => {
    if (caminho === "/painel/auth/renovar") { await route.fulfill({ status: 401 }); return true; }
    return false;
  });
  await page.goto(`${PAINEL}/painel/esqueci-senha`);
  await expect(page.getByText("peça ao administrador", { exact: false })).toBeVisible();
});

test("cria usuário sem e-mail, sugere usuário e só copia telefone por escolha explícita", async ({ page }) => {
  let enviado: Record<string, unknown> | undefined;
  await preparar(page, async (route, caminho) => {
    if (caminho === "/painel/usuarios" && route.request().method() === "POST") {
      enviado = route.request().postDataJSON(); await route.fulfill({ status: 201, json: "usuario-novo" }); return true;
    }
    return false;
  });
  await page.goto(`${PAINEL}/painel/usuarios`);
  await page.getByRole("button", { name: "Novo usuário", exact: true }).click();
  const modal = page.getByRole("dialog", { name: "Novo usuário", exact: true });
  await modal.getByLabel("Nome", { exact: true }).fill("João Silva");
  await expect(modal.getByLabel("Nome de usuário (opcional)", { exact: true })).toHaveValue("joao.silva");
  await modal.getByLabel("Telefone (opcional)", { exact: true }).fill("(71) 98888-7777");
  await expect(modal.getByLabel("Telefone de login (opcional)", { exact: true })).toHaveValue("");
  await modal.getByRole("button", { name: "Usar o telefone do cadastro para entrar" }).click();
  await modal.getByLabel("Senha", { exact: true }).fill("SenhaForte123");
  await modal.getByRole("button", { name: "Criar", exact: true }).click();
  await expect(modal).toBeHidden();
  expect(enviado).toMatchObject({ email: null, nomeUsuario: "joao.silva", telefoneLogin: "(71) 98888-7777", telefone: "(71) 98888-7777" });
});

test("valida pelo menos um identificador e apresenta conflito no campo com sugestão", async ({ page }) => {
  let pedidos = 0;
  await preparar(page, async (route, caminho) => {
    if (caminho === "/painel/usuarios" && route.request().method() === "POST") {
      pedidos++;
      await route.fulfill({ status: 409, json: { codigo: "identificador_indisponivel", campo: "nomeUsuario", sugestao: "joao.silva2", detail: "Este nome de usuário não está disponível." } }); return true;
    }
    return false;
  });
  await page.goto(`${PAINEL}/painel/usuarios`);
  await page.getByRole("button", { name: "Novo usuário", exact: true }).click();
  const modal = page.getByRole("dialog");
  await modal.getByLabel("Nome", { exact: true }).fill("João Silva");
  const usuario = modal.getByLabel("Nome de usuário (opcional)", { exact: true });
  await usuario.fill("");
  await modal.getByLabel("Senha", { exact: true }).fill("SenhaForte123");
  await modal.getByRole("button", { name: "Criar", exact: true }).click();
  await expect(modal.getByText("Informe pelo menos um:", { exact: false })).toBeVisible();
  expect(pedidos).toBe(0);
  await usuario.fill("joao.silva");
  await modal.getByRole("button", { name: "Criar", exact: true }).click();
  await expect(usuario).toHaveAttribute("aria-invalid", "true");
  await modal.getByRole("button", { name: "Usar joao.silva2" }).click();
  await expect(usuario).toHaveValue("joao.silva2");
});

test("Minha conta confirma e-mail com resposta neutra, reenvia e remove aviso", async ({ page }) => {
  const conta = { ...contaInicial };
  let pedidos = 0;
  await preparar(page, async (route, caminho) => {
    if (caminho === "/painel/minha-conta/email") { pedidos++; await route.fulfill({ status: 202 }); return true; }
    if (caminho === "/painel/minha-conta/email/confirmar") {
      expect(route.request().postDataJSON()).toEqual({ codigo: "123456" });
      conta.email = "pessoa@exemplo.com"; conta.emailVerificado = true;
      await route.fulfill({ status: 204 }); return true;
    }
    return false;
  }, conta);
  await page.goto(`${PAINEL}/painel/minha-conta`);
  const aviso = page.getByRole("complementary", { name: "Cadastro da sua conta" });
  await expect(aviso).toContainText("Complete seu cadastro");
  await page.getByLabel("E-mail", { exact: true }).fill("pessoa@exemplo.com");
  await page.getByLabel("Senha atual para confirmar e-mail", { exact: true }).fill("SenhaForte123");
  await page.getByRole("button", { name: "Solicitar código", exact: true }).click();
  await expect(page.getByRole("status").filter({ hasText: "Se for possível usar este e-mail" })).toBeVisible();
  await page.getByRole("button", { name: "Reenviar código", exact: true }).click();
  await expect.poll(() => pedidos).toBe(2);
  await page.getByLabel("Código de 6 dígitos", { exact: true }).fill("123456");
  await page.getByRole("button", { name: "Confirmar e-mail", exact: true }).click();
  await expect(page.getByRole("status").filter({ hasText: "E-mail confirmado." })).toBeVisible();
  await expect(aviso).not.toContainText("Complete seu cadastro");
});

test("senha fraca é explicada na criação e a troca de senha renova login com identificador", async ({ page }) => {
  const conta = { ...contaInicial };
  let login: unknown;
  await preparar(page, async (route, caminho) => {
    if (caminho === "/painel/usuarios" && route.request().method() === "POST") {
      await route.fulfill({ status: 400, json: { codigo: "senha_fraca" } }); return true;
    }
    if (caminho === "/painel/minha-conta/senha") {
      expect(route.request().postDataJSON()).toEqual({ senhaAtual: "SenhaAntiga1", novaSenha: "SenhaNova123" });
      conta.senhaDefinidaPorTerceiro = false; await route.fulfill({ status: 204 }); return true;
    }
    if (caminho === "/painel/auth/login") login = route.request().postDataJSON();
    return false;
  }, conta);
  await page.goto(`${PAINEL}/painel/usuarios`);
  await page.getByRole("button", { name: "Novo usuário", exact: true }).click();
  const modal = page.getByRole("dialog");
  await modal.getByLabel("Nome", { exact: true }).fill("Teste Pessoa");
  await modal.getByLabel("Senha", { exact: true }).fill("abcdefgh");
  await modal.getByRole("button", { name: "Criar", exact: true }).click();
  await expect(modal.getByText("Use uma senha com pelo menos 8 caracteres, incluindo letra e número.")).toBeVisible();
  await page.goto(`${PAINEL}/painel/minha-conta`);
  await page.getByLabel("Senha atual", { exact: true }).fill("SenhaAntiga1");
  await page.getByLabel("Nova senha", { exact: true }).fill("SenhaNova123");
  await page.getByRole("button", { name: "Trocar senha", exact: true }).click();
  await expect(page.getByRole("status").filter({ hasText: "Senha alterada." })).toBeVisible();
  expect(login).toEqual({ identificador: "pessoa.teste", senha: "SenhaNova123" });
  await expect(page.getByText("Sua senha foi definida pelo administrador.", { exact: false })).toHaveCount(0);
  await expect(page).toHaveURL(/\/painel\/minha-conta$/);
});

for (const largura of [375, 768, 1280]) test(`Minha conta acessível em ${largura}px, inclusive assinatura suspensa`, async ({ page }) => {
  await page.setViewportSize({ width: largura, height: 900 });
  await preparar(page, async (route, caminho) => {
    if (caminho === "/painel/assinatura/aviso") { await route.fulfill({ status: 403, json: { codigo: "assinatura_suspensa" } }); return true; }
    return false;
  });
  await page.goto(`${PAINEL}/painel/minha-conta`);
  await expect(page.getByRole("heading", { name: "Minha conta", exact: true })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Trocar senha", exact: true })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  if (largura < 1024) await page.getByRole("button", { name: "Abrir menu" }).click();
  const menu = page.locator("#navegacao-painel");
  await menu.getByRole("link", { name: "Minha conta", exact: true }).click();
  if (largura < 1024) await expect(page.getByRole("button", { name: "Abrir menu" })).toHaveAttribute("aria-expanded", "false");
});

test("Dar acesso aceita usuário sem e-mail, trata senha fraca e não oferece convite sem e-mail", async ({ page }) => {
  const profissional = { id: "profissional-teste", nome: "Zz Pessoa Teste", telefone: "(71) 98888-7777", email: null, acesso: null, ativo: true, fotoUrl: null, cpfMascarado: null, endereco: null, funcao: null, corAgenda: null };
  let enviado: unknown;
  await preparar(page, async (route, caminho) => {
    if (caminho === "/painel/profissionais/profissional-teste") { await route.fulfill({ json: profissional }); return true; }
    if (caminho === "/painel/profissionais/profissional-teste/acesso") {
      enviado = route.request().postDataJSON();
      await route.fulfill({ status: 400, json: { codigo: "senha_fraca" } }); return true;
    }
    return false;
  });
  await page.goto(`${PAINEL}/painel/profissionais/profissional-teste`);
  await page.getByRole("button", { name: "Dar acesso ao sistema", exact: true }).click();
  await expect(page.getByLabel("Nome de usuário (opcional)", { exact: true })).toHaveValue("zz.pessoa.teste");
  await expect(page.getByRole("radio", { name: /^Enviar convite por e-mail/ })).toBeDisabled();
  await page.getByRole("button", { name: "Usar o telefone do cadastro para entrar" }).click();
  await page.getByLabel("Senha de acesso", { exact: true }).fill("abcdefgh");
  await page.getByRole("button", { name: "Criar acesso", exact: true }).click();
  await expect(page.getByText("Use uma senha com pelo menos 8 caracteres, incluindo letra e número.")).toBeVisible();
  expect(enviado).toMatchObject({ email: null, nomeUsuario: "zz.pessoa.teste", telefoneLogin: "(71) 98888-7777", enviarConvite: false, senha: "abcdefgh" });
});

test("Acesso do usuário substitui identificadores, envia convite e confirma definição de senha", async ({ page }) => {
  const usuario = { id: "outro-usuario", nome: "Outra Pessoa", perfil: "Recepcionista", telefone: "(71) 98888-7777", email: null as string | null, emailVerificado: false, nomeUsuario: "outra.pessoa", telefoneLogin: null, ativo: true, fotoUrl: null, cpfMascarado: null, endereco: null, permissoes: [] };
  let enviado: unknown;
  let convites = 0;
  let senhas = 0;
  await preparar(page, async (route, caminho) => {
    if (caminho === "/painel/usuarios") { await route.fulfill({ json: [usuario] }); return true; }
    if (caminho === "/painel/usuarios/outro-usuario") { await route.fulfill({ json: usuario }); return true; }
    if (caminho === "/painel/usuarios/outro-usuario/acesso") {
      enviado = route.request().postDataJSON(); usuario.email = "outra@exemplo.com";
      await route.fulfill({ status: 204 }); return true;
    }
    if (caminho === "/painel/usuarios/outro-usuario/convite") { convites++; await route.fulfill({ status: 202 }); return true; }
    if (caminho === "/painel/usuarios/outro-usuario/senha") {
      expect(route.request().postDataJSON()).toEqual({ novaSenha: "NovaSenha123" });
      senhas++; await route.fulfill({ status: 204 }); return true;
    }
    return false;
  });
  await page.goto(`${PAINEL}/painel/usuarios`);
  await expect(page.getByRole("cell", { name: "@outra.pessoa", exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Acesso", exact: true }).click();
  const modal = page.getByRole("dialog", { name: "Acesso de Outra Pessoa", exact: true });
  await expect(modal.getByRole("button", { name: "Enviar convite", exact: true })).toBeDisabled();
  await modal.getByRole("button", { name: "Editar acesso", exact: true }).click();
  await modal.getByLabel("E-mail de acesso (opcional)", { exact: true }).fill("outra@exemplo.com");
  await modal.getByRole("button", { name: "Salvar acesso", exact: true }).click();
  await expect(modal.getByRole("status")).toContainText("Acesso atualizado");
  expect(enviado).toEqual({ email: "outra@exemplo.com", nomeUsuario: "outra.pessoa", telefoneLogin: null });
  await expect(modal.getByText("E-mail: não confirmado", { exact: true })).toBeVisible();
  await modal.getByRole("button", { name: "Enviar convite", exact: true }).click();
  await expect.poll(() => convites).toBe(1);
  await modal.getByRole("button", { name: "Definir nova senha", exact: true }).click();
  await modal.getByLabel("Nova senha do usuário", { exact: true }).fill("NovaSenha123");
  page.once("dialog", (dialogo) => dialogo.dismiss());
  await modal.getByRole("button", { name: "Salvar nova senha", exact: true }).click();
  expect(senhas).toBe(0);
  page.once("dialog", (dialogo) => dialogo.accept());
  await modal.getByRole("button", { name: "Salvar nova senha", exact: true }).click();
  await expect(modal.getByRole("status")).toContainText("Nova senha definida");
  expect(senhas).toBe(1);
});

test("nome de usuário exige senha atual, código inválido é neutro e aviso pode ser dispensado", async ({ page }) => {
  let pedido: unknown;
  await preparar(page, async (route, caminho) => {
    if (caminho === "/painel/minha-conta/nome-usuario") {
      pedido = route.request().postDataJSON();
      await route.fulfill({ status: 409, json: { codigo: "identificador_indisponivel", campo: "nomeUsuario", sugestao: "pessoa.teste2", detail: "Nome de usuário indisponível." } }); return true;
    }
    if (caminho === "/painel/minha-conta/email") { await route.fulfill({ status: 202 }); return true; }
    if (caminho === "/painel/minha-conta/email/confirmar") { await route.fulfill({ status: 400, json: { codigo: "codigo_invalido" } }); return true; }
    return false;
  });
  await page.goto(`${PAINEL}/painel/minha-conta`);
  await page.getByRole("button", { name: "Dispensar nesta sessão", exact: true }).click();
  await expect(page.getByRole("complementary", { name: "Cadastro da sua conta" })).toHaveCount(0);
  await page.getByRole("textbox", { name: "Nome de usuário", exact: true }).fill("pessoa.teste");
  await page.getByLabel("Senha atual para alterar usuário", { exact: true }).fill("SenhaAtual123");
  await page.getByRole("button", { name: "Salvar nome de usuário", exact: true }).click();
  await page.getByRole("button", { name: "Usar pessoa.teste2", exact: true }).click();
  await expect(page.getByRole("textbox", { name: "Nome de usuário", exact: true })).toHaveValue("pessoa.teste2");
  expect(pedido).toEqual({ nomeUsuario: "pessoa.teste", senhaAtual: "SenhaAtual123" });
  await page.getByLabel("E-mail", { exact: true }).fill("pessoa@exemplo.com");
  await page.getByLabel("Senha atual para confirmar e-mail", { exact: true }).fill("SenhaAtual123");
  await page.getByRole("button", { name: "Solicitar código", exact: true }).click();
  await page.getByLabel("Código de 6 dígitos", { exact: true }).fill("111111");
  await page.getByRole("button", { name: "Confirmar e-mail", exact: true }).click();
  await expect(page.getByRole("alert").filter({ hasText: "Código inválido ou vencido." })).toBeVisible();
  await expect(page.getByText(/e-mail já (existe|cadastrado)/i)).toHaveCount(0);
});

test("senha salva com falha no novo login orienta a entrar novamente e encerra a sessão antiga", async ({ page }) => {
  let encerrou = false;
  await preparar(page, async (route, caminho) => {
    if (caminho === "/painel/minha-conta/senha") { await route.fulfill({ status: 204 }); return true; }
    if (caminho === "/painel/auth/login" || (caminho === "/painel/auth/renovar" && encerrou)) {
      await route.fulfill({ status: 429 }); return true;
    }
    if (caminho === "/painel/auth/logout") { encerrou = true; await route.fulfill({ status: 204 }); return true; }
    return false;
  });
  await page.goto(`${PAINEL}/painel/minha-conta`);
  await page.getByLabel("Senha atual", { exact: true }).fill("SenhaAntiga1");
  await page.getByLabel("Nova senha", { exact: true }).fill("SenhaNova123");
  await page.getByRole("button", { name: "Trocar senha", exact: true }).click();
  await expect(page).toHaveURL(/\/painel\/login\?senhaAlterada=1$/);
  await expect(page.getByRole("status").filter({ hasText: "Senha alterada. Entre novamente com a nova senha." })).toBeVisible();
  expect(encerrou).toBe(true);
});
