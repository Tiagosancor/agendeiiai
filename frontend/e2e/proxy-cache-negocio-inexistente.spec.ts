import { expect, test, type APIRequestContext } from "@playwright/test";

const DOMINIO = "agendeiiai.localhost:3000";

async function requisitarHost(request: APIRequestContext, host: string, caminho = "/") {
  return request.get(`http://localhost:3000${caminho}`, {
    headers: { Host: host },
    maxRedirects: 0,
  });
}

function esperar404SemCachePublico(resposta: Awaited<ReturnType<typeof requisitarHost>>) {
  expect(resposta.status()).toBe(404);
  const cacheControl = resposta.headers()["cache-control"] ?? "";
  expect(cacheControl).toContain("no-store");
  expect(cacheControl).not.toMatch(/(?:s-maxage|max-age)\s*=\s*(?:31536000|[1-9]\d{5,})/i);
}

test("slug válido inexistente devolve 404 humano sem cache prolongado", async ({ request }) => {
  const resposta = await requisitarHost(
    request,
    `slug-inexistente-d81.${DOMINIO}`,
    "/?origem=d81",
  );

  esperar404SemCachePublico(resposta);
  expect(await resposta.text()).toContain("Página não encontrada");
});

test("slug inválido recebe a mesma proteção de cache", async ({ request }) => {
  const resposta = await requisitarHost(request, `--invalido.${DOMINIO}`, "/privacidade");

  esperar404SemCachePublico(resposta);
  expect(await resposta.text()).toContain("Página não encontrada");
});

test("resposta inexistente não é reutilizada entre hosts e os fluxos válidos permanecem", async ({ request }) => {
  const inexistente = await requisitarHost(request, `outro-inexistente-d81.${DOMINIO}`);
  esperar404SemCachePublico(inexistente);

  const raiz = await requisitarHost(request, DOMINIO);
  expect(raiz.status()).toBe(200);
  expect(await raiz.text()).toContain("Sua agenda cheia");

  const painel = await requisitarHost(request, `app.${DOMINIO}`, "/painel/login");
  expect(painel.status()).toBe(200);
  expect(await painel.text()).toContain("Entrar no painel");

  const negocio = await requisitarHost(request, `acme.${DOMINIO}`);
  expect(negocio.status()).toBe(200);
  expect(await negocio.text()).toContain("Acme Barbearia");
});

test("www preserva caminho e query ao redirecionar para a raiz", async ({ request }) => {
  const resposta = await requisitarHost(request, `www.${DOMINIO}`, "/termos?origem=d81");

  expect(resposta.status()).toBe(308);
  expect(resposta.headers().location).toBe("http://agendeiiai.localhost:3000/termos?origem=d81");
});
