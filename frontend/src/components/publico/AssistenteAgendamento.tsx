"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { requisicaoApiPublica, ErroApi } from "@/lib/api";
import { formatarReais } from "@/lib/formatacao";
import { dataNoFuso, proximosDiasDoNegocio, formatarDiaDaFaixa, formatarHoraDoNegocio, formatarDataDoNegocio } from "./datas-agendamento";
import { fundoDoNegocio } from "@/lib/fundo";
import type {
  CategoriaComServicosPublicos,
  ConfirmarAgendamentoPublico,
  CriarReservaPublica,
  DetalhePublicoAgendamento,
  HorarioLivrePublico,
  NegocioPublico,
  ProfissionalPublico,
  ServicoPublico,
} from "@/lib/tipos";

/** 1 Profissional · 2 Serviços · 3 Data e horário · 4 Seus dados · 5 Resumo (seção 6.2). */
type Etapa = 1 | 2 | 3 | 4 | 5 | "sucesso";

/** "Qualquer profissional": a etapa de horário acha, entre quem faz a combinação escolhida, alguém livre. */
const QUALQUER = "";

interface Props {
  aberto: boolean;
  aoFechar: () => void;
  negocio: NegocioPublico;
  categorias: CategoriaComServicosPublicos[];
  profissionais: ProfissionalPublico[];
  servicoInicialId: string | null;
}

export function AssistenteAgendamento({ aberto, aoFechar, negocio, categorias, profissionais, servicoInicialId }: Props) {
  // Com um único profissional ativo, o passo Profissional é pulado (seção 6.2.1).
  const unico = profissionais.length === 1 ? profissionais[0] : null;
  const primeiraEtapa: Etapa = unico ? 2 : 1;

  const [etapa, setEtapa] = useState<Etapa>(primeiraEtapa);
  const [servicoIds, setServicoIds] = useState<string[]>([]);
  const [profissionalId, setProfissionalId] = useState<string | null>(null); // null = ainda não escolheu; QUALQUER = qualquer um
  const [dataEscolhida, setDataEscolhida] = useState(() => dataNoFuso(new Date(), negocio.fuso));
  const [disponibilidade, setDisponibilidade] = useState<{
    chave: string; status: "carregando" | "sucesso" | "erro"; horarios: HorarioLivrePublico[];
  } | null>(null);
  const [tentativaHorarios, setTentativaHorarios] = useState(0);
  const [chaveHorarioEscolhido, setChaveHorarioEscolhido] = useState<string | null>(null);
  const [reservando, setReservando] = useState(false);
  const reservaEmCurso = useRef(false);
  const versaoVerificacao = useRef(0);
  const [telefoneVerificado, setTelefoneVerificado] = useState<string | null>(null);
  const [horarioEscolhido, setHorarioEscolhido] = useState<HorarioLivrePublico | null>(null);
  const [agendamentoId, setAgendamentoId] = useState<string | null>(null);
  const [reservadoAte, setReservadoAte] = useState<number | null>(null);

  const [telefone, setTelefone] = useState("");
  const [email, setEmail] = useState("");
  const [nome, setNome] = useState("");
  const [aceite, setAceite] = useState(false);
  const [codigoEnviado, setCodigoEnviado] = useState(false);
  const [codigo, setCodigo] = useState("");
  const [tokenVerificacao, setTokenVerificacao] = useState<string | null>(null);
  const [reenviarEm, setReenviarEm] = useState(0);
  const [enviandoCodigo, setEnviandoCodigo] = useState(false);
  const [validandoCodigo, setValidandoCodigo] = useState(false);

  const [observacoes, setObservacoes] = useState("");
  const [cupomCodigo, setCupomCodigo] = useState("");
  const [desconto, setDesconto] = useState(0);
  const [aplicandoCupom, setAplicandoCupom] = useState(false);
  const [confirmando, setConfirmando] = useState(false);
  const [resumoFinal, setResumoFinal] = useState<DetalhePublicoAgendamento | null>(null);
  const [tokenAgendamento, setTokenAgendamento] = useState<string | null>(null);

  const [erro, setErro] = useState<string | null>(null);

  const todosServicos = useMemo(() => categorias.flatMap((c) => c.servicos), [categorias]);

  // Entrando por um serviço da página (seção 6.1): o passo Profissional mostra só quem o executa.
  const candidatos = servicoInicialId ? profissionais.filter((p) => p.servicoIds.includes(servicoInicialId)) : profissionais;
  const profissionalEscolhido = profissionalId ? profissionais.find((p) => p.id === profissionalId) ?? null : null;
  // Preço e duração são os do profissional (personalizados no vínculo); com "Qualquer", os do cadastro até o horário
  // definir quem atende.
  const profissionalDosValores =
    profissionalEscolhido ?? (horarioEscolhido ? profissionais.find((p) => p.id === horarioEscolhido.profissionalId) ?? null : null);

  const catalogo = useMemo(() => {
    if (!profissionalEscolhido) return categorias;
    const dele = new Set(profissionalEscolhido.servicoIds);
    return categorias
      .map((c) => ({ ...c, servicos: c.servicos.filter((s) => dele.has(s.id)) }))
      .filter((c) => c.servicos.length > 0);
  }, [categorias, profissionalEscolhido]);

  function valoresDe(servico: ServicoPublico) {
    const proprio = profissionalDosValores?.servicos?.find((s) => s.servicoId === servico.id);
    return { preco: proprio?.preco ?? servico.preco, duracaoMinutos: proprio?.duracaoMinutos ?? servico.duracaoMinutos };
  }

  const servicosSelecionados = todosServicos.filter((s) => servicoIds.includes(s.id)).map((s) => ({ ...s, ...valoresDe(s) }));
  const duracaoTotal = servicosSelecionados.reduce((soma, s) => soma + s.duracaoMinutos, 0);
  const totalServicos = servicosSelecionados.reduce((soma, s) => soma + s.preco, 0);
  const totalComDesconto = totalServicos - desconto;

  const profissionaisElegiveis = profissionalId
    ? profissionais.filter((p) => p.id === profissionalId)
    : profissionais.filter((p) => servicoIds.every((id) => p.servicoIds.includes(id)));
  const nomeDoProfissional = profissionalDosValores?.nome ?? null;

  // A consulta não depende do horário selecionado: a API calcula a duração de cada profissional.
  const duracaoConsulta = todosServicos.filter((s) => servicoIds.includes(s.id)).reduce((soma, s) =>
    soma + (profissionalEscolhido?.servicos?.find((v) => v.servicoId === s.id)?.duracaoMinutos ?? s.duracaoMinutos), 0);
  const parametrosHorarios = new URLSearchParams({ data: dataEscolhida, duracaoMinutos: String(duracaoConsulta) });
  if (profissionalId) parametrosHorarios.set("profissionalId", profissionalId);
  for (const id of [...servicoIds].sort()) parametrosHorarios.append("servicoIds", id);
  const chaveDisponibilidade = parametrosHorarios.toString();
  const disponibilidadeAtual = disponibilidade?.chave === chaveDisponibilidade ? disponibilidade : null;
  const horariosLivres = disponibilidadeAtual?.status === "sucesso" ? disponibilidadeAtual.horarios : null;

  useEffect(() => {
    // Reseta o assistente toda vez que ele é reaberto.
    if (!aberto) return;
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setEtapa(primeiraEtapa);
    setServicoIds(servicoInicialId ? [servicoInicialId] : []);
    setProfissionalId(unico ? unico.id : null);
    setDataEscolhida(dataNoFuso(new Date(), negocio.fuso));
    setDisponibilidade(null);
    setChaveHorarioEscolhido(null);
    setTelefoneVerificado(null);
    versaoVerificacao.current += 1;
    setHorarioEscolhido(null);
    setAgendamentoId(null);
    setReservadoAte(null);
    setTelefone("");
    setEmail("");
    setNome("");
    setAceite(false);
    setCodigoEnviado(false);
    setCodigo("");
    setTokenVerificacao(null);
    setObservacoes("");
    setCupomCodigo("");
    setDesconto(0);
    setResumoFinal(null);
    setTokenAgendamento(null);
    setErro(null);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [aberto, servicoInicialId]);

  useEffect(() => {
    if (!aberto || etapa !== 3 || duracaoConsulta === 0 || profissionaisElegiveis.length === 0) return;
    let atual = true;
    // A chave protege o render e a reserva antes mesmo de este efeito executar.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setDisponibilidade({ chave: chaveDisponibilidade, status: "carregando", horarios: [] });
    setHorarioEscolhido(null);
    setChaveHorarioEscolhido(null);
    requisicaoApiPublica<HorarioLivrePublico[]>(`/horarios-livres?${chaveDisponibilidade}`).then((lista) => {
      if (!atual) return;
      const vistos = new Set<string>();
      setDisponibilidade({ chave: chaveDisponibilidade, status: "sucesso", horarios:
        lista.filter((h) => !vistos.has(h.inicio) && vistos.add(h.inicio)) });
    }).catch(() => {
      if (atual) setDisponibilidade({ chave: chaveDisponibilidade, status: "erro", horarios: [] });
    });
    return () => { atual = false; };
  }, [aberto, etapa, chaveDisponibilidade, duracaoConsulta, profissionaisElegiveis.length, tentativaHorarios]);

  useEffect(() => {
    if (reenviarEm <= 0) return;
    const id = setTimeout(() => setReenviarEm((atual) => atual - 1), 1000);
    return () => clearTimeout(id);
  }, [reenviarEm]);

  if (!aberto) return null;

  /** Trocar de profissional tira os serviços que o novo não executa. */
  function escolherProfissional(id: string) {
    setProfissionalId(id);
    setHorarioEscolhido(null);
    setChaveHorarioEscolhido(null);
    setDisponibilidade(null);
    if (id !== QUALQUER) {
      const dele = profissionais.find((p) => p.id === id)?.servicoIds ?? [];
      setServicoIds((atual) => atual.filter((s) => dele.includes(s)));
    }
  }

  function alternarServico(id: string) {
    setHorarioEscolhido(null);
    setChaveHorarioEscolhido(null);
    setDisponibilidade(null);
    setServicoIds((atual) => (atual.includes(id) ? atual.filter((s) => s !== id) : [...atual, id]));
  }

  async function avancarParaResumo() {
    if (!podeContinuarHorario || !horarioEscolhido || reservaEmCurso.current) return;
    reservaEmCurso.current = true;
    setReservando(true);
    setErro(null);

    try {
      const dados: CriarReservaPublica = {
        profissionalId: horarioEscolhido.profissionalId,
        servicoIds,
        inicio: horarioEscolhido.inicio,
      };
      const resposta = await requisicaoApiPublica<{ agendamentoId: string }>("/reservas", { metodo: "POST", corpo: dados });
      setAgendamentoId(resposta.agendamentoId);
      setReservadoAte(Date.now() + 10 * 60 * 1000);
      setEtapa(4);
    } catch (excecao) {
      if (excecao instanceof ErroApi && excecao.status === 409) {
        setErro("Esse horário acabou de ser preenchido. Volte e escolha outro.");
      } else {
        setErro("Não foi possível reservar esse horário. Tente novamente.");
      }
    } finally {
      reservaEmCurso.current = false;
      setReservando(false);
    }
  }

  async function enviarCodigo() {
    setErro(null);
    setEnviandoCodigo(true);
    try {
      await requisicaoApiPublica("/codigos", { metodo: "POST", corpo: { telefone, email: email || null } });
      setCodigoEnviado(true);
      setReenviarEm(60);
    } catch {
      setErro("Não foi possível enviar o código. Confira o telefone e tente de novo.");
    } finally {
      setEnviandoCodigo(false);
    }
  }

  /** Troca o código e manda de novo pelos dois canais; o limite de reenvios vem da API, com a mensagem pronta. */
  async function reenviarCodigo() {
    setErro(null);
    setEnviandoCodigo(true);
    try {
      await requisicaoApiPublica("/codigos/reenviar", { metodo: "POST", corpo: { telefone, email: email || null } });
      setCodigo("");
      setReenviarEm(60);
    } catch (excecao) {
      setErro(
        excecao instanceof ErroApi && excecao.status === 429
          ? excecao.message
          : "Não foi possível reenviar o código. Tente de novo em instantes.",
      );
    } finally {
      setEnviandoCodigo(false);
    }
  }

  async function validarCodigo() {
    const versao = versaoVerificacao.current;
    const telefoneDoCodigo = telefone;
    setErro(null);
    setValidandoCodigo(true);
    try {
      const resposta = await requisicaoApiPublica<{ tokenVerificacao: string }>("/codigos/validar", {
        metodo: "POST",
        corpo: { telefone, codigo },
      });
      if (versao !== versaoVerificacao.current) return;
      setTelefoneVerificado(telefoneDoCodigo);
      setTokenVerificacao(resposta.tokenVerificacao);
      setEtapa(5);
    } catch {
      setErro("Código inválido ou expirado. Confira e tente de novo.");
    } finally {
      setValidandoCodigo(false);
    }
  }

  async function aplicarCupom() {
    if (!agendamentoId || !cupomCodigo) return;
    setErro(null);
    setAplicandoCupom(true);
    try {
      const resposta = await requisicaoApiPublica<{ desconto: number }>("/cupons/validar", {
        metodo: "POST",
        corpo: { agendamentoId, codigo: cupomCodigo },
      });
      setDesconto(resposta.desconto);
    } catch {
      setDesconto(0);
      setErro("Cupom inválido.");
    } finally {
      setAplicandoCupom(false);
    }
  }

  async function confirmarAgendamento() {
    if (!agendamentoId || !podeConfirmar || !tokenVerificacao) return;
    setErro(null);
    setConfirmando(true);
    try {
      const dados: ConfirmarAgendamentoPublico = {
        agendamentoId,
        tokenVerificacao,
        nome,
        telefone,
        email: email || null,
        observacoes: observacoes || null,
        codigoCupom: cupomCodigo || null,
      };
      const resposta = await requisicaoApiPublica<{ agendamentoId: string; tokenAgendamento: string }>("/agendamentos", {
        metodo: "POST",
        corpo: dados,
      });
      setTokenAgendamento(resposta.tokenAgendamento);
      const detalhe = await requisicaoApiPublica<DetalhePublicoAgendamento>(
        `/meus-agendamentos/${resposta.tokenAgendamento}`,
      ).catch(() => null);
      setResumoFinal(
        detalhe ?? {
          id: agendamentoId,
          nomeNegocio: negocio.nomeExibido,
          local: [negocio.rua, negocio.bairro].filter(Boolean).join(", "),
          inicio: horarioEscolhido!.inicio,
          fim: horarioEscolhido!.inicio,
          servicos: servicosSelecionados.map((s) => s.nome),
          total: totalComDesconto,
          status: "Agendado",
        },
      );
      setEtapa("sucesso");
    } catch (excecao) {
      if (excecao instanceof ErroApi && excecao.status === 409) {
        setErro("Esse horário acabou de ser preenchido. Volte e escolha outro.");
      } else {
        setErro("Não foi possível confirmar o agendamento.");
      }
    } finally {
      setConfirmando(false);
    }
  }

  const podeEscolherServicos = profissionalId !== null;
  const podeContinuarServicos = servicoIds.length > 0;
  const podeContinuarHorario = horarioEscolhido !== null && chaveHorarioEscolhido === chaveDisponibilidade
    && disponibilidadeAtual?.status === "sucesso"
    && dataNoFuso(horarioEscolhido.inicio, negocio.fuso) === dataEscolhida
    && (horariosLivres?.some((h) => h.inicio === horarioEscolhido.inicio && h.profissionalId === horarioEscolhido.profissionalId) ?? false);
  const etapas: number[] = unico ? [2, 3, 4, 5] : [1, 2, 3, 4, 5];
  // DDD + número (10 ou 11 dígitos) basta: a API completa o +55. Com "+", é número de outro país.
  const podeEnviarCodigo = telefone.replace(/\D/g, "").length >= 10 && nome.trim().length > 0 && aceite;
  const podeConfirmar = tokenVerificacao !== null && telefoneVerificado === telefone;

  // Mesma identidade da página (seção 5): logo e fundo do negócio em volta; os passos num painel do tema, legíveis.
  const fundo = fundoDoNegocio(negocio);

  return (
    <div
      data-testid="assistente-agendamento"
      className={`fixed inset-0 z-40 flex flex-col ${fundo.temFundo ? "" : "bg-white dark:bg-neutral-950"}`}
      style={fundo.estilo}
    >
      <div data-testid="assistente-marca" className="flex items-center gap-2 px-4 pt-3 pb-1">
        {negocio.logoUrl && <img src={negocio.logoUrl} alt="" className="h-7 w-7 rounded-full object-cover" />}
        <span
          className={`text-sm font-semibold ${
            fundo.temFundo ? (fundo.textoClaro ? "text-white" : "text-gray-900") : "text-gray-900 dark:text-neutral-50"
          }`}
        >
          {negocio.nomeExibido}
        </span>
      </div>

      <div
        className={
          fundo.temFundo
            ? "mx-2 mt-2 mb-2 flex min-h-0 flex-1 flex-col overflow-hidden rounded-2xl bg-white shadow-sm sm:mx-auto sm:w-full sm:max-w-xl dark:bg-neutral-950"
            : "flex min-h-0 flex-1 flex-col"
        }
      >
      <div className="flex items-center justify-between border-b border-gray-100 px-4 py-3 dark:border-neutral-800">
        {etapa !== primeiraEtapa && etapa !== "sucesso" ? (
          <button disabled={reservando} onClick={() => setEtapa((e) => (typeof e === "number" ? ((e - 1) as Etapa) : e))} className="text-sm text-gray-500">
            ← Voltar
          </button>
        ) : (
          <span />
        )}
        <div className="flex gap-1">
          {etapas.map((n) => (
            <span
              key={n}
              className={`h-1.5 w-6 rounded-full ${typeof etapa === "number" && etapa >= n ? "bg-(--cor-primaria)" : "bg-gray-200 dark:bg-neutral-800"}`}
            />
          ))}
        </div>
        <button disabled={reservando} onClick={aoFechar} aria-label="Fechar" className="text-gray-400 hover:text-gray-700">
          ✕
        </button>
      </div>

      <div className="flex-1 overflow-y-auto px-4 py-4">
        {erro && <p className="mb-3 rounded-lg bg-red-50 px-3 py-2 text-sm text-red-700 dark:bg-red-950 dark:text-red-300">{erro}</p>}

        {etapa === 1 && (
          <div>
            <h2 className="mb-3 text-lg font-semibold text-gray-900 dark:text-neutral-50">Escolha o profissional</h2>
            <div className="space-y-2" role="group" aria-label="Profissionais">
              <button
                aria-pressed={profissionalId === QUALQUER}
                onClick={() => escolherProfissional(QUALQUER)}
                className={`flex w-full items-center gap-3 rounded-lg border px-3 py-3 text-left text-sm ${
                  profissionalId === QUALQUER ? "border-(--cor-primaria) bg-blue-50 dark:bg-blue-950" : "border-gray-300 dark:border-neutral-700"
                }`}
              >
                <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-(--cor-primaria) text-white">★</span>
                <span>
                  <span className="block font-medium text-gray-900 dark:text-neutral-50">
                    {servicoInicialId ? "Qualquer profissional entre eles" : "Qualquer profissional"}
                  </span>
                  <span className="block text-xs text-gray-500 dark:text-neutral-400">O primeiro horário livre de quem faz o serviço</span>
                </span>
              </button>
              {candidatos.map((p) => (
                <button
                  key={p.id}
                  aria-pressed={profissionalId === p.id}
                  onClick={() => escolherProfissional(p.id)}
                  className={`flex w-full items-center gap-3 rounded-lg border px-3 py-3 text-left text-sm ${
                    profissionalId === p.id ? "border-(--cor-primaria) bg-blue-50 dark:bg-blue-950" : "border-gray-200 dark:border-neutral-800"
                  }`}
                >
                  {p.fotoUrl ? (
                    // eslint-disable-next-line @next/next/no-img-element
                    <img src={p.fotoUrl} alt="" className="h-10 w-10 shrink-0 rounded-full object-cover" />
                  ) : (
                    <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-gray-100 font-semibold text-gray-600 dark:bg-neutral-800 dark:text-neutral-300">
                      {p.nome.charAt(0)}
                    </span>
                  )}
                  <span>
                    <span className="block font-medium text-gray-900 dark:text-neutral-50">{p.nome}</span>
                    {p.funcao && <span className="block text-xs text-gray-500 dark:text-neutral-400">{p.funcao}</span>}
                  </span>
                </button>
              ))}
            </div>
          </div>
        )}

        {etapa === 2 && (
          <div>
            <h2 className="mb-1 text-lg font-semibold text-gray-900 dark:text-neutral-50">Escolha os serviços</h2>
            <p className="mb-3 text-sm text-gray-500 dark:text-neutral-400">
              {profissionalEscolhido ? `Com ${profissionalEscolhido.nome}` : "Com qualquer profissional"}
            </p>
            {catalogo.length === 0 && (
              <div className="space-y-2 text-sm text-gray-500 dark:text-neutral-400">
                <p>Nenhum serviço disponível para essa escolha.</p>
                <button onClick={() => unico ? aoFechar() : setEtapa(1)} className="rounded-lg border border-gray-300 px-3 py-2 dark:border-neutral-700">
                  {unico ? "Voltar à página" : "Escolher outro profissional"}
                </button>
              </div>
            )}
            {catalogo.map((categoria) => (
              <div key={categoria.categoriaId} className="mb-4">
                <h3 className="mb-2 text-sm font-semibold text-gray-600 dark:text-neutral-400">{categoria.nome}</h3>
                <div className="space-y-2">
                  {categoria.servicos.map((s) => {
                    const selecionado = servicoIds.includes(s.id);
                    const valores = valoresDe(s);
                    return (
                      <button
                        key={s.id}
                        aria-pressed={selecionado}
                        onClick={() => alternarServico(s.id)}
                        className={`flex w-full items-center justify-between rounded-lg border px-3 py-2 text-left text-sm ${
                          selecionado ? "border-(--cor-primaria) bg-blue-50 dark:bg-blue-950" : "border-gray-200 dark:border-neutral-800"
                        }`}
                      >
                        <span>
                          <span className="text-gray-900 dark:text-neutral-50">{s.nome}</span>
                          <span className="ml-2 text-xs text-gray-500 dark:text-neutral-400">
                            {valores.duracaoMinutos} min · {formatarReais(valores.preco)}
                          </span>
                        </span>
                        <span
                          className={`flex h-6 w-6 items-center justify-center rounded-full text-xs ${
                            selecionado ? "bg-(--cor-primaria) text-white" : "border border-gray-300 dark:border-neutral-700"
                          }`}
                        >
                          {selecionado ? "✓" : "+"}
                        </span>
                      </button>
                    );
                  })}
                </div>
              </div>
            ))}
          </div>
        )}

        {etapa === 3 && (
          <div>
            <h2 className="mb-1 text-lg font-semibold text-gray-900 dark:text-neutral-50">Data e horário</h2>
            <p className="mb-3 text-sm text-gray-500 dark:text-neutral-400">
              {profissionalEscolhido ? `Com ${profissionalEscolhido.nome}` : "Com qualquer profissional"}
            </p>

            <div className="mb-3 flex gap-2 overflow-x-auto pb-1">
              {proximosDiasDoNegocio(14, negocio.fuso).map((dia) => {
                const selecionado = dia === dataEscolhida;
                return (
                  <button
                    key={dia}
                    disabled={reservando}
                    onClick={() => {
                      setDataEscolhida(dia);
                      setHorarioEscolhido(null);
                      setChaveHorarioEscolhido(null);
                      setDisponibilidade(null);
                    }}
                    className={`flex shrink-0 flex-col items-center rounded-lg border px-3 py-2 text-xs ${
                      selecionado ? "border-(--cor-primaria) bg-blue-50 dark:bg-blue-950" : "border-gray-200 dark:border-neutral-800"
                    }`}
                  >
                    <span>{formatarDiaDaFaixa(dia)}</span>
                    <span className="font-semibold">{Number(dia.slice(8))}</span>
                  </button>
                );
              })}
            </div>

            {profissionaisElegiveis.length === 0 ? (
              <p className="text-sm text-gray-500">Nenhum profissional faz todos esses serviços juntos. Volte e ajuste a escolha.</p>
            ) : disponibilidadeAtual?.status === "erro" ? (
              <div role="alert" className="space-y-2 text-sm text-gray-500 dark:text-neutral-400">
                <p>Não foi possível consultar os horários.</p>
                <button onClick={() => {
                  setDisponibilidade(null);
                  setHorarioEscolhido(null);
                  setChaveHorarioEscolhido(null);
                  setTentativaHorarios((atual) => atual + 1);
                }} className="rounded-lg border border-gray-300 px-3 py-2 dark:border-neutral-700">Tentar novamente</button>
              </div>
            ) : horariosLivres === null ? (
              <p className="text-sm text-gray-500">Carregando horários...</p>
            ) : horariosLivres.length === 0 ? (
              <p className="text-sm text-gray-500">Nenhum horário livre neste dia. Escolha outra data.</p>
            ) : (
              <div className="grid grid-cols-3 gap-2">
                {horariosLivres.map((h) => (
                  <button
                    key={h.inicio}
                    disabled={reservando}
                    onClick={() => { setHorarioEscolhido(h); setChaveHorarioEscolhido(chaveDisponibilidade); }}
                    className={`rounded-lg border px-2 py-2 text-sm ${
                      horarioEscolhido?.inicio === h.inicio && horarioEscolhido.profissionalId === h.profissionalId
                        ? "border-(--cor-primaria) bg-blue-50 dark:bg-blue-950"
                        : "border-gray-200 dark:border-neutral-800"
                    }`}
                  >
                    {formatarHoraDoNegocio(h.inicio, negocio.fuso)}
                  </button>
                ))}
              </div>
            )}
          </div>
        )}

        {etapa === 4 && (
          <div className="space-y-3">
            <h2 className="text-lg font-semibold text-gray-900 dark:text-neutral-50">Seus dados</h2>
            {reservadoAte && (
              <p className="text-xs text-gray-500 dark:text-neutral-400">
                Horário reservado por alguns minutos enquanto você confirma os dados.
              </p>
            )}

            <input
              placeholder="Nome completo"
              value={nome}
              onChange={(e) => setNome(e.target.value)}
              disabled={codigoEnviado || enviandoCodigo || validandoCodigo}
              className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm disabled:opacity-60 dark:border-neutral-700 dark:bg-neutral-900"
            />
            <input
              placeholder="WhatsApp com DDD — ex.: (71) 98888-7777"
              aria-label="WhatsApp com DDD"
              type="tel"
              inputMode="tel"
              autoComplete="tel-national"
              value={telefone}
              onChange={(e) => {
                if (e.target.value !== telefone) {
                  versaoVerificacao.current += 1;
                  setTokenVerificacao(null);
                  setTelefoneVerificado(null);
                  setCodigo("");
                }
                setTelefone(e.target.value);
              }}
              disabled={codigoEnviado || enviandoCodigo || validandoCodigo}
              className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm disabled:opacity-60 dark:border-neutral-700 dark:bg-neutral-900"
            />
            <input
              placeholder="E-mail"
              type="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              disabled={codigoEnviado || enviandoCodigo || validandoCodigo}
              className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm disabled:opacity-60 dark:border-neutral-700 dark:bg-neutral-900"
            />
            {!codigoEnviado && !email.trim() && (
              <p data-testid="aviso-sem-email" className="text-xs text-gray-500 dark:text-neutral-400">
                Sem e-mail, o código chega só pelo WhatsApp. Com e-mail, você recebe pelos dois.
              </p>
            )}

            {!codigoEnviado && (
              <label className="flex items-start gap-2 text-xs text-gray-600 dark:text-neutral-400">
                <input type="checkbox" checked={aceite} onChange={(e) => setAceite(e.target.checked)} className="mt-0.5" />
                <span>
                  Ao agendar, concordo com os termos de serviço e a{" "}
                  <a href="/privacidade" target="_blank" rel="noopener noreferrer" className="underline">
                    política de privacidade
                  </a>
                  , e aceito receber lembretes e confirmações dos meus agendamentos por e-mail e WhatsApp.
                </span>
              </label>
            )}

            {!codigoEnviado ? (
              <button
                onClick={enviarCodigo}
                disabled={!podeEnviarCodigo || enviandoCodigo}
                className="w-full rounded-lg bg-(--cor-primaria) px-4 py-2 text-sm font-medium text-white disabled:opacity-60"
              >
                {enviandoCodigo ? "Enviando..." : "Enviar código"}
              </button>
            ) : (
              <div className="space-y-2">
                <p className="text-sm text-gray-600 dark:text-neutral-400">
                  {email.trim()
                    ? "Digite o código de 6 dígitos enviado por WhatsApp e e-mail."
                    : "Digite o código de 6 dígitos enviado por WhatsApp."}
                </p>
                <input
                  placeholder="000000"
                  value={codigo}
                  onChange={(e) => setCodigo(e.target.value)}
                  maxLength={6}
                  className="w-full rounded-lg border border-gray-300 px-3 py-2 text-center text-lg tracking-widest dark:border-neutral-700 dark:bg-neutral-900"
                />
                <button
                  onClick={validarCodigo}
                  disabled={codigo.length !== 6 || validandoCodigo}
                  className="w-full rounded-lg bg-(--cor-primaria) px-4 py-2 text-sm font-medium text-white disabled:opacity-60"
                >
                  {validandoCodigo ? "Validando..." : "Confirmar código"}
                </button>
                <div className="flex justify-between text-xs">
                  <button
                    disabled={enviandoCodigo || validandoCodigo}
                    onClick={() => {
                      setCodigoEnviado(false);
                      setCodigo("");
                    }}
                    className="text-gray-500 hover:underline"
                  >
                    Mudar dados
                  </button>
                  <button onClick={reenviarCodigo} disabled={reenviarEm > 0 || enviandoCodigo} className="text-(--cor-primaria) disabled:opacity-50">
                    {reenviarEm > 0 ? `Reenviar em ${reenviarEm}s` : "Reenviar código"}
                  </button>
                </div>
              </div>
            )}
          </div>
        )}

        {etapa === 5 && horarioEscolhido && (
          <div className="space-y-4">
            <h2 className="text-lg font-semibold text-gray-900 dark:text-neutral-50">Resumo</h2>

            <div>
              <h3 className="text-xs font-semibold uppercase text-gray-500">Quando</h3>
              <p className="text-sm text-gray-800 dark:text-neutral-200">
                {formatarDataDoNegocio(horarioEscolhido.inicio, negocio.fuso)} às{" "}
                {formatarHoraDoNegocio(horarioEscolhido.inicio, negocio.fuso)}
              </p>
            </div>

            <div>
              <h3 className="text-xs font-semibold uppercase text-gray-500">Onde</h3>
              <p className="text-sm text-gray-800 dark:text-neutral-200">{[negocio.bairro, negocio.cidade].filter(Boolean).join(", ")}</p>
            </div>

            <div>
              <h3 className="text-xs font-semibold uppercase text-gray-500">Serviços</h3>
              {nomeDoProfissional && <p className="text-xs text-gray-500 dark:text-neutral-400">com {nomeDoProfissional}</p>}
              <ul className="text-sm text-gray-800 dark:text-neutral-200">
                {servicosSelecionados.map((s) => (
                  <li key={s.id} className="flex justify-between">
                    <span>{s.nome}</span>
                    <span>{formatarReais(s.preco)}</span>
                  </li>
                ))}
              </ul>
            </div>

            <div className="flex gap-2">
              <input
                placeholder="Cupom"
                value={cupomCodigo}
                onChange={(e) => setCupomCodigo(e.target.value)}
                className="flex-1 rounded-lg border border-gray-300 px-3 py-2 text-sm dark:border-neutral-700 dark:bg-neutral-900"
              />
              <button
                onClick={aplicarCupom}
                disabled={!cupomCodigo || aplicandoCupom}
                className="rounded-lg border border-gray-300 px-3 py-2 text-sm dark:border-neutral-700"
              >
                Aplicar
              </button>
            </div>

            <textarea
              placeholder="Observações (opcional)"
              value={observacoes}
              onChange={(e) => setObservacoes(e.target.value)}
              rows={2}
              className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm dark:border-neutral-700 dark:bg-neutral-900"
            />
          </div>
        )}

        {etapa === "sucesso" && resumoFinal && (
          <div className="space-y-4 text-center">
            <div className="mx-auto flex h-14 w-14 items-center justify-center rounded-full bg-green-100 text-2xl text-green-600 dark:bg-green-950">
              ✓
            </div>
            <h2 className="text-lg font-semibold text-gray-900 dark:text-neutral-50">Agendamento confirmado!</h2>
            <p className="text-sm text-gray-600 dark:text-neutral-400">
              {formatarDataDoNegocio(resumoFinal.inicio, negocio.fuso)} às{" "}
              {formatarHoraDoNegocio(resumoFinal.inicio, negocio.fuso)}
            </p>
            <p className="text-sm text-gray-600 dark:text-neutral-400">{resumoFinal.servicos.join(", ")}</p>
            <div className="flex flex-col gap-2">
              <a
                href={`/api/publico/meus-agendamentos/${tokenAgendamento}/ics`}
                className="rounded-lg border border-gray-300 px-4 py-2 text-sm dark:border-neutral-700"
              >
                Adicionar ao calendário
              </a>
              {tokenAgendamento && (
                <a href={`/agendamentos/${tokenAgendamento}`} className="text-sm text-(--cor-primaria) hover:underline">
                  Cancelar ou remarcar
                </a>
              )}
            </div>
            <button onClick={aoFechar} className="w-full rounded-lg bg-(--cor-primaria) px-4 py-2 text-sm font-medium text-white">
              Fechar
            </button>
          </div>
        )}
      </div>

      {etapa !== "sucesso" && (
        <div className="border-t border-gray-100 px-4 py-3 dark:border-neutral-800">
          <div className="mb-2 flex items-center justify-between text-sm">
            <span className="text-gray-500 dark:text-neutral-400">
              {servicosSelecionados.length > 0 ? `${servicosSelecionados.length} serviço(s) · ${duracaoTotal} min` : "Nenhum serviço"}
            </span>
            <span className="font-semibold text-gray-900 dark:text-neutral-50">
              {formatarReais(etapa === 5 ? totalComDesconto : totalServicos)}
            </span>
          </div>
          <button
            disabled={
              (etapa === 1 && !podeEscolherServicos) ||
              (etapa === 2 && !podeContinuarServicos) ||
              (etapa === 3 && (!podeContinuarHorario || reservando)) ||
              (etapa === 4 && !podeConfirmar) ||
              (etapa === 5 && confirmando)
            }
            onClick={() => {
              if (etapa === 1) setEtapa(2);
              else if (etapa === 2) { setDisponibilidade(null); setHorarioEscolhido(null); setChaveHorarioEscolhido(null); setEtapa(3); }
              else if (etapa === 3) avancarParaResumo();
              else if (etapa === 4) setEtapa(5);
              else if (etapa === 5) confirmarAgendamento();
            }}
            className="w-full rounded-lg bg-(--cor-primaria) px-4 py-3 text-sm font-semibold text-white disabled:opacity-50"
          >
            {reservando ? "Reservando..." : textoBotaoContinuar(etapa, { podeEscolherServicos, podeContinuarServicos, podeContinuarHorario, podeConfirmar, confirmando })}
          </button>
        </div>
      )}
      </div>
    </div>
  );
}

function textoBotaoContinuar(
  etapa: Etapa,
  estado: {
    podeEscolherServicos: boolean;
    podeContinuarServicos: boolean;
    podeContinuarHorario: boolean;
    podeConfirmar: boolean;
    confirmando: boolean;
  },
): string {
  if (etapa === 1) return estado.podeEscolherServicos ? "Continuar" : "Selecione um profissional";
  if (etapa === 2) return estado.podeContinuarServicos ? "Continuar" : "Selecione um serviço";
  if (etapa === 3) return estado.podeContinuarHorario ? "Continuar" : "Selecione um horário";
  if (etapa === 4) return estado.podeConfirmar ? "Continuar" : "Valide o código";
  if (etapa === 5) return estado.confirmando ? "Confirmando..." : "Confirmar Agendamento";
  return "Continuar";
}
