// Service worker mínimo (Sprint 5, "PWA polido") — só o suficiente pra ficar instalável e
// não quebrar completamente offline. Estratégia network-first em tudo, de propósito: esta
// é uma aplicação multi-tenant com autenticação e dados que mudam a toda hora (agenda,
// disponibilidade, financeiro) — cachear "primeiro" arriscaria mostrar dado velho ou vazar
// tela de outro negócio/sessão. O cache só entra como ÚLTIMO recurso, quando a rede falha.
const CACHE = "agendeiiai-shell-v1";
const RECURSOS_DO_SHELL = ["/", "/favicon.ico"];

self.addEventListener("install", (evento) => {
  evento.waitUntil(
    caches.open(CACHE).then((cache) => cache.addAll(RECURSOS_DO_SHELL)).catch(() => {}),
  );
  self.skipWaiting();
});

self.addEventListener("activate", (evento) => {
  evento.waitUntil(
    caches.keys().then((chaves) => Promise.all(chaves.filter((chave) => chave !== CACHE).map((chave) => caches.delete(chave)))),
  );
  self.clients.claim();
});

self.addEventListener("fetch", (evento) => {
  if (evento.request.method !== "GET") return;

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
