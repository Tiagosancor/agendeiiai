import type { CSSProperties } from "react";

/**
 * Fundo configurado pelo negócio (seção 5, "Cada negócio configura"): cor e, opcionalmente, imagem — o mesmo na página
 * (6.1) e no assistente (6.2). Sem nada configurado, a página fica como sempre foi (fundo do tema).
 */

const URL_BASE_API = process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:5080";

/** Arquivo guardado pela API (`/arquivos/{chave}`) vem relativo a ela; URL absoluta passa direto (armazenamento externo). */
export function urlArquivo(caminho: string | null | undefined): string | null {
  if (!caminho) return null;
  return caminho.startsWith("/") ? `${URL_BASE_API}${caminho}` : caminho;
}

export interface FundoNegocio {
  /** Falso = negócio sem cor nem imagem: nada muda no visual. */
  temFundo: boolean;
  estilo: CSSProperties;
  /** Texto posto direto sobre o fundo precisa ser claro (imagem, que ganha um véu escuro, ou cor escura). */
  textoClaro: boolean;
}

export function fundoDoNegocio(negocio: { corFundo: string | null; imagemFundoUrl: string | null }): FundoNegocio {
  const imagem = urlArquivo(negocio.imagemFundoUrl);
  const cor = negocio.corFundo;
  if (!imagem && !cor) return { temFundo: false, estilo: {}, textoClaro: false };

  return {
    temFundo: true,
    estilo: {
      // Sem imagem, só a cor; com imagem, a cor fica por baixo enquanto ela carrega. O véu escuro garante o contraste do
      // texto branco sobre qualquer foto. A URL vem da própria API (chave hexadecimal), então não quebra o `url("")`.
      backgroundColor: cor ?? "#1e2a38",
      ...(imagem
        ? {
            backgroundImage: `linear-gradient(rgba(0,0,0,0.45), rgba(0,0,0,0.45)), url("${imagem}")`,
            backgroundSize: "cover",
            backgroundPosition: "center",
          }
        : {}),
    },
    textoClaro: imagem !== null || corEscura(cor!),
  };
}

/** Luminância relativa (WCAG) abaixo de 0,179: daí para baixo o branco dá mais contraste que o preto. */
export function corEscura(hex: string): boolean {
  const valor = /^#([0-9a-f]{6})$/i.exec(hex)?.[1];
  if (!valor) return false;
  const [r, g, b] = [0, 2, 4].map((i) => {
    const canal = parseInt(valor.slice(i, i + 2), 16) / 255;
    return canal <= 0.03928 ? canal / 12.92 : ((canal + 0.055) / 1.055) ** 2.4;
  });
  return 0.2126 * r + 0.7152 * g + 0.0722 * b < 0.179;
}
