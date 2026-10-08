// Service worker mínimo (Sprint 5, "PWA polido") — só o suficiente pra ficar instalável e
// não quebrar completamente offline. Estratégia network-first nos recursos públicos: esta
// é uma aplicação multi-tenant com autenticação e dados que mudam a toda hora (agenda,
// disponibilidade, financeiro) — cachear "primeiro" arriscaria mostrar dado velho ou vazar
// tela de outro negócio/sessão. O cache só entra como ÚLTIMO recurso, quando a rede falha.
const CACHE = "agendeiiai-shell-v1";
const RECURSOS_DO_SHELL = ["/", "/favicon.ico"];

// Documento (incluindo RSC/prefetch), detalhe e ICS de gestão por token.
// O navegador utiliza o proxy same-origin, nunca a API backend diretamente.
function ehGestaoPorToken(requisicao) {
  const url = new URL(requisicao.url);
  return url.origin === self.location.origin &&
    /^\/(?:agendamentos|api\/publico\/meus-agendamentos)\/[^/]+(?:\/|$)/.test(url.pathname);
}

self.addEventListener("install", (evento) => {
  evento.waitUntil(
    caches.open(CACHE).then((cache) => cache.addAll(RECURSOS_DO_SHELL)).catch(() => {}),
  );
  self.skipWaiting();
});

self.addEventListener("activate", (evento) => {
  evento.waitUntil(
    // Preserva assets e caches não relacionados; remove somente entradas sensíveis,
    // inclusive as que uma versão anterior gravou em outro cache desta origem.
    caches.keys().then((chaves) => Promise.all(chaves.map(async (chave) => {
      const cache = await caches.open(chave);
      const requisicoes = await cache.keys();
      await Promise.all(requisicoes.filter(ehGestaoPorToken).map(requisicao => cache.delete(requisicao)));
    }))).then(() => self.clients.claim()),
  );
});

self.addEventListener("fetch", (evento) => {
  if (evento.request.method !== "GET") return;

  if (ehGestaoPorToken(evento.request)) {
    // Sem CacheStorage, cache HTTP ou fallback: falha de rede chega à aplicação.
    evento.respondWith(fetch(evento.request, { cache: "no-store" }));
    return;
  }

  evento.respondWith(
    fetch(evento.request)
      .then((resposta) => {
        const copia = resposta.clone();
        caches.open(CACHE).then((cache) => cache.put(evento.request, copia)).catch(() => {});
        return resposta;
      })
      .catch(() => caches.match(evento.request).then((resposta) => resposta ?? caches.match("/"))),
  );
});
