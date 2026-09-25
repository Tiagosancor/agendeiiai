import type { Metadata } from "next";
import { PaginaLegal } from "@/components/site/PaginaLegal";

// MARCA_NOME_PRODUTO é de runtime (nunca existe no build da imagem).
export const dynamic = "force-dynamic";

export const metadata: Metadata = { title: "Termos de Uso" };

/** Termos de Uso do produto (seção 6.4) — texto-base, marcado como rascunho. */
export default function PaginaTermos() {
  const nome = process.env.MARCA_NOME_PRODUTO ?? "a plataforma";

  return (
    <PaginaLegal titulo="Termos de Uso" nomeProduto={nome}>
      <p>
        Estes termos regulam o uso do {nome}, um serviço online de agendamento e gestão para negócios que atendem com
        horário marcado. Ao criar uma conta, você (o &quot;assinante&quot;) concorda com eles.
      </p>

      <h2>1. Quem oferece o serviço</h2>
      <p>[Razão social, CNPJ e endereço do responsável pelo {nome} — a definir.]</p>

      <h2>2. Conta e acesso</h2>
      <ul>
        <li>O cadastro exige nome, e-mail confirmado, telefone e senha. Você responde pela guarda da senha e por tudo o que for feito com a sua conta.</li>
        <li>Você pode criar usuários para a sua equipe e definir o que cada um pode ver e fazer. As ações desses usuários também são de sua responsabilidade.</li>
        <li>As informações do negócio (nome, endereço da página, serviços, preços, horários) precisam ser verdadeiras.</li>
      </ul>

      <h2>3. Teste grátis, planos e pagamento</h2>
      <ul>
        <li>Toda conta nova tem 30 dias de teste grátis, sem cartão de crédito.</li>
        <li>Os planos diferem só pelo número máximo de profissionais ativos. Preços e faixas vigentes estão no site; o preço da sua assinatura fica mantido enquanto ela estiver em dia.</li>
        <li>Hoje o pagamento é feito por PIX e confirmado manualmente. A assinatura pode ser mensal ou anual.</li>
        <li>Sem pagamento após o vencimento, há um período de tolerância. Depois dele, a conta é suspensa: o agendamento online e o painel ficam bloqueados (exceto a tela de assinatura) até a regularização. Os dados não são apagados pela suspensão.</li>
      </ul>

      <h2>4. Dados dos seus clientes</h2>
      <p>
        Os dados que seus clientes informam ao agendar (nome, telefone, e-mail) pertencem ao seu negócio. Em relação a
        eles, você é o controlador e o {nome} atua como operador, tratando esses dados só para prestar o serviço, conforme
        a Política de Privacidade. Você deve atender os pedidos de acesso, exportação e exclusão dos seus clientes — o
        painel oferece essas funções.
      </p>

      <h2>5. Uso aceitável</h2>
      <p>
        É proibido usar o serviço para enviar mensagens não solicitadas, se passar por outra pessoa ou negócio, violar
        direitos de terceiros ou tentar burlar os limites e a segurança do sistema.
      </p>

      <h2>6. Disponibilidade e responsabilidade</h2>
      <p>
        Trabalhamos para manter o serviço no ar e com backups regulares, mas não garantimos funcionamento ininterrupto.
        O envio de lembretes e códigos depende de provedores de e-mail e WhatsApp de terceiros. [Limites de
        responsabilidade — a definir na revisão jurídica.]
      </p>

      <h2>7. Cancelamento</h2>
      <p>
        Você pode deixar de usar o serviço a qualquer momento, e pode pedir a exportação dos dados antes disso. [Prazo de
        guarda dos dados depois do cancelamento — a definir.]
      </p>

      <h2>8. Alterações e foro</h2>
      <p>
        Mudanças nestes termos serão avisadas com antecedência pelo painel ou por e-mail. [Foro — a definir.]
      </p>
    </PaginaLegal>
  );
}
