import { test, expect, type Page } from "@playwright/test";
import { createServer, request as requisicaoHttp, type Server } from "node:http";
import { readFileSync } from "node:fs";
import { execFileSync } from "node:child_process";
import { resolve } from "node:path";

// SW real em origem HTTP local isolada. APIs usam fixtures; somente HTML/bundles
// são encaminhados ao frontend Docker. Nenhuma ação chega ao backend.
const DETALHE = "/api/publico/meus-agendamentos/fixture-a";
const OUTRO = "/api/publico/meus-agendamentos/fixture-b";
const ICS = `${DETALHE}/ics`;
const SLOTS = `${DETALHE}/horarios-livres?data=2026-10-09`;
const DOCUMENTO = "/agendamentos/fixture-a";
const ASSET = "/fixture-public.css";
const PUBLICO = "/pagina-publica-fixture";
const CACHE = "agendeiiai-shell-v1";
const detalhe = { id: "reserva-local", nomeNegocio: "Acme", local: "Rua de teste, 10", inicio: "2026-10-05T12:00:00Z", fim: "2026-10-05T12:30:00Z", servicos: ["Corte Teste"], total: 45, status: "Agendado", fuso: "America/Sao_Paulo", acoes: { cancelar: { permitido: true }, remarcar: { permitido: true } } };
let servidor: Server;
let base: string;
let sw: string;
let atual = { ...detalhe };
let falharRefresh = false;
let posts: string[] = [];
let leituras = 0;
const novoSw = readFileSync(resolve("public/sw.js"), "utf8");
const antigoSw = execFileSync("git", ["-c", "safe.directory=C:/Projetos/Agendeiiai", "show", "8b7a4390cf254fdf8e87e1ffa1e540bb4a1564c1:frontend/public/sw.js"], { cwd: resolve(".."), encoding: "utf8" });

test.use({ serviceWorkers: "allow" });
test.beforeAll(async () => {
  servidor = createServer((pedido, resposta) => {
    const url = new URL(pedido.url!, "http://localhost");
    if (url.pathname === "/sw.js") {
      resposta.writeHead(200, { "Content-Type": "application/javascript", "Cache-Control": "no-store" });
      return resposta.end(sw);
    }
    if (["/", PUBLICO, "/favicon.ico", ASSET].includes(url.pathname)) {
      resposta.writeHead(200, { "Content-Type": url.pathname === ASSET ? "text/css" : "text/html" });
      return resposta.end(url.pathname === ASSET ? "body { color: #10243b; }" : "<!doctype html><title>Página pública local</title><main><h1>Página pública local</h1></main>");
    }
    if (url.pathname === "/api/publico/negocio") {
      resposta.setHeader("Content-Type", "application/json");
      return resposta.end(JSON.stringify({ fuso: "America/Sao_Paulo" }));
    }
    if (url.pathname.startsWith("/api/publico/meus-agendamentos/")) {
      if (url.pathname.endsWith("/horarios-livres")) {
        resposta.setHeader("Content-Type", "application/json");
        return resposta.end(JSON.stringify({ fuso: "America/Sao_Paulo", data: url.searchParams.get("data"), duracaoMinutos: 30, profissionalId: "profissional-local", horarios: [`${url.searchParams.get("data")}T12:00:00.000Z`] }));
      }
      if (pedido.method === "POST") {
        posts.push(url.pathname.endsWith("/cancelar") ? "cancelar" : "remarcar");
        let corpo = "";
        pedido.on("data", parte => { corpo += parte; });
        pedido.on("end", () => {
          if (url.pathname.endsWith("/cancelar")) atual = { ...atual, status: "Cancelado" };
          else atual = { ...atual, inicio: JSON.parse(corpo).novoInicio };
          resposta.writeHead(204);
          resposta.end();
        });
        return;
      }
      // Simule também uma resposta cacheável no cache HTTP: network-only deve
      // continuar buscando o servidor, além de ignorar CacheStorage.
      resposta.setHeader("Cache-Control", "public, max-age=3600");
      if (url.pathname.endsWith("/ics")) {
        resposta.setHeader("Content-Type", "text/calendar");
        return resposta.end("BEGIN:VCALENDAR\r\nVERSION:2.0\r\nEND:VCALENDAR\r\n");
      }
      leituras++;
      resposta.setHeader("Content-Type", "application/json");
      if (falharRefresh && posts.length) { resposta.writeHead(503); return resposta.end("{}"); }
      return resposta.end(JSON.stringify(url.pathname === OUTRO ? { ...atual, nomeNegocio: "Outro negócio demonstrativo" } : atual));
    }
    if (url.pathname.startsWith("/api/")) { resposta.writeHead(404); return resposta.end(); }
    if (pedido.method !== "GET") { resposta.writeHead(405); return resposta.end(); }
    const proxy = requisicaoHttp({ hostname: "127.0.0.1", port: 3000, path: pedido.url, method: "GET", headers: { ...pedido.headers, host: "acme.agendeiiai.localhost:3000" } }, origem => {
      resposta.writeHead(origem.statusCode!, origem.headers);
      origem.pipe(resposta);
    });
    proxy.on("error", () => { resposta.writeHead(502); resposta.end(); });
    proxy.end();
  });
  await new Promise<void>(resolve => servidor.listen(0, "127.0.0.1", resolve));
  const endereco = servidor.address();
  if (!endereco || typeof endereco === "string") throw new Error("Servidor local indisponível");
  base = `http://127.0.0.1:${endereco.port}`;
});
test.afterAll(async () => { await new Promise<void>(resolve => servidor.close(() => resolve())); });
test.beforeEach(() => { sw = novoSw; atual = { ...detalhe }; posts = []; leituras = 0; falharRefresh = false; });

async function registrar(page: Page) {
  await page.goto(base);
  await page.evaluate(async () => {
    await navigator.serviceWorker.register("/sw.js", { updateViaCache: "none" });
    await navigator.serviceWorker.ready;
    if (!navigator.serviceWorker.controller) await new Promise<void>(resolve => navigator.serviceWorker.addEventListener("controllerchange", () => resolve(), { once: true }));
  });
}
async function ler(page: Page, caminho: string, cabecalhos?: Record<string, string>) {
  return page.evaluate(async ({ caminho, cabecalhos }) => {
    try { const resposta = await fetch(caminho, { headers: cabecalhos }); return { status: resposta.status, texto: await resposta.text() }; }
    catch { return { status: null, texto: "indisponível" }; }
  }, { caminho, cabecalhos });
}
const cacheado = (page: Page, caminho: string) => page.evaluate(async caminho => !!await caches.match(caminho), caminho);

test("detalhe, ICS, HTML e RSC por token não são gravados em CacheStorage", async ({ page }) => {
  await registrar(page);
  for (const caminho of [DETALHE, ICS, SLOTS, DOCUMENTO, `${DOCUMENTO}?_rsc=fixture`]) {
    expect((await ler(page, caminho)).status).toBe(200);
    expect(await cacheado(page, caminho)).toBe(false);
  }
  const rsc = await page.evaluate(async caminho => {
    const resposta = await fetch(caminho, { headers: { RSC: "1" } });
    return { status: resposta.status, tipo: resposta.headers.get("Content-Type") };
  }, `${DOCUMENTO}?_rsc=fixture-real`);
  expect(rsc.status).toBe(200);
  expect(rsc.tipo).toContain("text/x-component");
  expect(await cacheado(page, `${DOCUMENTO}?_rsc=fixture-real`)).toBe(false);
});

test("offline não há detalhe nem ICS stale, mesmo com entradas antigas presentes", async ({ page, context }) => {
  await registrar(page);
  await page.evaluate(async ({ caminhos, cache }) => {
    const destino = await caches.open(cache);
    for (const caminho of caminhos) await destino.put(caminho, new Response("conteúdo antigo sensível", { status: 200 }));
  }, { caminhos: [DETALHE, ICS, SLOTS, DOCUMENTO, `${DOCUMENTO}?_rsc=fixture`], cache: CACHE });
  await context.setOffline(true);
  for (const caminho of [DETALHE, ICS, SLOTS, DOCUMENTO, `${DOCUMENTO}?_rsc=fixture`]) expect(await ler(page, caminho)).toEqual({ status: null, texto: "indisponível" });
  expect(await ler(page, `${DOCUMENTO}?_rsc=fixture`, { RSC: "1" })).toEqual({ status: null, texto: "indisponível" });
});

test("network-only ignora também o cache HTTP durante consultas online", async ({ page }) => {
  await registrar(page);
  expect((await ler(page, DETALHE)).texto).toContain('"status":"Agendado"');
  atual = { ...atual, status: "Cancelado" };
  expect((await ler(page, DETALHE)).texto).toContain('"status":"Cancelado"');
  expect(leituras).toBe(2);
});

test("token A online → offline → token B nunca apresenta conteúdo de A", async ({ page, context }) => {
  await registrar(page);
  expect((await ler(page, DETALHE)).texto).toContain("Acme");
  await context.setOffline(true);
  expect((await ler(page, OUTRO)).status).toBeNull();
  await page.goto(`${base}/agendamentos/fixture-b`).catch(() => {});
  await expect(page.getByRole("heading", { name: "Acme" })).toHaveCount(0);
  expect(await page.content()).not.toContain("Rua de teste, 10");
});

test("mesmo token atualizado não retorna versão antiga offline; online recupera a atual", async ({ page, context }) => {
  await registrar(page);
  expect((await ler(page, DETALHE)).texto).toContain('"status":"Agendado"');
  atual = { ...atual, status: "Cancelado" };
  await context.setOffline(true);
  expect((await ler(page, DETALHE)).status).toBeNull();
  await context.setOffline(false);
  expect((await ler(page, DETALHE)).texto).toContain('"status":"Cancelado"');
  expect(await cacheado(page, DETALHE)).toBe(false);
});

test("upgrade do SW real remove somente entradas sensíveis, preservando assets e outros caches", async ({ page }) => {
  sw = antigoSw;
  await registrar(page);
  for (const caminho of [DETALHE, ICS, DOCUMENTO, `${DOCUMENTO}?_rsc=fixture`, ASSET, PUBLICO]) {
    expect((await ler(page, caminho)).status).toBe(200);
    await expect.poll(() => cacheado(page, caminho)).toBe(true);
  }
  await page.evaluate(async ({ detalhe, asset }) => {
    const extra = await caches.open("fixture-cache-nao-relacionado");
    await extra.put(detalhe, new Response("antigo", { status: 200 }));
    await extra.put(asset, new Response("asset preservado", { status: 200 }));
  }, { detalhe: DETALHE, asset: "/asset-extra-fixture.css" });
  sw = novoSw;
  await page.evaluate(async () => {
    const registro = await navigator.serviceWorker.getRegistration();
    const mudou = new Promise<void>(resolve => navigator.serviceWorker.addEventListener("controllerchange", () => resolve(), { once: true }));
    await registro!.update();
    await mudou;
    const ativo = navigator.serviceWorker.controller!;
    if (ativo.state !== "activated") await new Promise<void>(resolve => ativo.addEventListener("statechange", () => {
      if (ativo.state === "activated") resolve();
    }));
  });
  for (const caminho of [DETALHE, ICS, DOCUMENTO, `${DOCUMENTO}?_rsc=fixture`]) expect(await cacheado(page, caminho)).toBe(false);
  for (const caminho of [ASSET, PUBLICO, "/asset-extra-fixture.css"]) expect(await cacheado(page, caminho)).toBe(true);
  expect(await page.evaluate(() => caches.keys())).toEqual(expect.arrayContaining([CACHE, "fixture-cache-nao-relacionado"]));
});

test("assets e página pública continuam com fallback offline", async ({ page, context }) => {
  await registrar(page);
  const asset = await ler(page, ASSET);
  const publico = await ler(page, PUBLICO);
  await expect.poll(() => cacheado(page, ASSET)).toBe(true);
  await expect.poll(() => cacheado(page, PUBLICO)).toBe(true);
  await context.setOffline(true);
  expect(await ler(page, ASSET)).toEqual(asset);
  expect(await ler(page, PUBLICO)).toEqual(publico);
});

test("gestão real online preserva cancelamento com SW ativo", async ({ page }) => {
  await registrar(page);
  await page.goto(`${base}${DOCUMENTO}`);
  await expect(page.getByRole("button", { name: "Remarcar agendamento" })).toBeVisible();
  expect(await page.evaluate(() => !!navigator.serviceWorker.controller)).toBe(true);
  page.on("dialog", dialog => dialog.accept());
  await page.getByRole("button", { name: "Cancelar agendamento" }).click();
  await expect(page.getByRole("status")).toHaveText("Agendamento cancelado.");
  await expect(page.getByText("Status: Cancelado")).toBeVisible();
  expect(posts).toEqual(["cancelar"]);
  expect(await cacheado(page, DETALHE)).toBe(false);
});

test("gestão real: remarcação, refresh falho, offline e retry online sem repetir POST", async ({ page, context }) => {
  await registrar(page);
  await page.goto(`${base}${DOCUMENTO}`);
  await expect(page.getByText(/segunda-feira, 5 de outubro.*09:00/)).toBeVisible();
  await page.screenshot({ path: test.info().outputPath("sw-online.png"), fullPage: true });
  await page.getByRole("button", { name: "Remarcar agendamento" }).click();
  await page.getByLabel("Data da remarcação").fill("2026-10-06");
  await page.getByRole("button", { name: "09:00", exact: true }).click();
  await page.getByRole("button", { name: "Revisar remarcação" }).click();
  falharRefresh = true;
  await page.getByRole("button", { name: "Confirmar novo horário", exact: true }).click();
  await expect(page.getByRole("status")).toContainText("A remarcação foi aceita");
  await context.setOffline(true);
  await page.getByRole("button", { name: "Atualizar detalhes", exact: true }).click();
  await expect(page.getByRole("status")).toContainText("não foi possível atualizar");
  await expect(page.getByText(/segunda-feira, 5 de outubro/)).toHaveCount(0);
  expect(await ler(page, DETALHE)).toEqual({ status: null, texto: "indisponível" });
  await page.screenshot({ path: test.info().outputPath("sw-offline.png"), fullPage: true });
  await context.setOffline(false);
  falharRefresh = false;
  await page.getByRole("button", { name: "Atualizar detalhes", exact: true }).click();
  await expect(page.getByRole("status")).toHaveText("Agendamento remarcado.");
  await expect(page.getByText(/terça-feira, 6 de outubro.*09:00/)).toBeVisible();
  await page.screenshot({ path: test.info().outputPath("sw-retorno-online.png"), fullPage: true });
  expect(posts).toEqual(["remarcar"]);
  expect(leituras).toBe(3);
  expect(await cacheado(page, DETALHE)).toBe(false);
});
