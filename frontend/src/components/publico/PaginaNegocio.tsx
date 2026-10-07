"use client";

import { useEffect, useId, useState } from "react";
import Image from "next/image";
import { requisicaoApiPublica } from "@/lib/api";
import type { CategoriaComServicosPublicos, NegocioPublico, ProfissionalPublico } from "@/lib/tipos";
import { NOMES_DIAS_SEMANA } from "@/lib/tipos";
import { AssistenteAgendamento } from "@/components/publico/AssistenteAgendamento";
import { BotaoTema } from "@/components/BotaoTema";
import { formatarReais } from "@/lib/formatacao";
import "./experiencia-publica.css";
import { identidadePublica } from "./identidade-publica";
import { fundoDoNegocio } from "@/lib/fundo";

function iniciaisNome(nome: string): string {
  return nome
    .split(" ")
    .filter(Boolean)
    .slice(0, 2)
    .map((parte) => parte[0]?.toUpperCase())
    .join("");
}

function enderecoResumido(n: NegocioPublico): string {
  return [n.bairro, n.cidade].filter(Boolean).join(", ");
}

function enderecoCompleto(n: NegocioPublico): string {
  return [n.rua && n.numero ? `${n.rua}, ${n.numero}` : n.rua, n.bairro, n.cidade, n.cep].filter(Boolean).join(" — ");
}

export function PaginaNegocio({ negocio }: { negocio: NegocioPublico }) {
  const [categorias, setCategorias] = useState<CategoriaComServicosPublicos[] | null>(null);
  const [profissionais, setProfissionais] = useState<ProfissionalPublico[] | null>(null);
  const [estadoCatalogo, setEstadoCatalogo] = useState<"carregando" | "sucesso" | "erro">("carregando");
  const [tentativaCatalogo, setTentativaCatalogo] = useState(0);
  const [categoriasAbertas, setCategoriasAbertas] = useState<Set<string>>(new Set());
  const [assistenteAberto, setAssistenteAberto] = useState(false);
  const [servicoInicialId, setServicoInicialId] = useState<string | null>(null);

  useEffect(() => {
    let atual = true;
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setEstadoCatalogo("carregando");
    Promise.all([
      requisicaoApiPublica<CategoriaComServicosPublicos[]>("/servicos"),
      requisicaoApiPublica<ProfissionalPublico[]>("/profissionais"),
    ]).then(([listaCategorias, listaProfissionais]) => {
      if (!atual) return;
      setCategorias(listaCategorias);
      setProfissionais(listaProfissionais);
      setCategoriasAbertas(new Set(listaCategorias.slice(0, 1).map((c) => c.categoriaId)));
      setEstadoCatalogo("sucesso");
    }).catch(() => {
      if (atual) setEstadoCatalogo("erro");
    });
    return () => { atual = false; };
  }, [tentativaCatalogo]);

  const corPrimaria = negocio.corPrimaria ?? "#2563eb";
  const corSecundaria = negocio.corSecundaria ?? "#1d4ed8";
  // Cor/imagem de fundo (seção 5): o topo fica sobre o fundo; o resto vai num painel do tema, para o texto seguir legível.
  const fundo = fundoDoNegocio(negocio);
  const classeTituloTopo = fundo.temFundo ? (fundo.textoClaro ? "text-white" : "text-gray-900") : "text-gray-900 dark:text-neutral-50";
  const classeTextoTopo = fundo.temFundo ? (fundo.textoClaro ? "text-white/90" : "text-gray-700") : "text-gray-600 dark:text-neutral-400";

  function abrirAssistente(servicoId?: string) {
    if (estadoCatalogo !== "sucesso") return;
    setServicoInicialId(servicoId ?? null);
    setAssistenteAberto(true);
  }

  function alternarCategoria(id: string) {
    setCategoriasAbertas((atual) => {
      const proxima = new Set(atual);
      if (proxima.has(id)) proxima.delete(id);
      else proxima.add(id);
      return proxima;
    });
  }

  // Vitrine (seção 6.1): só o que o Administrador marcou para a página; o assistente recebe o catálogo inteiro.
  const vitrine = (categorias ?? [])
    .map((c) => ({ ...c, servicos: c.servicos.filter((s) => s.exibirNaPaginaInicial) }))
    .filter((c) => c.servicos.length > 0);
  const populares = vitrine.flatMap((c) => c.servicos.filter((s) => s.popular));
  // Falso com a assinatura suspensa (seção 7): a página continua no ar, só sem agendar online.
  const aceitaAgendamento = negocio.aceitaAgendamentoOnline;

  return (
    <div
      data-testid="pagina-negocio"
      className="pagina-publica"
      data-fundo-personalizado={fundo.temFundo}
      style={{ ...identidadePublica(corPrimaria, corSecundaria), ...fundo.estilo }}
    >
      <header className="pagina-cabecalho sticky top-0 z-30 flex items-center justify-between border-b border-gray-100 bg-white/90 px-4 py-3 backdrop-blur dark:border-neutral-800 dark:bg-neutral-950/90">
        <div className="pagina-marca flex items-center gap-2">
          {negocio.logoUrl && <img src={negocio.logoUrl} alt="" className="h-8 w-8 rounded-full object-cover" />}
          <span className="font-semibold text-gray-900 dark:text-neutral-50">{negocio.nomeExibido}</span>
        </div>
        <div className="flex items-center gap-2">
          <BotaoTema />
          {aceitaAgendamento && (
            <button
              disabled={estadoCatalogo !== "sucesso"}
              onClick={() => abrirAssistente()}
              className="publico-cta rounded-lg bg-(--cor-primaria) px-4 py-2 text-sm font-medium text-white transition hover:opacity-90"
            >
              Agendar
            </button>
          )}
        </div>
      </header>

      <main>
        <section className="pagina-hero">
          <div className="pagina-hero-marca">
            {negocio.logoUrl ? <Image src={negocio.logoUrl} alt="" width={58} height={58} unoptimized className="pagina-hero-avatar" /> : <span className="pagina-hero-avatar" aria-hidden="true">{iniciaisNome(negocio.nomeExibido)}</span>}
            <div>
              <p className="pagina-hero-nome">{negocio.nomeExibido}</p>
              {enderecoResumido(negocio) && <p className="pagina-hero-local">{enderecoResumido(negocio)}</p>}
            </div>
          </div>
          <h1 className={`mx-auto max-w-md text-2xl font-bold ${classeTituloTopo}`}>
            {negocio.tituloPagina ?? `Bem-vindo à ${negocio.nomeExibido}`}
          </h1>
          {negocio.subtituloPagina && (
            <p className={`mx-auto mt-2 max-w-sm text-sm ${classeTextoTopo}`}>{negocio.subtituloPagina}</p>
          )}
          {aceitaAgendamento ? (
            <button
              disabled={estadoCatalogo !== "sucesso"}
              onClick={() => abrirAssistente()}
              className="publico-cta mt-6 rounded-lg bg-(--cor-primaria) px-6 py-3 text-sm font-semibold text-white transition hover:opacity-90"
            >
              Agendar Agora
            </button>
          ) : (
            <p data-testid="agendamento-por-telefone" className={`mx-auto mt-6 max-w-sm text-sm ${classeTextoTopo}`}>
              No momento, os agendamentos são feitos por telefone
              {negocio.telefone ? (
                <>
                  :{" "}
                  <a href={`tel:${negocio.telefone}`} className="font-semibold text-(--cor-primaria) underline">
                    {negocio.telefone}
                  </a>
                </>
              ) : (
                "."
              )}
            </p>
          )}
          {negocio.textoSobre && <p>{negocio.textoSobre}</p>}
        </section>

        <div
          data-testid="conteudo-pagina"
          className="pagina-conteudo"
        >
        <section className="pagina-servicos border-t border-gray-100 px-4 py-8 dark:border-neutral-800">
          <h2 className="mb-4 text-lg font-semibold text-gray-900 dark:text-neutral-50">Serviços</h2>

          {estadoCatalogo === "carregando" && <p role="status" className="text-sm text-gray-500 dark:text-neutral-400">Carregando serviços e profissionais...</p>}
          {estadoCatalogo === "erro" && (
            <div role="alert" className="space-y-2 text-sm text-gray-600 dark:text-neutral-400">
              <p>Não foi possível carregar os serviços e profissionais.</p>
              <button onClick={() => {
                setEstadoCatalogo("carregando");
                setTentativaCatalogo((atual) => atual + 1);
              }} className="rounded-lg border border-gray-300 px-3 py-2 dark:border-neutral-700">Tentar novamente</button>
            </div>
          )}
          {estadoCatalogo === "sucesso" && categorias?.length === 0 && <p className="text-sm text-gray-500 dark:text-neutral-400">Nenhum serviço disponível no momento.</p>}
          {estadoCatalogo === "sucesso" && populares.length > 0 && (
            <div className="mb-4">
              <h3 className="mb-2 flex items-center gap-1 text-sm font-semibold text-gray-700 dark:text-neutral-300">
                ⭐ Mais procurados
              </h3>
              <div className="space-y-2">
                {populares.map((s) => (
                  <button
                    key={s.id}
                    onClick={() => abrirAssistente(s.id)}
                    disabled={!aceitaAgendamento}
                    className="flex w-full items-center justify-between rounded-lg border border-gray-200 px-3 py-2 text-left text-sm dark:border-neutral-800"
                  >
                    <span>
                      <span className="font-medium text-gray-900 dark:text-neutral-50">{s.nome}</span>
                      <span className="ml-2 text-xs text-gray-500 dark:text-neutral-400">{s.duracaoMinutos} min</span>
                    </span>
                    <span className="font-semibold text-(--cor-primaria)">{formatarReais(s.preco)}</span>
                  </button>
                ))}
              </div>
            </div>
          )}

          <div className="space-y-2">
            {(estadoCatalogo === "sucesso" ? vitrine : []).map((categoria) => {
              const aberta = categoriasAbertas.has(categoria.categoriaId);
              return (
                <div key={categoria.categoriaId} className="rounded-lg border border-gray-200 dark:border-neutral-800">
                  <button
                    aria-expanded={aberta}
                    onClick={() => alternarCategoria(categoria.categoriaId)}
                    className="flex w-full items-center justify-between px-3 py-2 text-left text-sm font-medium text-gray-800 dark:text-neutral-200"
                  >
                    <span>
                      {categoria.nome} <span className="text-xs text-gray-400">({categoria.servicos.length})</span>
                    </span>
                    <span>{aberta ? "−" : "+"}</span>
                  </button>
                  {aberta && (
                    <div className="space-y-2 border-t border-gray-100 px-3 py-2 dark:border-neutral-800">
                      {categoria.servicos.map((s) => (
                        <button
                          key={s.id}
                          onClick={() => abrirAssistente(s.id)}
                          disabled={!aceitaAgendamento}
                          className="flex w-full items-center justify-between rounded-lg px-2 py-1.5 text-left text-sm hover:bg-gray-50 dark:hover:bg-neutral-900"
                        >
                          <span>
                            <span className="text-gray-900 dark:text-neutral-50">{s.nome}</span>
                            <span className="ml-2 text-xs text-gray-500 dark:text-neutral-400">{s.duracaoMinutos} min</span>
                          </span>
                          <span className="font-semibold text-(--cor-primaria)">{formatarReais(s.preco)}</span>
                        </button>
                      ))}
                    </div>
                  )}
                </div>
              );
            })}
          </div>
        </section>

        {profissionais && profissionais.length > 0 && (
          <section className="pagina-equipe border-t border-gray-100 px-4 py-8 dark:border-neutral-800">
            <h2 className="mb-4 text-lg font-semibold text-gray-900 dark:text-neutral-50">Equipe</h2>
            <div className="flex gap-4 overflow-x-auto pb-2">
              {profissionais.map((p) => (
                <div key={p.id} className="flex w-20 shrink-0 flex-col items-center text-center">
                  {p.fotoUrl ? (
                    <img src={p.fotoUrl} alt="" className="h-16 w-16 rounded-full object-cover" />
                  ) : (
                    <div className="flex h-16 w-16 items-center justify-center rounded-full bg-(--cor-primaria) text-sm font-semibold text-white">
                      {iniciaisNome(p.nome)}
                    </div>
                  )}
                  <span className="mt-2 text-xs font-medium text-gray-800 dark:text-neutral-200">{p.nome.split(" ")[0]}</span>
                  {p.funcao && <span className="text-xs text-gray-500 dark:text-neutral-400">{p.funcao}</span>}
                </div>
              ))}
            </div>
          </section>
        )}

        <section className="pagina-local border-t border-gray-100 px-4 py-8 dark:border-neutral-800">
          <h2 className="mb-3 text-lg font-semibold text-gray-900 dark:text-neutral-50">Localização</h2>
          <p className="text-sm text-gray-700 dark:text-neutral-300">{enderecoResumido(negocio)}</p>
          {enderecoCompleto(negocio) && <p className="text-sm text-gray-500 dark:text-neutral-400">{enderecoCompleto(negocio)}</p>}
          {negocio.telefone && <a href={`tel:${negocio.telefone}`}>{negocio.telefone}</a>}

          {negocio.horarioFuncionamento.length > 0 && (
            <ul className="mt-3 space-y-0.5 text-sm text-gray-600 dark:text-neutral-400">
              {negocio.horarioFuncionamento.map((h) => (
                <li key={h.diaSemana} className="flex justify-between">
                  <span>{NOMES_DIAS_SEMANA[h.diaSemana]}</span>
                  <span>{h.fechado ? "Fechado" : `${h.abertura?.slice(0, 5)} – ${h.fechamento?.slice(0, 5)}`}</span>
                </li>
              ))}
            </ul>
          )}
        </section>

        <FormularioContato />
        </div>
      </main>

      <footer
        className={`pagina-rodape px-4 py-6 text-center text-xs ${
          fundo.temFundo ? classeTextoTopo : "border-t border-gray-100 text-gray-400 dark:border-neutral-800 dark:text-neutral-500"
        }`}
      >
        <p>{negocio.nomeExibido}</p>
        <a href="/privacidade" className="mt-1 inline-block underline">
          Política de privacidade
        </a>
      </footer>

      {aceitaAgendamento && estadoCatalogo === "sucesso" && (
        <AssistenteAgendamento
          aberto={assistenteAberto}
          aoFechar={() => setAssistenteAberto(false)}
          negocio={negocio}
          categorias={categorias ?? []}
          profissionais={profissionais ?? []}
          servicoInicialId={servicoInicialId}
        />
      )}
    </div>
  );
}

function FormularioContato() {
  const id = useId();
  const [nome, setNome] = useState("");
  const [telefone, setTelefone] = useState("");
  const [email, setEmail] = useState("");
  const [mensagem, setMensagem] = useState("");
  const [enviando, setEnviando] = useState(false);
  const [status, setStatus] = useState<"idle" | "enviado" | "erro">("idle");

  async function aoEnviar(evento: React.FormEvent) {
    evento.preventDefault();
    setEnviando(true);
    setStatus("idle");
    try {
      await requisicaoApiPublica("/contato", {
        metodo: "POST",
        corpo: { nome, telefone: telefone || null, email: email || null, mensagem },
      });
      setStatus("enviado");
      setNome("");
      setTelefone("");
      setEmail("");
      setMensagem("");
    } catch {
      setStatus("erro");
    } finally {
      setEnviando(false);
    }
  }

  return (
    <section className="pagina-contato border-t border-gray-100 px-4 py-8 dark:border-neutral-800">
      <h2 className="mb-3 text-lg font-semibold text-gray-900 dark:text-neutral-50">Fale Conosco</h2>
      <form onSubmit={aoEnviar} className="space-y-3">
        <div><label htmlFor={`${id}-nome`}>Nome</label>
        <input id={`${id}-nome`}
          required
          placeholder="Nome"
          value={nome}
          onChange={(e) => setNome(e.target.value)}
          className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm outline-none focus:border-(--cor-primaria) dark:border-neutral-700 dark:bg-neutral-900"
        />
        </div>
        <div className="contato-dupla">
          <div><label htmlFor={`${id}-telefone`}>Telefone</label>
          <input id={`${id}-telefone`} type="tel" inputMode="tel"
            placeholder="Telefone"
            value={telefone}
            onChange={(e) => setTelefone(e.target.value)}
            className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm outline-none focus:border-(--cor-primaria) dark:border-neutral-700 dark:bg-neutral-900"
          />
          </div>
          <div><label htmlFor={`${id}-email`}>E-mail</label>
          <input id={`${id}-email`}
            placeholder="E-mail"
            type="email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm outline-none focus:border-(--cor-primaria) dark:border-neutral-700 dark:bg-neutral-900"
          />
          </div>
        </div>
        <div><label htmlFor={`${id}-mensagem`}>Mensagem</label>
        <textarea id={`${id}-mensagem`}
          required
          rows={3}
          placeholder="Mensagem"
          value={mensagem}
          onChange={(e) => setMensagem(e.target.value)}
          className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm outline-none focus:border-(--cor-primaria) dark:border-neutral-700 dark:bg-neutral-900"
        />
        </div>
        <button
          type="submit"
          disabled={enviando}
          className="publico-cta rounded-lg bg-(--cor-primaria) px-4 py-2 text-sm font-medium text-white transition hover:opacity-90 disabled:opacity-60"
        >
          {enviando ? "Enviando..." : "Enviar"}
        </button>
        {status === "enviado" && <p role="status" className="text-sm text-green-600">Mensagem enviada!</p>}
        {status === "erro" && <p role="alert" className="text-sm text-red-600">Não foi possível enviar. Tente novamente.</p>}
      </form>
    </section>
  );
}
