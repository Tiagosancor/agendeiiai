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
 * assina — texto do dono, sem a faixa de rascunho.
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

/** Política de privacidade do produto, para quem assina (seção 6.4) — texto do dono, sem a faixa de rascunho. */
function PoliticaDoProduto() {
  const nome = process.env.MARCA_NOME_PRODUTO ?? "a plataforma";
  const dominio = process.env.MARCA_DOMINIO ?? "";
  const whatsApp = (process.env.CONTATO_WHATSAPP ?? "").replace(/\D/g, "");

  return (
    <PaginaLegal titulo="Política de Privacidade" nomeProduto={nome} rascunho={false}>
      <p>Última atualização: 19 de outubro de 2026</p>
      <p>
        Esta política explica como o {nome}
        {dominio ? ` (${dominio})` : ""} trata os dados pessoais de quem cria uma conta para o seu negócio, de quem agenda
        pelos links dos negócios assinantes e de quem conversa com o assistente virtual de WhatsApp de um negócio
        assinante, conforme a Lei Geral de Proteção de Dados (LGPD, Lei nº 13.709/2018).
      </p>

      <h2>Dados de quem assina</h2>
      <ul>
        <li>Nome, e-mail e telefone de quem cria a conta e dos usuários da equipe.</li>
        <li>Dados do negócio: nome, tipo, endereço da página, serviços, preços, horários e profissionais.</li>
        <li>CPF, quando informado no cadastro de usuários ou profissionais, guardado criptografado.</li>
        <li>
          Dados da conexão com o WhatsApp do negócio, quando o recurso é ativado: número conectado, estado da conexão e
          configurações do assistente virtual.
        </li>
        <li>
          Volume de uso do assistente virtual (quantidade de conversas e de processamento de inteligência artificial),
          para controle de limites e cobrança.
        </li>
        <li>Registros técnicos de acesso (data, hora e IP), para segurança.</li>
      </ul>
      <p>Usamos esses dados para criar e manter a conta, cobrar a assinatura, dar suporte e proteger o serviço contra abuso.</p>

      <h2>Dados dos clientes dos negócios</h2>
      <p>
        Quando alguém agenda pelo link de um negócio, informa nome, telefone e, se quiser, e-mail. Esses dados pertencem ao
        negócio (controlador); o {nome} os trata como operador, só para agendar, enviar o código de confirmação e os
        lembretes. Registramos também a data, o IP e a versão dos termos aceitos no agendamento.
      </p>
      <p>Pedidos de exportação ou exclusão devem ser feitos ao próprio negócio, que tem essas funções no painel.</p>

      <h2>Assistente virtual de WhatsApp</h2>
      <p>
        Os negócios assinantes podem ativar um assistente virtual que responde, pelo WhatsApp do próprio negócio, a clientes
        que enviam mensagem. Ele informa serviços, profissionais e horários disponíveis e pode criar, remarcar ou cancelar
        agendamentos a pedido do cliente.
      </p>

      <p>
        <strong>Quais dados são tratados</strong>
      </p>
      <ul>
        <li>Número de telefone e nome de perfil do WhatsApp do cliente.</li>
        <li>Conteúdo das mensagens trocadas com o assistente, com data e hora.</li>
        <li>
          Dados de agendamento associados ao telefone: nome informado, serviço, profissional, data e horário, histórico de
          agendamentos e preferências de atendimento.
        </li>
      </ul>

      <p>
        <strong>Para que usamos</strong>
      </p>
      <ul>
        <li>Responder às mensagens e atender ao pedido do cliente.</li>
        <li>Consultar a agenda e registrar, alterar ou cancelar agendamentos.</li>
        <li>
          Reconhecer o cliente em conversas futuras, com base nos dados de agendamento, para que ele não precise repetir
          informações.
        </li>
        <li>Encaminhar a conversa para atendimento humano, quando necessário.</li>
        <li>Garantir a segurança do serviço, prevenir abusos e controlar limites de uso.</li>
      </ul>

      <p>
        <strong>Papéis no tratamento.</strong> Quanto aos dados dos clientes do negócio, o negócio é o controlador, e o{" "}
        {nome} atua como operador, tratando os dados conforme as instruções do negócio. O assistente só responde a
        mensagens iniciadas pelo cliente.
      </p>

      <p>
        <strong>Inteligência artificial e decisões automatizadas.</strong> As respostas do assistente são geradas por um
        modelo de inteligência artificial. Para isso, trechos da conversa e os dados de agenda necessários para atender o
        pedido são enviados ao Google (API Gemini), que atua como suboperador. Os dados enviados ao Google não são usados
        para treinar os modelos do provedor. O {nome} também não usa o conteúdo das conversas para treinar modelos
        próprios.
      </p>
      <p>
        O assistente só realiza as ações previstas no sistema (consultar horários e gerenciar agendamentos do próprio
        cliente), e as regras de agenda são validadas pelo sistema, não pela inteligência artificial.
      </p>

      <p>
        <strong>Atendimento humano e revisão.</strong> O cliente pode, a qualquer momento, pedir para falar com uma pessoa do
        negócio. Quem entender que uma decisão tomada de forma automatizada afetou seus interesses pode solicitar a revisão
        dela ao negócio ou pelo canal indicado na seção &quot;Seus direitos&quot;.
      </p>

      <p>
        <strong>Cuidado com informações sensíveis.</strong> Pedimos que os clientes não enviem pelo assistente dados de
        saúde, documentos ou outras informações sensíveis, além do necessário para o agendamento. Negócios da área da saúde
        devem orientar seus clientes nesse sentido.
      </p>

      <p>
        <strong>Aviso ao cliente.</strong> O assistente se identifica como atendimento automatizado no início da conversa e
        informa onde esta política pode ser consultada.
      </p>

      <h2>Bases legais</h2>
      <p>Cada finalidade de tratamento se apoia em uma base legal da LGPD (art. 7º):</p>
      <div className="overflow-x-auto">
        <table>
          <thead>
            <tr>
              <th>Finalidade</th>
              <th>Base legal</th>
            </tr>
          </thead>
          <tbody>
            <tr>
              <td>Criar e manter a conta do assinante e dar suporte</td>
              <td>Execução de contrato</td>
            </tr>
            <tr>
              <td>Cobrar a assinatura</td>
              <td>Execução de contrato e cumprimento de obrigação legal ou regulatória</td>
            </tr>
            <tr>
              <td>Responder ao cliente e agendar, remarcar ou cancelar pelo WhatsApp</td>
              <td>Execução de contrato ou de procedimentos preliminares, a pedido do titular</td>
            </tr>
            <tr>
              <td>Enviar código de confirmação e lembretes do agendamento</td>
              <td>Execução de contrato</td>
            </tr>
            <tr>
              <td>Reconhecer o cliente em conversas futuras (histórico de agendamentos e preferências)</td>
              <td>Legítimo interesse</td>
            </tr>
            <tr>
              <td>Registros técnicos de acesso, segurança e prevenção a abusos</td>
              <td>Legítimo interesse</td>
            </tr>
            <tr>
              <td>Guardar registros pelo prazo exigido por lei</td>
              <td>Cumprimento de obrigação legal ou regulatória</td>
            </tr>
            <tr>
              <td>Envio de promoções ou divulgação, quando houver</td>
              <td>Consentimento, que pode ser revogado a qualquer momento</td>
            </tr>
          </tbody>
        </table>
      </div>
      <p>
        Nos dados dos clientes dos negócios, a escolha da base legal cabe ao negócio, como controlador, e o {nome} trata os
        dados conforme as instruções dele.
      </p>

      <h2>Suboperadores</h2>
      <p>Para o serviço funcionar, contamos com os seguintes suboperadores, que tratam dados pessoais em nosso nome:</p>
      <div className="overflow-x-auto">
        <table>
          <thead>
            <tr>
              <th>Suboperador</th>
              <th>Para que é usado</th>
            </tr>
          </thead>
          <tbody>
            <tr>
              <td>Google (API Gemini)</td>
              <td>
                Processa o conteúdo das conversas e os dados de agenda necessários para gerar as respostas do assistente
                virtual
              </td>
            </tr>
            <tr>
              <td>Railway</td>
              <td>Hospedagem da aplicação e dos serviços de integração com o WhatsApp</td>
            </tr>
            <tr>
              <td>Neon</td>
              <td>Banco de dados</td>
            </tr>
            <tr>
              <td>Resend</td>
              <td>Envio de e-mails (por exemplo, confirmações e avisos)</td>
            </tr>
            <tr>
              <td>Meta (WhatsApp)</td>
              <td>Envio e recebimento de mensagens pelo WhatsApp</td>
            </tr>
          </tbody>
        </table>
      </div>
      <p>Não vendemos dados pessoais.</p>
      <p>
        <strong>Transferência internacional.</strong> Esses suboperadores podem processar os dados fora do Brasil, incluindo
        os Estados Unidos. Nesses casos, a transferência se apoia nas garantias previstas no art. 33 da LGPD, como cláusulas
        contratuais adequadas.
      </p>

      <h2>Segurança</h2>
      <p>
        Senhas são guardadas com hash (nunca em texto), CPFs são criptografados, códigos de confirmação são guardados só
        como hash e expiram em minutos, e os backups do banco são criptografados.
      </p>
      <p>
        No assistente de WhatsApp, a comunicação entre os sistemas é protegida por chaves e segredos de acesso, os dados de
        cada negócio são isolados dos demais, o conteúdo das mensagens não é gravado em registros técnicos (logs) e o
        acesso às conversas é restrito ao negócio responsável e à equipe autorizada.
      </p>

      <h2>Por quanto tempo guardamos</h2>
      <p>
        Os dados da conta são guardados enquanto ela existir e por até 30 dias após o encerramento, quando são excluídos ou
        anonimizados. Ficam de fora os dados que a lei exige manter por mais tempo:
      </p>
      <ul>
        <li>
          <strong>Registros de acesso (data, hora e IP):</strong> 6 meses, conforme o art. 15 do Marco Civil da Internet
          (Lei nº 12.965/2014).
        </li>
        <li>
          <strong>Dados de cobrança e documentos fiscais:</strong> 5 anos, conforme a legislação tributária.
        </li>
      </ul>
      <p>
        Os dados de agendamento registrados no sistema seguem o prazo de guarda da conta do negócio. Os dados temporários de
        controle (como o registro usado para evitar mensagens duplicadas) são apagados automaticamente em poucos minutos.
      </p>

      <h2>Seus direitos</h2>
      <p>
        Você pode pedir confirmação do tratamento, acesso, correção, anonimização, portabilidade, exportação ou exclusão dos
        seus dados, informação sobre com quem eles são compartilhados, revisão de decisões automatizadas e a revogação do
        consentimento, quando essa for a base legal. Para dados de clientes de um negócio, o pedido deve ser feito ao próprio
        negócio; nós ajudamos o negócio a atendê-lo.
      </p>
      <p>
        <strong>Canal de atendimento e encarregado (DPO):</strong> {nome}, pelo WhatsApp{" "}
        {whatsApp ? (
          <a href={`https://wa.me/${whatsApp}`} rel="noopener noreferrer">
            +{whatsApp}
          </a>
        ) : (
          "indicado no site"
        )}
        .
      </p>
      <p>Você também pode apresentar reclamação à Autoridade Nacional de Proteção de Dados (ANPD).</p>

      <h2>Alterações nesta política</h2>
      <p>
        Podemos atualizar esta política para refletir mudanças no serviço ou na lei. A data da última atualização aparece no
        topo, e mudanças relevantes serão informadas aos assinantes.
      </p>
    </PaginaLegal>
  );
}
