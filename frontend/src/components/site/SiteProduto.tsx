import Link from "next/link";
import { preload } from "react-dom";
import type { ReactNode } from "react";
import type { PlanoPublico } from "@/lib/tipos";
import { BotaoTema } from "@/components/BotaoTema";
import { PlanosSite } from "@/components/site/PlanosSite";

/**
 * Site do produto no domínio raiz (seção 6.4): uma página só, com um objetivo — levar ao
 * teste grátis. Server Component; só o alternador de planos (PlanosSite) e o botão de tema
 * rodam no navegador. As imagens são capturas reais do produto (negócio de demonstração),
 * em WebP em /public/site — nunca banco de imagens. Nada de depoimento, logo de cliente ou
 * número inventado (regra de conteúdo da seção 6.4).
 */

const CTA = "Testar grátis por 30 dias";
const IMAGEM_HERO = "/site/assistente-horarios.webp";

const ANCORAS = [
  { href: "#como-funciona", rotulo: "Como funciona" },
  { href: "#recursos", rotulo: "Recursos" },
  { href: "#planos", rotulo: "Planos" },
  { href: "#duvidas", rotulo: "Dúvidas" },
];

const classeCta =
  "inline-flex items-center justify-center rounded-xl bg-marca-acento px-5 py-3 text-sm font-semibold text-marca-primaria shadow-sm transition hover:brightness-110 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-marca-acento";

const classeTitulo = "font-display text-3xl font-bold tracking-tight text-gray-900 sm:text-4xl dark:text-neutral-50";
const classeTexto = "text-gray-700 dark:text-neutral-300";

function Icone({ children }: { children: ReactNode }) {
  return (
    <svg viewBox="0 0 24 24" width="24" height="24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      {children}
    </svg>
  );
}

/** Moldura de celular em volta de uma captura real do assistente (390×780 lógicos). */
function Celular({ src, alt, prioridade = false, className = "" }: { src: string; alt: string; prioridade?: boolean; className?: string }) {
  return (
    <div className={`rounded-[2.2rem] border-[7px] border-marca-primaria bg-marca-primaria shadow-2xl dark:border-neutral-700 dark:bg-neutral-700 ${className}`}>
      <img
        src={src}
        alt={alt}
        width={390}
        height={780}
        loading={prioridade ? "eager" : "lazy"}
        fetchPriority={prioridade ? "high" : "auto"}
        decoding={prioridade ? "sync" : "async"}
        className="h-auto w-full rounded-[1.7rem] bg-white"
      />
    </div>
  );
}

const PUBLICO = [
  {
    titulo: "Barbearias",
    frase: "Cada barbeiro com a própria agenda, e o cliente escolhe com quem quer cortar.",
    icone: (
      <Icone>
        <circle cx="6" cy="6" r="3" />
        <circle cx="6" cy="18" r="3" />
        <path d="M20 4 8.12 15.88M14.47 14.48 20 20M8.12 8.12 12 12" />
      </Icone>
    ),
  },
  {
    titulo: "Salões de beleza",
    frase: "Serviços de durações diferentes encaixados sem buraco na agenda.",
    icone: (
      <Icone>
        <path d="M12 3v3M12 18v3M3 12h3M18 12h3M5.6 5.6l2.1 2.1M16.3 16.3l2.1 2.1M5.6 18.4l2.1-2.1M16.3 7.7l2.1-2.1" />
        <circle cx="12" cy="12" r="3" />
      </Icone>
    ),
  },
  {
    titulo: "Clínicas de estética",
    frase: "Horários confirmados por código e lembrete antes do atendimento: menos cadeira vazia.",
    icone: (
      <Icone>
        <path d="M12 2.7S6 9.5 6 14a6 6 0 0 0 12 0c0-4.5-6-11.3-6-11.3Z" />
      </Icone>
    ),
  },
  {
    titulo: "Profissionais autônomos",
    frase: "Um link para mandar aos clientes e a agenda se organiza enquanto você trabalha.",
    icone: (
      <Icone>
        <circle cx="12" cy="8" r="4" />
        <path d="M4 21a8 8 0 0 1 16 0" />
      </Icone>
    ),
  },
];

const PASSOS = [
  { titulo: "Cadastre serviços e equipe", texto: "Preços, durações, quem faz o quê e o horário de cada profissional, com almoço e folgas." },
  { titulo: "Compartilhe seu link", texto: "Seu negócio ganha um endereço próprio. Coloque na bio, no WhatsApp e no Google." },
  { titulo: "Receba agendamentos confirmados", texto: "O cliente escolhe serviço e horário e confirma com um código. Cai direto na sua agenda." },
];

const OUTROS_RECURSOS = [
  { titulo: "Lembretes automáticos", texto: "O cliente é lembrado um dia antes e poucas horas antes do horário." },
  { titulo: "Financeiro do mês", texto: "Quanto entrou, por profissional e por serviço, sem planilha." },
  { titulo: "Programa de fidelidade", texto: "Selos por atendimento e recompensa quando o cliente completa o cartão." },
  { titulo: "Cupons de desconto", texto: "Crie códigos com validade e limite de uso para campanhas." },
  { titulo: "Permissões por usuário", texto: "Cada pessoa da equipe vê e mexe só no que você liberar." },
  { titulo: "Cancelar e remarcar pelo link", texto: "O próprio cliente reorganiza, respeitando a antecedência que você definir." },
];

const DUVIDAS = [
  {
    pergunta: "Como funciona o teste grátis?",
    resposta:
      "Você cria a conta, escolhe o plano pelo número de profissionais e usa tudo por 30 dias. Não pedimos cartão de crédito no cadastro.",
  },
  {
    pergunta: "O que acontece depois dos 30 dias?",
    resposta:
      "Avisamos antes do fim do teste. Para continuar, é só assinar pela tela Assinatura do painel. Se não assinar, há alguns dias de tolerância; depois disso o agendamento online fica pausado até o pagamento. Seus dados e a página do negócio continuam guardados.",
  },
  {
    pergunta: "Meu cliente precisa baixar algum aplicativo?",
    resposta:
      "Não. Ele abre o link do seu negócio no navegador do celular, escolhe serviço e horário e confirma com um código de 6 dígitos. Não precisa criar senha.",
  },
  {
    pergunta: "Dá para trocar de plano depois?",
    resposta:
      "Sim, a qualquer momento, pela tela Assinatura do painel, desde que o número de profissionais ativos caiba na nova faixa. Todos os planos têm os mesmos recursos; muda só o limite de profissionais.",
  },
  {
    pergunta: "Como é o pagamento hoje?",
    resposta:
      "Por enquanto o pagamento é feito por PIX: no painel aparecem a chave e o contato para enviar o comprovante, e a assinatura é liberada depois da confirmação. Não há cobrança automática no cartão.",
  },
];

export function SiteProduto({ nomeProduto, planos, whatsApp }: { nomeProduto: string; planos: PlanoPublico[]; whatsApp: string | null }) {
  // A captura do hero é o maior elemento da primeira tela (LCP no Lighthouse mobile):
  // <link rel=preload> no <head> para o download não esperar o HTML inteiro ser lido.
  preload(IMAGEM_HERO, { as: "image", fetchPriority: "high" });
  const linkWhatsApp = whatsApp ? `https://wa.me/${whatsApp}?text=${encodeURIComponent(`Olá! Quero saber mais sobre o ${nomeProduto}.`)}` : null;

  return (
    <div className="flex min-h-screen flex-col">
      <a href="#conteudo" className="sr-only focus:not-sr-only focus:fixed focus:left-4 focus:top-4 focus:z-50 focus:rounded-lg focus:bg-white focus:px-3 focus:py-2">
        Pular para o conteúdo
      </a>

      {/* 1. Cabeçalho fixo */}
      <header className="fixed inset-x-0 top-0 z-40 border-b border-gray-200/70 bg-(--background)/90 backdrop-blur dark:border-neutral-800/70">
        <div className="mx-auto flex h-16 max-w-6xl items-center justify-between gap-3 px-4">
          <Link href="/" className="flex min-w-0 items-center gap-2" aria-label={`${nomeProduto}, início`}>
            <img src="/brand/agendeiiai-icone-reduzido.svg" alt="" width={32} height={32} className="rounded-lg" />
            <span className="font-display text-lg font-bold text-gray-900 dark:text-neutral-50">{nomeProduto}</span>
          </Link>
          <nav aria-label="Seções" className="hidden items-center gap-6 text-sm text-gray-700 lg:flex dark:text-neutral-300">
            {ANCORAS.map((a) => (
              <a key={a.href} href={a.href} className="hover:text-gray-900 dark:hover:text-neutral-50">
                {a.rotulo}
              </a>
            ))}
          </nav>
          <div className="flex items-center gap-1 sm:gap-2">
            <BotaoTema />
            <Link href="/painel/login" className="rounded-lg px-2 py-2 text-sm font-medium text-gray-800 sm:px-3 hover:bg-gray-100 dark:text-neutral-100 dark:hover:bg-neutral-800">
              Entrar
            </Link>
            <Link href="/cadastro" className={`${classeCta} whitespace-nowrap px-3 py-2 sm:px-4`}>
              <span className="sm:hidden">Testar grátis</span>
              <span className="hidden sm:inline">{CTA}</span>
            </Link>
          </div>
        </div>
      </header>

      <main id="conteudo" className="flex-1 pt-16">
        {/* 2. Hero */}
        <section className="relative overflow-hidden">
          <div
            aria-hidden="true"
            className="absolute inset-x-0 top-0 -z-10 h-130 bg-[radial-gradient(60%_60%_at_75%_30%,rgba(217,142,59,0.18),transparent_70%)] dark:bg-[radial-gradient(60%_60%_at_75%_30%,rgba(217,142,59,0.12),transparent_70%)]"
          />
          <div className="mx-auto grid max-w-6xl items-center gap-12 px-4 py-14 md:grid-cols-[1.15fr_1fr] md:py-20">
            <div>
              <p className="text-sm font-semibold text-marca-madeira dark:text-marca-acento">Agendamento online para barbearias, salões e clínicas</p>
              <h1 className="mt-3 font-display text-4xl leading-tight font-bold tracking-tight text-gray-900 sm:text-5xl dark:text-neutral-50">
                Sua agenda cheia, sem precisar parar para atender o WhatsApp
              </h1>
              <p className={`mt-5 max-w-xl text-lg ${classeTexto}`}>
                Seus clientes marcam sozinhos pelo link do seu negócio e confirmam com um código. Você recebe o horário pronto na agenda e só se preocupa em atender.
              </p>
              <div className="mt-8 flex flex-col items-start gap-2">
                <Link href="/cadastro" className={`${classeCta} px-6 py-3.5 text-base`}>
                  {CTA}
                </Link>
                <p className="text-sm text-gray-600 dark:text-neutral-400">Sem cartão de crédito.</p>
              </div>
            </div>
            <div className="relative mx-auto w-full max-w-75 md:max-w-80">
              <Celular src={IMAGEM_HERO} alt="Assistente de agendamento no celular: escolha de data e horário" prioridade />
            </div>
          </div>
        </section>

        {/* 3. Para quem é */}
        <section aria-labelledby="titulo-publico" className="border-y border-gray-200 bg-white/60 dark:border-neutral-800 dark:bg-neutral-900/40">
          <div className="mx-auto max-w-6xl px-4 py-14">
            <h2 id="titulo-publico" className={classeTitulo}>
              Feito para quem vive de horário marcado
            </h2>
            <ul className="mt-10 grid gap-8 sm:grid-cols-2 lg:grid-cols-4">
              {PUBLICO.map((item) => (
                <li key={item.titulo}>
                  <span className="inline-flex h-11 w-11 items-center justify-center rounded-full bg-marca-primaria text-marca-acento dark:bg-neutral-800">
                    {item.icone}
                  </span>
                  <h3 className="mt-4 font-semibold text-gray-900 dark:text-neutral-50">{item.titulo}</h3>
                  <p className={`mt-1 text-sm ${classeTexto}`}>{item.frase}</p>
                </li>
              ))}
            </ul>
          </div>
        </section>

        {/* 4. Como funciona */}
        <section id="como-funciona" aria-labelledby="titulo-como-funciona" className="scroll-mt-20">
          <div className="mx-auto max-w-6xl px-4 py-16">
            <h2 id="titulo-como-funciona" className={classeTitulo}>
              Pronto para receber agendamentos em minutos
            </h2>
            <ol className="mt-10 grid gap-10 md:grid-cols-3">
              {PASSOS.map((passo, i) => (
                <li key={passo.titulo} className="relative border-l-2 border-marca-acento pl-5">
                  <span className="font-display text-5xl font-bold text-marca-primaria/15 dark:text-neutral-50/15" aria-hidden="true">
                    {i + 1}
                  </span>
                  <h3 className="mt-1 text-lg font-semibold text-gray-900 dark:text-neutral-50">{passo.titulo}</h3>
                  <p className={`mt-2 text-sm ${classeTexto}`}>{passo.texto}</p>
                </li>
              ))}
            </ol>
          </div>
        </section>

        {/* 5. Recursos */}
        <section id="recursos" aria-labelledby="titulo-recursos" className="scroll-mt-20 bg-marca-primaria text-white">
          <div className="mx-auto max-w-6xl px-4 py-16">
            <h2 id="titulo-recursos" className="font-display text-3xl font-bold tracking-tight sm:text-4xl">
              Tudo o que o dia a dia pede, num lugar só
            </h2>

            <div className="mt-12 grid items-center gap-10 md:grid-cols-[1.3fr_1fr]">
              <img
                src="/site/painel-agenda.webp"
                alt="Agenda do dia de um profissional no painel, com os atendimentos marcados"
                width={1100}
                height={560}
                loading="lazy"
                decoding="async"
                className="h-auto w-full rounded-xl shadow-2xl ring-1 ring-white/10"
              />
              <div>
                <h3 className="font-display text-2xl font-bold">Agenda por profissional</h3>
                <p className="mt-3 text-white/80">
                  Cada profissional com seus horários, intervalo de almoço, folgas e bloqueios. Encaixe manual quando precisar, e o sistema nunca deixa dois clientes no mesmo horário.
                </p>
              </div>
            </div>

            <div className="mt-16 grid items-center gap-10 md:grid-cols-2">
              <div className="flex items-start justify-center gap-4 md:order-2">
                <Celular src="/site/assistente-servicos.webp" alt="Escolha de serviços no link de agendamento" className="w-[46%] max-w-57.5" />
                <Celular src="/site/assistente-resumo.webp" alt="Resumo do agendamento confirmado por código" className="mt-10 w-[46%] max-w-57.5" />
              </div>
              <div className="space-y-8 md:order-1">
                <div>
                  <h3 className="font-display text-2xl font-bold">Link de agendamento sem app</h3>
                  <p className="mt-3 text-white/80">
                    Seu cliente abre o link, escolhe serviços, profissional e horário. Sem baixar nada e sem cadastro com senha.
                  </p>
                </div>
                <div>
                  <h3 className="font-display text-2xl font-bold">Confirmação por código</h3>
                  <p className="mt-3 text-white/80">
                    Cada agendamento é confirmado com um código de 6 dígitos enviado ao cliente. Menos faltas, e nenhum horário preso por um número de telefone errado.
                  </p>
                </div>
              </div>
            </div>

            <ul className="mt-16 grid gap-x-10 gap-y-8 border-t border-white/15 pt-12 sm:grid-cols-2 lg:grid-cols-3">
              {OUTROS_RECURSOS.map((r) => (
                <li key={r.titulo} className="flex gap-3">
                  <svg viewBox="0 0 20 20" width="20" height="20" aria-hidden="true" className="mt-0.5 shrink-0 text-marca-acento">
                    <path fill="currentColor" d="M8.2 13.6 4.6 10l-1.2 1.2 4.8 4.8 9-9-1.2-1.2z" />
                  </svg>
                  <div>
                    <h3 className="font-semibold">{r.titulo}</h3>
                    <p className="mt-1 text-sm text-white/75">{r.texto}</p>
                  </div>
                </li>
              ))}
            </ul>
          </div>
        </section>

        {/* 6. Planos */}
        <section id="planos" aria-labelledby="titulo-planos" className="scroll-mt-20">
          <div className="mx-auto max-w-5xl px-4 py-16">
            <div className="text-center">
              <h2 id="titulo-planos" className={classeTitulo}>
                Um preço pelo tamanho da sua equipe
              </h2>
              <p className={`mx-auto mt-3 max-w-xl ${classeTexto}`}>
                Todos os planos têm os mesmos recursos. O que muda é só quantos profissionais atendem pela agenda. 30 dias grátis em qualquer um.
              </p>
            </div>
            <div className="mt-10">
              <PlanosSite planos={planos} whatsApp={whatsApp} />
            </div>
          </div>
        </section>

        {/* 7. Dúvidas frequentes */}
        <section id="duvidas" aria-labelledby="titulo-duvidas" className="scroll-mt-20 border-t border-gray-200 dark:border-neutral-800">
          <div className="mx-auto max-w-3xl px-4 py-16">
            <h2 id="titulo-duvidas" className={classeTitulo}>
              Dúvidas frequentes
            </h2>
            <div className="mt-8 divide-y divide-gray-200 border-y border-gray-200 dark:divide-neutral-800 dark:border-neutral-800">
              {DUVIDAS.map((d) => (
                <details key={d.pergunta} className="group py-4">
                  <summary className="flex cursor-pointer list-none items-center justify-between gap-4 font-medium text-gray-900 dark:text-neutral-50 [&::-webkit-details-marker]:hidden">
                    {d.pergunta}
                    <span aria-hidden="true" className="text-xl text-marca-madeira transition group-open:rotate-45 dark:text-marca-acento">
                      +
                    </span>
                  </summary>
                  <p className={`mt-3 text-sm leading-relaxed ${classeTexto}`}>{d.resposta}</p>
                </details>
              ))}
            </div>
          </div>
        </section>

        {/* 8. Chamada final — o logotipo completo (seção 5.1) aparece aqui, grande. */}
        <section aria-labelledby="titulo-final" className="border-t border-gray-200 bg-white/60 dark:border-neutral-800 dark:bg-neutral-900/40">
          <div className="mx-auto flex max-w-3xl flex-col items-center px-4 py-16 text-center">
            {/* Variante escolhida pela `dark:`, que lê o mesmo data-theme do fundo (lib/tema.tsx). */}
            <img
              src="/brand/agendeiiai-logotipo-completo-texto-escuro-enquadrado.svg"
              alt={nomeProduto}
              width={440}
              height={440}
              loading="lazy"
              data-testid="logo-claro"
              className="h-auto w-full max-w-55 dark:hidden"
            />
            <img
              src="/brand/agendeiiai-logotipo-completo-enquadrado.svg"
              alt={nomeProduto}
              width={440}
              height={440}
              loading="lazy"
              data-testid="logo-escuro"
              className="hidden h-auto w-full max-w-55 dark:block"
            />
            <h2 id="titulo-final" className={`mt-6 ${classeTitulo}`}>
              Comece hoje e veja sua agenda se organizar
            </h2>
            <Link href="/cadastro" className={`${classeCta} mt-8 px-6 py-3.5 text-base`}>
              {CTA}
            </Link>
            <p className="mt-2 text-sm text-gray-600 dark:text-neutral-400">Sem cartão de crédito.</p>
          </div>
        </section>
      </main>

      <footer id="contato" className="border-t border-gray-200 dark:border-neutral-800">
        <div className="mx-auto flex max-w-6xl flex-col gap-4 px-4 py-8 text-sm text-gray-600 sm:flex-row sm:items-center sm:justify-between dark:text-neutral-400">
          <p>
            © {new Date().getFullYear()} {nomeProduto}
          </p>
          <nav aria-label="Rodapé" className="flex flex-wrap gap-x-6 gap-y-2">
            {linkWhatsApp && (
              <a href={linkWhatsApp} target="_blank" rel="noopener noreferrer" className="hover:text-gray-900 dark:hover:text-neutral-100">
                Contato pelo WhatsApp
              </a>
            )}
            <Link href="/termos" className="hover:text-gray-900 dark:hover:text-neutral-100">
              Termos de Uso
            </Link>
            <Link href="/privacidade" className="hover:text-gray-900 dark:hover:text-neutral-100">
              Política de Privacidade
            </Link>
            <Link href="/painel/login" className="hover:text-gray-900 dark:hover:text-neutral-100">
              Entrar no painel
            </Link>
          </nav>
        </div>
      </footer>
    </div>
  );
}
