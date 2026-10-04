import Link from "next/link";
import "./site-produto.css";
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

const classeCta = "site-cta";
const classeTitulo = "site-titulo";
const classeTexto = "site-texto";

function Logo({ nomeProduto, final = false }: { nomeProduto: string; final?: boolean }) {
  return <span className="site-logo">
    {final ? <>
      <img src="/brand/calendario-confirmado.svg" width={46} height={46} alt="" data-testid="logo-claro" className="dark:hidden" />
      <img src="/brand/calendario-confirmado.svg" width={46} height={46} alt="" data-testid="logo-escuro" className="hidden dark:block" />
    </> : <img src="/brand/calendario-confirmado.svg" width={46} height={46} alt="" />}
    <span><strong>{nomeProduto}</strong><small>Agendou, tá confirmado!</small></span>
  </span>;
}

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
    <div className={`site-celular ${className}`}>
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
        <path d="M12 3C6 8 7 14 12 19c5-5 6-11 0-16Z" />
        <path d="M8 8 3 6c-1 7 3 12 9 13M16 8l5-2c1 7-3 12-9 13M5 14l-3 1c3 6 8 7 10 4 2 3 7 2 10-4l-3-1" />
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
  { titulo: "Lembretes automáticos", texto: "O cliente recebe um lembrete antes do horário do atendimento." },
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
    <div className="site-produto">
      <a href="#conteudo" className="site-pular">Pular para o conteúdo</a>
      <header className="site-header">
        <div className="site-container site-navbar">
          <Link href="/" aria-label={`${nomeProduto}, início`}><Logo nomeProduto={nomeProduto} /></Link>
          <nav aria-label="Seções" className="site-nav">
            {ANCORAS.map(a => <a key={a.href} href={a.href}>{a.rotulo}</a>)}
          </nav>
          <div className="site-acoes">
            <BotaoTema className="site-tema" />
            <Link href="/painel/login" className="site-entrar">Entrar</Link>
            <Link href="/cadastro" className={classeCta}><span className="sm:hidden">Testar grátis</span><span className="hidden sm:inline">{CTA}</span><span aria-hidden="true"> →</span></Link>
          </div>
        </div>
      </header>
      <main id="conteudo">
        <section className="site-hero">
          <div className="site-container site-hero-grid">
            <div className="site-hero-texto">
              <p className="site-badge">Agendamento online para barbearias, salões e clínicas</p>
              <h1>Sua agenda cheia,<br /><span className="site-gradiente">sem precisar parar<br className="site-quebra" /> para atender o WhatsApp.</span></h1>
              <p className={classeTexto}>Seus clientes marcam sozinhos pelo link do seu negócio e confirmam com um código. Você recebe o horário pronto na agenda e só se preocupa em atender.</p>
              <div className="site-hero-ctas">
                <Link href="/cadastro" className={classeCta}>{CTA}<span aria-hidden="true">→</span></Link>
                <Link href="/painel/login" className="site-cta-secundario">Já tenho uma conta</Link>
              </div>
              <ul className="site-micro"><li><span aria-hidden="true">✓</span> Sem cartão de crédito.</li><li><span aria-hidden="true">◷</span> Setup em minutos</li><li><span aria-hidden="true">✓</span> Cancele quando quiser</li></ul>
            </div>
            <div className="site-hero-celulares">
              <Celular src={IMAGEM_HERO} alt="Assistente de agendamento no celular: escolha de data e horário" prioridade className="site-celular-frente" />
              <Celular src="/site/assistente-servicos.webp" alt="Escolha os serviços no assistente de agendamento real" className="site-celular-atras" />
            </div>
          </div>
        </section>
        <section aria-labelledby="titulo-publico" className="site-secao">
          <div className="site-container">
            <h2 id="titulo-publico" className={classeTitulo}>Feito para quem vive de <span className="site-gradiente">horário marcado</span></h2>
            <p className="site-subtitulo">Ideal para quem valoriza o tempo do cliente e a organização do dia a dia.</p>
            <ul className="site-segmentos">
              {PUBLICO.map(item => <li key={item.titulo} className="site-card"><span className="site-icone">{item.icone}</span><h3>{item.titulo}</h3><p>{item.frase}</p></li>)}
            </ul>
          </div>
        </section>
        <section id="como-funciona" aria-labelledby="titulo-como-funciona" className="site-secao">
          <div className="site-container">
            <h2 id="titulo-como-funciona" className={classeTitulo}>Pronto para receber agendamentos <span className="site-gradiente">em minutos</span></h2>
            <p className="site-subtitulo">Receba agendamentos em minutos, com um processo simples.</p>
            <ol className="site-passos">
              {PASSOS.map((passo, i) => <li key={passo.titulo}><div className="site-passo-topo"><span className="site-numero" aria-hidden="true">{i + 1}</span><span className="site-icone"><Icone>{i === 0 ? <><circle cx="8" cy="7" r="3" /><path d="M2 21v-3a6 6 0 0 1 12 0v3M16 5h6M19 2v6M17 13h5M17 17h5" /></> : i === 1 ? <><path d="m10 14 4-4M8 16l-2 2a4 4 0 0 1-6-6l5-5a4 4 0 0 1 6 0M16 8l2-2a4 4 0 0 1 6 6l-5 5a4 4 0 0 1-6 0" /></> : <><rect x="3" y="5" width="18" height="16" rx="3" /><path d="M7 2v6M17 2v6M3 10h18m-13 5 3 3 5-5" /></>}</Icone></span></div><h3>{passo.titulo}</h3><p>{passo.texto}</p>{i < 2 && <span className="site-seta" aria-hidden="true">→</span>}</li>)}
            </ol>
          </div>
        </section>
        <section id="recursos" aria-labelledby="titulo-recursos" className="site-secao">
          <div className="site-container">
            <h2 id="titulo-recursos" className={classeTitulo}>Tudo o que o dia a dia pede, <span className="site-gradiente">num lugar só</span></h2>
            <div className="site-demonstracao">
              <img src="/site/painel-agenda.webp" alt="Agenda do dia de um profissional no painel, com os atendimentos marcados" width={1100} height={560} loading="lazy" decoding="async" className="site-painel" />
              <div className="site-recurso-texto"><span className="site-icone"><Icone><rect x="3" y="5" width="18" height="16" rx="2" /><path d="M7 2v6M17 2v6M3 10h18M8 14h3M8 17h7" /></Icone></span><div><h3>Agenda por profissional</h3><p>Cada profissional com seus horários, intervalo de almoço, folgas e bloqueios. Encaixe manual quando precisar, e o sistema nunca deixa dois clientes no mesmo horário.</p></div></div>
            </div>
            <div className="site-demonstracao site-demonstracao-mobile">
              <div className="site-recurso-lista">
                <div className="site-recurso-texto"><span className="site-icone"><Icone><path d="m10 14 4-4M8 16l-2 2a4 4 0 0 1-6-6l5-5a4 4 0 0 1 6 0M16 8l2-2a4 4 0 0 1 6 6l-5 5a4 4 0 0 1-6 0" /></Icone></span><div><h3>Link de agendamento sem app</h3><p>Seu cliente abre o link, escolhe serviços, profissional e horário. Sem baixar nada e sem cadastro com senha.</p></div></div>
                <div className="site-recurso-texto"><span className="site-icone"><Icone><path d="m12 2 9 4v6c0 5-5 8-9 10-4-2-9-5-9-10V6l9-4Z" /><path d="m8 12 3 3 5-6" /></Icone></span><div><h3>Confirmação por código</h3><p>Cada agendamento é confirmado com um código de 6 dígitos enviado ao cliente. Menos faltas, e nenhum horário preso por um número de telefone errado.</p></div></div>
              </div>
              <div className="site-telas-mobile"><Celular src="/site/assistente-servicos.webp" alt="Escolha de serviços no link de agendamento" /><Celular src="/site/assistente-resumo.webp" alt="Resumo do agendamento confirmado por código" /></div>
            </div>
            <div className="site-beneficios">
              <h2 className={classeTitulo}>Mais benefícios <span className="site-gradiente">para o seu negócio</span></h2>
              <ul className="site-beneficios-grid">{OUTROS_RECURSOS.map((r, i) => <li key={r.titulo} className="site-card"><span className="site-icone"><Icone>{[
                <path key="lembrete" d="M18 8a6 6 0 0 0-12 0c0 7-3 7-3 9h18c0-2-3-2-3-9M9 21h6" />,
                <path key="financeiro" d="M3 21h18M5 17v-5M11 17V8M17 17V4M3 9l6-5 5 2 7-5" />,
                <g key="fidelidade"><rect x="3" y="8" width="18" height="13" rx="2" /><path d="M12 8v13M3 12h18M12 8H7a3 3 0 1 1 3-3l2 3Zm0 0h5a3 3 0 1 0-3-3l-2 3Z" /></g>,
                <g key="cupom"><path d="M3 3h9l9 9-9 9-9-9V3Z" /><circle cx="8" cy="8" r="1" /></g>,
                <g key="permissoes"><circle cx="9" cy="7" r="3" /><path d="M2 21v-3a7 7 0 0 1 14 0v3M17 3a4 4 0 0 1 0 8M19 15a6 6 0 0 1 3 6" /></g>,
                <path key="remarcar" d="M20 7a9 9 0 0 0-15-2L2 8m0-5v5h5M4 17a9 9 0 0 0 15 2l3-3m0 5v-5h-5" />
              ][i]}</Icone></span><div><h3>{r.titulo}</h3><p>{r.texto}</p></div></li>)}</ul>
            </div>
          </div>
        </section>
        <section id="planos" aria-labelledby="titulo-planos" className="site-secao">
          <div className="site-container"><h2 id="titulo-planos" className={classeTitulo}>Um preço pelo tamanho <span className="site-gradiente">da sua equipe</span></h2><p className="site-subtitulo">Todos os planos têm os mesmos recursos. O que muda é só quantos profissionais atendem pela agenda. 30 dias grátis em qualquer um.</p><PlanosSite planos={planos} whatsApp={whatsApp} /></div>
        </section>
        <section id="duvidas" aria-labelledby="titulo-duvidas" className="site-secao">
          <div className="site-container"><h2 id="titulo-duvidas" className={`${classeTitulo} site-titulo-faq`}>Dúvidas frequentes</h2><div className="site-faq">{DUVIDAS.map(d => <details key={d.pergunta}><summary>{d.pergunta}<span aria-hidden="true">+</span></summary><p>{d.resposta}</p></details>)}</div></div>
        </section>
        <section aria-labelledby="titulo-final" className="site-final"><div className="site-container site-final-grid"><Logo nomeProduto={nomeProduto} final /><div><h2 id="titulo-final">Comece hoje e veja sua agenda se organizar.</h2><p>Teste grátis por 30 dias. Sem cartão de crédito.</p></div><Link href="/cadastro" className={classeCta}>{CTA}<span aria-hidden="true">→</span></Link></div></section>
      </main>
      <footer id="contato"><div className="site-container site-rodape"><nav aria-label="Rodapé">{ANCORAS.map(a => <a key={a.href} href={a.href}>{a.rotulo}</a>)}{linkWhatsApp && <a href={linkWhatsApp} target="_blank" rel="noopener noreferrer">Contato pelo WhatsApp</a>}<Link href="/termos">Termos de Uso</Link><Link href="/privacidade">Política de Privacidade</Link><Link href="/painel/login">Entrar no painel</Link></nav><p>© {new Date().getFullYear()} {nomeProduto}. Todos os direitos reservados.</p></div></footer>
    </div>
  );
}
