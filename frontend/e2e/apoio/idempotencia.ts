import { expect, type Page } from "@playwright/test";

export function capturarIdempotencia(page: Page) {
  const chamadas: { caminho: string; chave: string }[] = [];
  page.on("request", (requisicao) => {
    const chave = requisicao.headers()["idempotency-key"];
    if (chave) chamadas.push({ caminho: new URL(requisicao.url()).pathname, chave });
  });
  return {
    conferir(caminho: RegExp) {
      const chamada = chamadas.find((item) => caminho.test(item.caminho));
      expect(chamada, `Idempotency-Key ausente em ${caminho}`).toBeDefined();
      expect(chamada!.chave).toMatch(/^[0-9a-f-]{36}$/i);
      return chamada!.chave;
    },
  };
}
