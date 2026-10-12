import { expect, test, type APIRequestContext } from "@playwright/test";

const PAINEL = "http://app.agendeiiai.localhost:3000";
const API = "http://localhost:5080";

async function autenticar(request: APIRequestContext) {
  const resposta = await request.post(`${API}/painel/auth/login`, {
    data: { email: "admin@acme.dev", senha: "Admin!123" },
  });
  expect(resposta.ok(), await resposta.text()).toBeTruthy();
  return { Authorization: `Bearer ${(await resposta.json()).accessToken}` };
}

async function entrar(page: import("@playwright/test").Page) {
  await page.goto(`${PAINEL}/painel/login`);
  await page.getByLabel("E-mail").fill("admin@acme.dev");
  await page.getByLabel("Senha").fill("Admin!123");
  await page.getByRole("button", { name: "Entrar", exact: true }).click();
  await expect(page).toHaveURL(/\/painel$/);
}

test("foto enviada e cor escolhida identificam o profissional na grade", async ({ page, request }) => {
  test.setTimeout(90_000);
  const auth = await autenticar(request);
  const nome = `Profissional visual ${Date.now()}`;
  const criado = await request.post(`${API}/painel/profissionais`, {
    headers: auth,
    data: { nome, corAgenda: "#8b5cf6" },
  });
  expect(criado.status(), await criado.text()).toBe(201);
  const id = await criado.json();

  try {
    const foto = await request.put(`${API}/painel/profissionais/${id}/foto`, {
      headers: auth,
      multipart: {
        arquivo: {
          name: "profissional.png",
          mimeType: "image/png",
          buffer: Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=", "base64"),
        },
      },
    });
    expect(foto.ok(), await foto.text()).toBeTruthy();

    await entrar(page);
    await page.goto(`${PAINEL}/painel/agenda`);
    const cabecalho = page.getByRole("columnheader", { name: nome });
    await expect(cabecalho).toBeVisible();
    await expect(cabecalho.locator("img")).toHaveAttribute("src", /\/arquivos\//);
    expect(await cabecalho.evaluate((elemento) => getComputedStyle(elemento).borderLeftColor)).toBe("rgb(139, 92, 246)");
  } finally {
    await request.delete(`${API}/painel/profissionais/${id}`, { headers: auth });
  }
});

test("sem cor escolhida a grade mantém uma cor automática estável", async ({ page, request }) => {
  const auth = await autenticar(request);
  const nome = `Profissional automático ${Date.now()}`;
  const criado = await request.post(`${API}/painel/profissionais`, { headers: auth, data: { nome } });
  expect(criado.status(), await criado.text()).toBe(201);
  const id = await criado.json();

  try {
    await entrar(page);
    await page.goto(`${PAINEL}/painel/agenda`);
    const cabecalho = page.getByRole("columnheader", { name: nome });
    const corInicial = await cabecalho.evaluate((elemento) => getComputedStyle(elemento).borderLeftColor);
    expect(corInicial).toMatch(/^rgb\(/);
    await page.reload();
    await expect(page.getByRole("columnheader", { name: nome })).toBeVisible();
    expect(await page.getByRole("columnheader", { name: nome }).evaluate((elemento) => getComputedStyle(elemento).borderLeftColor)).toBe(corInicial);
  } finally {
    await request.delete(`${API}/painel/profissionais/${id}`, { headers: auth });
  }
});

test("profissional envia e remove a própria foto sem permissão administrativa", async ({ page, request }) => {
  test.setTimeout(90_000);
  const auth = await autenticar(request);
  const sufixo = Date.now().toString().slice(-7);
  const email = `foto-${sufixo}@teste.dev`;
  const senha = `Senha!${sufixo}Aa`;
  const criado = await request.post(`${API}/painel/profissionais`, {
    headers: auth,
    data: { nome: `Profissional foto ${sufixo}` },
  });
  expect(criado.status(), await criado.text()).toBe(201);
  const id = await criado.json();

  try {
    const acesso = await request.post(`${API}/painel/profissionais/${id}/acesso`, {
      headers: auth,
      data: { email, senha, enviarConvite: false },
    });
    expect(acesso.status(), await acesso.text()).toBe(201);
    await page.goto(`${PAINEL}/painel/login`);
    await page.getByLabel("E-mail").fill(email);
    await page.getByLabel("Senha").fill(senha);
    await page.getByRole("button", { name: "Entrar", exact: true }).click();
    await expect(page).toHaveURL(/\/painel$/);
    await page.goto(`${PAINEL}/painel/agenda`);
    await page.getByText("Minha foto na agenda", { exact: true }).click();
    const envio = page.waitForResponse((resposta) => resposta.url().endsWith("/painel/minha-foto") && resposta.request().method() === "PUT");
    await page.getByLabel("Escolher foto do profissional").setInputFiles({
      name: "minha-foto.png",
      mimeType: "image/png",
      buffer: Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=", "base64"),
    });
    expect((await envio).status()).toBe(200);
    await expect(page.getByAltText("Prévia da foto do profissional")).toBeVisible();

    const remocao = page.waitForResponse((resposta) => resposta.url().endsWith("/painel/minha-foto") && resposta.request().method() === "DELETE");
    await page.getByRole("button", { name: "Remover foto", exact: true }).click();
    expect((await remocao).status()).toBe(204);
    await expect(page.getByText("Sem foto", { exact: true })).toBeVisible();
  } finally {
    await request.delete(`${API}/painel/profissionais/${id}`, { headers: auth });
  }
});

test("painel explica e impede salvar link de página como logo", async ({ page }) => {
  await entrar(page);
  await page.route("https://ibb.co/9mx5pCmJ", (rota) => rota.fulfill({ status: 200, contentType: "text/html", body: "<html>página, não imagem</html>" }));
  await page.goto(`${PAINEL}/painel/negocio`);
  await page.getByLabel("URL direta do logo", { exact: true }).fill("https://ibb.co/9mx5pCmJ");
  await expect(page.getByText("Este endereço não entrega uma imagem diretamente.")).toBeVisible();

  let enviou = false;
  page.on("request", (requisicao) => {
    if (requisicao.url().endsWith("/painel/negocio") && requisicao.method() === "PUT") enviou = true;
  });
  await page.getByRole("button", { name: "Salvar alterações", exact: true }).click();
  await expect(page.getByText("Informe o endereço direto do arquivo de imagem antes de salvar o logo.", { exact: true })).toBeVisible();
  expect(enviou).toBe(false);
});
