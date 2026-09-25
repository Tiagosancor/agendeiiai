import { headers } from "next/headers";
import { extrairSlugDoHost } from "@/lib/dominio";
import { buscarNegocioPorSlug } from "@/lib/api-servidor";
import { BotaoTema } from "@/components/BotaoTema";
import { PaginaLegal } from "@/components/site/PaginaLegal";

/**
 * Política de privacidade (seção 8.4) — precisa estar acessível na página pública de cada
 * negócio. Texto genérico, honesto sobre o que ainda não existe (ex.: revisão jurídica
 * de verdade) — não é aconselhamento legal, é o mínimo estrutural pro MVP.
 *
 * No domínio raiz (sem negócio), mostra a política do PRODUTO (seção 6.4), para quem
 * assina — texto-base marcado como rascunho.
 */
export const dynamic = "force-dynamic";

export default async function PaginaPrivacidade() {
  const dominioBase = process.env.MARCA_DOMINIO ?? "";
  const listaCabecalhos = await headers();
  const slug = dominioBase ? extrairSlugDoHost(listaCabecalhos.get("host") ?? "", dominioBase) : null;
  const negocio = slug ? await buscarNegocioPorSlug(slug) : null;

  if (!negocio) return <PoliticaDoProduto />;

  const nome = negocio.nomeExibido;

  return (
    <main className="mx-auto max-w-2xl space-y-4 px-6 py-12 text-sm text-gray-700 dark:text-neutral-300">
      <BotaoTema className="fixed right-4 top-4" />
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

/** Política de privacidade do produto, para quem assina (seção 6.4) — rascunho até revisão. */
function PoliticaDoProduto() {
  const nome = process.env.MARCA_NOME_PRODUTO ?? "a plataforma";

  return (
    <PaginaLegal titulo="Política de Privacidade" nomeProduto={nome}>
      <p>
        Esta política explica como o {nome} trata os dados pessoais de quem cria uma conta para o seu negócio e de quem
        agenda pelos links dos negócios assinantes, conforme a Lei Geral de Proteção de Dados (LGPD).
      </p>

      <h2>Dados de quem assina</h2>
      <ul>
        <li>Nome, e-mail e telefone de quem cria a conta e dos usuários da equipe.</li>
        <li>Dados do negócio: nome, tipo, endereço da página, serviços, preços, horários e profissionais.</li>
        <li>CPF, quando informado no cadastro de usuários ou profissionais — guardado criptografado.</li>
        <li>Registros técnicos de acesso (data, hora e IP), para segurança.</li>
      </ul>
      <p>Usamos esses dados para criar e manter a conta, cobrar a assinatura, dar suporte e proteger o serviço contra abuso.</p>

      <h2>Dados dos clientes dos negócios</h2>
      <p>
        Quando alguém agenda pelo link de um negócio, informa nome, telefone e, se quiser, e-mail. Esses dados pertencem ao
        negócio (controlador); o {nome} os trata como operador, só para agendar, enviar o código de confirmação e os
        lembretes. Registramos também a data, o IP e a versão dos termos aceitos no agendamento. Pedidos de exportação ou
        exclusão devem ser feitos ao próprio negócio, que tem essas funções no painel.
      </p>

      <h2>Com quem compartilhamos</h2>
      <p>
        Só com os fornecedores necessários para o serviço funcionar: hospedagem e banco de dados, envio de e-mail e envio
        de mensagens por WhatsApp. [Lista nominal de fornecedores — a definir.] Não vendemos dados pessoais.
      </p>

      <h2>Segurança</h2>
      <p>
        Senhas são guardadas com hash (nunca em texto), CPFs são criptografados, códigos de confirmação são guardados só
        como hash e expiram em minutos, e os backups do banco são criptografados.
      </p>

      <h2>Por quanto tempo guardamos</h2>
      <p>Enquanto a conta existir e pelo prazo exigido por lei depois disso. [Prazos específicos — a definir.]</p>

      <h2>Seus direitos</h2>
      <p>
        Você pode pedir acesso, correção, exportação ou exclusão dos seus dados. [Canal e encarregado (DPO) — a definir.]
      </p>
    </PaginaLegal>
  );
}
