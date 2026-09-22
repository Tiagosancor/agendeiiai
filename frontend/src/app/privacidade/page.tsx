import { headers } from "next/headers";
import { extrairSlugDoHost } from "@/lib/dominio";
import { buscarNegocioPorSlug } from "@/lib/api-servidor";

/**
 * Política de privacidade (seção 8.4) — precisa estar acessível na página pública de cada
 * negócio. Texto genérico, honesto sobre o que ainda não existe (ex.: revisão jurídica
 * de verdade) — não é aconselhamento legal, é o mínimo estrutural pro MVP.
 */
export const dynamic = "force-dynamic";

export default async function PaginaPrivacidade() {
  const dominioBase = process.env.MARCA_DOMINIO ?? "";
  const listaCabecalhos = await headers();
  const slug = dominioBase ? extrairSlugDoHost(listaCabecalhos.get("host") ?? "", dominioBase) : null;
  const negocio = slug ? await buscarNegocioPorSlug(slug) : null;

  const nome = negocio?.nomeExibido ?? "este negócio";

  return (
    <main className="mx-auto max-w-2xl space-y-4 px-6 py-12 text-sm text-gray-700 dark:text-neutral-300">
      <h1 className="text-xl font-semibold text-gray-900 dark:text-neutral-50">Política de privacidade</h1>
      <p>
        Esta página descreve como {nome} trata os dados pessoais coletados no agendamento online, em
        conformidade com a Lei Geral de Proteção de Dados (LGPD).
      </p>

      <h2 className="text-base font-semibold text-gray-900 dark:text-neutral-50">O que coletamos</h2>
      <p>
        Nome, telefone e, opcionalmente, e-mail — informados por você ao agendar. O telefone é usado
        como identificador do seu cadastro. Guardamos também o histórico dos agendamentos feitos.
      </p>

      <h2 className="text-base font-semibold text-gray-900 dark:text-neutral-50">Código de confirmação</h2>
      <p>
        Para confirmar que o telefone informado é seu, enviamos um código de 6 dígitos por WhatsApp e
        e-mail. O código nunca é armazenado em texto puro, só um hash — e expira em poucos minutos.
      </p>

      <h2 className="text-base font-semibold text-gray-900 dark:text-neutral-50">Seus direitos</h2>
      <p>
        Você pode pedir a exportação ou a exclusão dos seus dados a qualquer momento, entrando em
        contato com {nome}. A exclusão anonimiza seu cadastro (nome, telefone e e-mail deixam de
        identificar você) — o histórico de atendimentos já realizados é mantido de forma anônima, para
        fins contábeis e de auditoria.
      </p>

      <h2 className="text-base font-semibold text-gray-900 dark:text-neutral-50">Contato</h2>
      <p>Para exercer esses direitos, entre em contato diretamente com {nome} pelos canais informados na página inicial.</p>
    </main>
  );
}
