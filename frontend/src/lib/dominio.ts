// Extrai o slug do negócio a partir do host (seção 8.3.3) — usado pela página pública
// (Server Component) para buscar os dados do negócio antes de renderizar. É a mesma regra
// de `proxy.ts` (não compartilhada em código por serem contextos de execução diferentes —
// Edge Runtime vs Server Component —, mas devem ficar em sincronia se o formato do slug mudar).

const REGEX_SLUG = /^[a-z0-9]([a-z0-9-]*[a-z0-9])?$/;
const TAMANHO_MINIMO_SLUG = 3;
const TAMANHO_MAXIMO_SLUG = 30;

export function extrairSlugDoHost(host: string, dominioBase: string): string | null {
  const hostSemPorta = host.split(":")[0]?.toLowerCase() ?? "";
  const dominio = dominioBase.toLowerCase();
  const sufixo = `.${dominio}`;

  if (!hostSemPorta || hostSemPorta === dominio || hostSemPorta === `app.${dominio}` || !hostSemPorta.endsWith(sufixo)) {
    return null;
  }

  const candidato = hostSemPorta.slice(0, -sufixo.length);
  const formatoValido =
    candidato.length >= TAMANHO_MINIMO_SLUG && candidato.length <= TAMANHO_MAXIMO_SLUG && REGEX_SLUG.test(candidato);

  return formatoValido ? candidato : null;
}
