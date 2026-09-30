import { ImageResponse } from "next/og";
import { headers } from "next/headers";
import { extrairSlugDoHost } from "@/lib/dominio";
import { buscarNegocioPorSlug } from "@/lib/api-servidor";

/**
 * Ícone do PWA de cada negócio (seção 5, "Dois PWAs instaláveis separados"), gerado na hora pelo host: o logo do negócio
 * sobre a cor de fundo dele ou, sem logo (ou com um logo que não dá para usar), as iniciais sobre a cor primária. Nunca a
 * marca do produto — o painel usa os arquivos fixos de `/brand`, e aqui só responde quem é página de negócio (senão 404).
 *
 * Tamanhos: `180` (apple-touch-icon), `192`, `512` e `maskable-192`/`maskable-512` (conteúdo dentro da área segura
 * central, com o fundo cobrindo tudo — o Android recorta em formas diferentes).
 */
export const dynamic = "force-dynamic";

const TAMANHOS: Record<string, { lado: number; mascaravel: boolean }> = {
  "180": { lado: 180, mascaravel: false },
  "192": { lado: 192, mascaravel: false },
  "512": { lado: 512, mascaravel: false },
  "maskable-192": { lado: 192, mascaravel: true },
  "maskable-512": { lado: 512, mascaravel: true },
};

const BYTES_MAXIMOS_LOGO = 2 * 1024 * 1024;

function iniciais(nome: string): string {
  return nome
    .split(" ")
    .filter(Boolean)
    .slice(0, 2)
    .map((parte) => parte[0]?.toUpperCase())
    .join("");
}

/**
 * O logo ainda é uma URL digitada pelo negócio, e quem a busca é o servidor: só https, nunca IP nem localhost (não vira
 * porta para a rede interna), com tempo e tamanho limitados, e só PNG/JPEG conferidos pelos bytes (o que o gerador de
 * imagem aceita). Qualquer falha cai nas iniciais.
 */
async function logoComoDataUrl(url: string | null): Promise<string | null> {
  if (!url) return null;
  let endereco: URL;
  try {
    endereco = new URL(url);
  } catch {
    return null;
  }
  const host = endereco.hostname.toLowerCase();
  if (endereco.protocol !== "https:" || host === "localhost" || host.endsWith(".localhost") || /^[\d.]+$/.test(host) || host.includes(":")) {
    return null;
  }

  try {
    const resposta = await fetch(endereco, { signal: AbortSignal.timeout(3000), redirect: "error", cache: "no-store" });
    if (!resposta.ok) return null;
    if (Number(resposta.headers.get("content-length") ?? 0) > BYTES_MAXIMOS_LOGO) return null;
    const bytes = new Uint8Array(await resposta.arrayBuffer());
    if (bytes.length === 0 || bytes.length > BYTES_MAXIMOS_LOGO) return null;

    const png = bytes[0] === 0x89 && bytes[1] === 0x50 && bytes[2] === 0x4e && bytes[3] === 0x47;
    const jpeg = bytes[0] === 0xff && bytes[1] === 0xd8 && bytes[2] === 0xff;
    if (!png && !jpeg) return null;

    return `data:${png ? "image/png" : "image/jpeg"};base64,${Buffer.from(bytes).toString("base64")}`;
  } catch {
    return null;
  }
}

export async function GET(_request: Request, { params }: { params: Promise<{ tamanho: string }> }) {
  const { tamanho } = await params;
  const formato = TAMANHOS[tamanho];
  if (!formato) return new Response(null, { status: 404 });

  const dominioBase = process.env.MARCA_DOMINIO ?? "";
  const listaCabecalhos = await headers();
  const slug = dominioBase ? extrairSlugDoHost(listaCabecalhos.get("host") ?? "", dominioBase) : null;
  const negocio = slug ? await buscarNegocioPorSlug(slug) : null;
  if (!negocio) return new Response(null, { status: 404 });

  const { lado, mascaravel } = formato;
  const logo = await logoComoDataUrl(negocio.logoUrl);
  const corFundo = logo ? (negocio.corFundo ?? "#ffffff") : (negocio.corPrimaria ?? negocio.corFundo ?? "#2563eb");
  // Maskable: o que importa fica no círculo central de 80%; o normal usa um respiro menor.
  const conteudo = Math.round(lado * (mascaravel ? 0.6 : logo ? 0.8 : 1));

  return new ImageResponse(
    (
      <div
        style={{
          width: "100%",
          height: "100%",
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
          background: corFundo,
          color: "#ffffff",
          fontSize: conteudo * 0.45,
          fontWeight: 700,
          fontFamily: "sans-serif",
        }}
      >
        {logo ? (
          // eslint-disable-next-line @next/next/no-img-element -- ImageResponse (Satori) só entende <img>.
          <img src={logo} width={conteudo} height={conteudo} style={{ objectFit: "contain" }} alt="" />
        ) : (
          iniciais(negocio.nomeExibido) || "?"
        )}
      </div>
    ),
    {
      width: lado,
      height: lado,
      // Sem isso o ImageResponse sai "public, immutable" por um ano — um CDN poderia servir o ícone de um negócio a outro host.
      headers: { "Cache-Control": "private, max-age=300", Vary: "Host" },
    },
  );
}
