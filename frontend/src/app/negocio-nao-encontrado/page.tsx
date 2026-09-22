import { notFound } from "next/navigation";

/**
 * Alvo do rewrite do middleware quando o slug do subdomínio não corresponde a
 * nenhum negócio ativo. Chama `notFound()` para que o Next.js responda com o
 * status 404 real e a UI de `not-found.tsx`.
 */
export default function NegocioNaoEncontradoPage() {
  notFound();
}
