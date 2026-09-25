import type { Metadata } from "next";
import { AssistenteCadastro } from "@/components/cadastro/AssistenteCadastro";

export const metadata: Metadata = { title: "Criar conta" };

// MARCA_DOMINIO e TURNSTILE_CHAVE_SITE são de runtime (nunca existem no build da imagem).
export const dynamic = "force-dynamic";

export default async function PaginaCadastro({
  searchParams,
}: {
  searchParams: Promise<Record<string, string | string[] | undefined>>;
}) {
  const parametros = await searchParams;
  const plano = typeof parametros.plano === "string" ? parametros.plano : null;
  const periodicidade = parametros.periodicidade === "Anual" ? "Anual" : "Mensal";

  return (
    <AssistenteCadastro
      dominio={process.env.MARCA_DOMINIO ?? ""}
      planoInicialId={plano}
      periodicidadeInicial={periodicidade}
      chaveSiteCaptcha={process.env.TURNSTILE_CHAVE_SITE || null}
    />
  );
}
