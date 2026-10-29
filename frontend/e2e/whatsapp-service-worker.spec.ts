import { test, expect, type Page } from "@playwright/test";
import { createServer, type Server } from "node:http";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

test.use({ serviceWorkers: "allow" });
const sw = readFileSync(resolve("public/sw.js"), "utf8");
let servidores: Server[];
let base: string;
let api: string;
let versao = "atual-a";
const caminhos = ["/painel/whatsapp", "/painel/whatsapp/conversas", "/painel/whatsapp/conversas/fixture", "/painel/whatsapp/conversas/fixture?antesDe=2026-10-28T12%3A00%3A00Z&antesDeId=m1", "/painel/whatsapp/conversas/resumo", "/painel/whatsapp/status", "/painel/whatsapp/uso?mes=2026-10"];
caminhos.push("/painel/clientes/fixture/exportar", "/painel/clientes", "/painel/financeiro/resumo", "/api/privado", "/plataforma", "/plataforma/negocios");

test.beforeAll(async () => {
  servidores = [0, 1].map(() => createServer((req, res) => {
    res.setHeader("Access-Control-Allow-Origin", "*");
    if (req.url === "/sw.js") {
      res.writeHead(200, { "Content-Type": "application/javascript", "Cache-Control": "no-store" });
      return res.end(sw);
    }
    res.writeHead(200, { "Content-Type": "text/html", "Cache-Control": "public, max-age=3600" });
    res.end(req.url && req.url !== "/" ? versao : "<!doctype html><title>Fixture SW</title><main>Página de teste</main>");
  }));
  const origens = await Promise.all(servidores.map(async servidor => {
    await new Promise<void>(r => servidor.listen(0, "127.0.0.1", r));
    const endereco = servidor.address();
    if (!endereco || typeof endereco === "string") throw new Error("Sem porta local");
    return `http://127.0.0.1:${endereco.port}`;
  }));
  [base, api] = origens;
});
test.afterAll(async () => { await Promise.all(servidores.map(s => new Promise<void>(r => s.close(() => r())))); });

async function registrar(page: Page) {
  await page.evaluate(async () => {
    await navigator.serviceWorker.register("/sw.js", { updateViaCache: "none" });
    await navigator.serviceWorker.ready;
    if (!navigator.serviceWorker.controller) await new Promise<void>(r => navigator.serviceWorker.addEventListener("controllerchange", () => r(), { once: true }));
  });
}
async function ler(page: Page, url: string) {
  // A API externa é ignorada pelo SW; o cliente autenticado impede cache HTTP.
  return page.evaluate(async url => { try { return await (await fetch(url, { cache: "no-store" })).text(); } catch { return "indisponível"; } }, url);
}

test("WhatsApp: dados privados e exportação em outra origem ficam fora do CacheStorage e indisponíveis offline", async ({ page, context }) => {
  versao = "atual-a";
  await page.goto(base);
  await registrar(page);
  const urls = caminhos.flatMap(caminho => [base + caminho, api + caminho]);
  for (const url of urls) {
    expect(await ler(page, url)).toBe("atual-a");
    expect(await page.evaluate(async url => !!await caches.match(url), url)).toBe(false);
  }
  versao = "atual-b";
  expect(await ler(page, `${api}/painel/whatsapp/status`)).toBe("atual-b");
  await page.evaluate(async urls => {
    const cache = await caches.open("fixture-antigo");
    for (const url of urls) await cache.put(url, new Response("fixture antiga"));
  }, urls);
  await context.setOffline(true);
  for (const url of urls) expect(await ler(page, url)).toBe("indisponível");
});

test("WhatsApp: ativação remove entradas sensíveis antigas e preserva outros assets", async ({ page }) => {
  await page.goto(base);
  const urls = caminhos.flatMap(c => [base + c, api + c]);
  await page.evaluate(async urls => {
    const cache = await caches.open("fixture-antigo");
    for (const url of urls) await cache.put(url, new Response("fixture antiga"));
    await cache.put("/public.css", new Response("body{}"));
  }, urls);
  await registrar(page);
  for (const url of urls) expect(await page.evaluate(async url => !!await caches.match(url), url)).toBe(false);
  expect(await page.evaluate(async () => !!await caches.match("/public.css"))).toBe(true);
});

test("SW cacheia somente shell e assets estáticos da própria origem", async ({ page }) => {
  await page.goto(base);
  await registrar(page);
  for (const caminho of ["/brand/fixture.svg", "/_next/static/fixture.js", "/favicon.ico"]) {
    await ler(page, base + caminho);
    await expect.poll(() => page.evaluate(async url => !!await caches.match(url), base + caminho)).toBe(true);
  }
  for (const url of [base + "/pagina-dinamica", api + "/brand/fixture.svg"]) {
    await ler(page, url);
    expect(await page.evaluate(async url => !!await caches.match(url), url)).toBe(false);
  }
});
