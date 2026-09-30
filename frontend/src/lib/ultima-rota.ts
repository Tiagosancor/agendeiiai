/**
 * Última tela do painel aberta neste aparelho — o PWA do painel abre em `/painel/login` (seção 5) e, com a sessão ainda
 * válida, segue direto para onde a pessoa estava. Só conveniência: sem localStorage (aba privada, bloqueado), cai no início.
 */
const CHAVE = "painel:ultima-rota";

export function guardarUltimaRota(caminho: string) {
  if (!caminho.startsWith("/painel") || caminho.startsWith("/painel/login")) return;
  try {
    localStorage.setItem(CHAVE, caminho);
  } catch {
    // Armazenamento indisponível: não é essencial.
  }
}

export function lerUltimaRota(): string {
  try {
    const caminho = localStorage.getItem(CHAVE);
    // Só caminho interno do painel (nunca "//outro-site" nem URL absoluta).
    if (caminho && /^\/painel(\/[\w\-/]*)?$/.test(caminho) && !caminho.startsWith("/painel/login")) return caminho;
  } catch {
    // Idem.
  }
  return "/painel";
}
