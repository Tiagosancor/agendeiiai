"use client";

import { useEffect, useMemo, useState } from "react";
import { requisicaoApiPublica, ErroApi } from "@/lib/api";
import { dataLocalIso, formatarReais } from "@/lib/formatacao";
import type {
  CategoriaComServicosPublicos,
  ConfirmarAgendamentoPublico,
  CriarReservaPublica,
  DetalhePublicoAgendamento,
  HorarioLivrePublico,
  NegocioPublico,
  ProfissionalPublico,
} from "@/lib/tipos";

type Etapa = 1 | 2 | 3 | 4 | "sucesso";

interface Props {
  aberto: boolean;
  aoFechar: () => void;
  negocio: NegocioPublico;
  categorias: CategoriaComServicosPublicos[];
  profissionais: ProfissionalPublico[];
  servicoInicialId: string | null;
}

function formatarHora(iso: string): string {
  return new Date(iso).toLocaleTimeString("pt-BR", { hour: "2-digit", minute: "2-digit" });
}

function proximosDias(quantidade: number): Date[] {
  return Array.from({ length: quantidade }, (_, i) => {
    const dia = new Date();
    dia.setDate(dia.getDate() + i);
    return dia;
  });
}

export function AssistenteAgendamento({ aberto, aoFechar, negocio, categorias, profissionais, servicoInicialId }: Props) {
  const [etapa, setEtapa] = useState<Etapa>(1);
  const [servicoIds, setServicoIds] = useState<string[]>([]);
  const [profissionalId, setProfissionalId] = useState<string>(""); // "" = qualquer profissional
  const [dataEscolhida, setDataEscolhida] = useState(() => new Date());
  const [horariosLivres, setHorariosLivres] = useState<HorarioLivrePublico[] | null>(null);
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
  const servicosSelecionados = todosServicos.filter((s) => servicoIds.includes(s.id));
  const duracaoTotal = servicosSelecionados.reduce((soma, s) => soma + s.duracaoMinutos, 0);
  const totalServicos = servicosSelecionados.reduce((soma, s) => soma + s.preco, 0);
  const totalComDesconto = totalServicos - desconto;

  const profissionaisElegiveis = profissionalId
    ? profissionais.filter((p) => p.id === profissionalId)
    : profissionais.filter((p) => servicoIds.every((id) => p.servicoIds.includes(id)));

  useEffect(() => {
    // Reseta o assistente toda vez que ele é reaberto.
    if (!aberto) return;
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setEtapa(1);
    setServicoIds(servicoInicialId ? [servicoInicialId] : []);
    setProfissionalId("");
    setDataEscolhida(new Date());
    setHorariosLivres(null);
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
  }, [aberto, servicoInicialId]);

  useEffect(() => {
    if (etapa !== 2 || duracaoTotal === 0) return;

    const dataIso = dataLocalIso(dataEscolhida);
    const parametros = new URLSearchParams({ data: dataIso, duracaoMinutos: String(duracaoTotal) });
    if (profissionalId) parametros.set("profissionalId", profissionalId);
    for (const id of servicoIds) parametros.append("servicoIds", id);

    // Com "Qualquer profissional", a API devolve o mesmo horário uma vez por profissional
    // livre — o cliente vê cada horário uma vez só, e a reserva fica com o primeiro livre.
    requisicaoApiPublica<HorarioLivrePublico[]>(`/horarios-livres?${parametros.toString()}`).then((lista) => {
      const vistos = new Set<string>();
      setHorariosLivres(lista.filter((h) => !vistos.has(h.inicio) && vistos.add(h.inicio)));
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [etapa, dataEscolhida, profissionalId, duracaoTotal]);

  useEffect(() => {
    if (reenviarEm <= 0) return;
    const id = setTimeout(() => setReenviarEm((atual) => atual - 1), 1000);
    return () => clearTimeout(id);
  }, [reenviarEm]);

  if (!aberto) return null;

  function alternarServico(id: string) {
    setServicoIds((atual) => (atual.includes(id) ? atual.filter((s) => s !== id) : [...atual, id]));
  }

  async function avancarParaResumo() {
    if (!horarioEscolhido) return;
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
      setEtapa(3);
    } catch (excecao) {
      if (excecao instanceof ErroApi && excecao.status === 409) {
        setErro("Esse horário acabou de ser preenchido. Volte e escolha outro.");
      } else {
        setErro("Não foi possível reservar esse horário. Tente novamente.");
      }
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

  async function validarCodigo() {
    setErro(null);
    setValidandoCodigo(true);
    try {
      const resposta = await requisicaoApiPublica<{ tokenVerificacao: string }>("/codigos/validar", {
        metodo: "POST",
        corpo: { telefone, codigo },
      });
      setTokenVerificacao(resposta.tokenVerificacao);
      setEtapa(4);
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
    if (!agendamentoId || !tokenVerificacao) return;
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

  const podeContinuarEtapa1 = servicoIds.length > 0;
  const podeContinuarEtapa2 = horarioEscolhido !== null;
  const podeEnviarCodigo = telefone.trim().length >= 8 && nome.trim().length > 0 && aceite;
  const podeConfirmar = tokenVerificacao !== null;

  return (
    <div data-testid="assistente-agendamento" className="fixed inset-0 z-40 flex flex-col bg-white dark:bg-neutral-950">
      <div className="flex items-center justify-between border-b border-gray-100 px-4 py-3 dark:border-neutral-800">
        {etapa !== 1 && etapa !== "sucesso" ? (
          <button onClick={() => setEtapa((e) => (typeof e === "number" ? ((e - 1) as Etapa) : e))} className="text-sm text-gray-500">
            ← Voltar
          </button>
        ) : (
          <span />
        )}
        <div className="flex gap-1">
          {[1, 2, 3, 4].map((n) => (
            <span
              key={n}
              className={`h-1.5 w-6 rounded-full ${typeof etapa === "number" && etapa >= n ? "bg-(--cor-primaria)" : "bg-gray-200 dark:bg-neutral-800"}`}
            />
          ))}
        </div>
        <button onClick={aoFechar} aria-label="Fechar" className="text-gray-400 hover:text-gray-700">
          ✕
        </button>
      </div>

      <div className="flex-1 overflow-y-auto px-4 py-4">
        {erro && <p className="mb-3 rounded-lg bg-red-50 px-3 py-2 text-sm text-red-700 dark:bg-red-950 dark:text-red-300">{erro}</p>}

        {etapa === 1 && (
          <div>
            <h2 className="mb-3 text-lg font-semibold text-gray-900 dark:text-neutral-50">Escolha os serviços</h2>
            {categorias.map((categoria) => (
              <div key={categoria.categoriaId} className="mb-4">
                <h3 className="mb-2 text-sm font-semibold text-gray-600 dark:text-neutral-400">{categoria.nome}</h3>
                <div className="space-y-2">
                  {categoria.servicos.map((s) => {
                    const selecionado = servicoIds.includes(s.id);
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
                            {s.duracaoMinutos} min · {formatarReais(s.preco)}
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

        {etapa === 2 && (
          <div>
            <h2 className="mb-3 text-lg font-semibold text-gray-900 dark:text-neutral-50">Data e horário</h2>

            {profissionais.length > 1 && (
              <label className="mb-3 block">
                <span className="mb-1 block text-sm text-gray-600 dark:text-neutral-400">Profissional</span>
                <select
                  value={profissionalId}
                  onChange={(e) => {
                    setProfissionalId(e.target.value);
                    setHorarioEscolhido(null);
                  }}
                  className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm dark:border-neutral-700 dark:bg-neutral-900"
                >
                  <option value="">Qualquer profissional</option>
                  {profissionaisElegiveis.map((p) => (
                    <option key={p.id} value={p.id}>
                      {p.nome}
                    </option>
                  ))}
                </select>
              </label>
            )}

            <div className="mb-3 flex gap-2 overflow-x-auto pb-1">
              {proximosDias(14).map((dia) => {
                const selecionado = dia.toDateString() === dataEscolhida.toDateString();
                return (
                  <button
                    key={dia.toISOString()}
                    onClick={() => {
                      setDataEscolhida(dia);
                      setHorarioEscolhido(null);
                    }}
                    className={`flex shrink-0 flex-col items-center rounded-lg border px-3 py-2 text-xs ${
                      selecionado ? "border-(--cor-primaria) bg-blue-50 dark:bg-blue-950" : "border-gray-200 dark:border-neutral-800"
                    }`}
                  >
                    <span>{dia.toLocaleDateString("pt-BR", { weekday: "short" })}</span>
                    <span className="font-semibold">{dia.getDate()}</span>
                  </button>
                );
              })}
            </div>

            {horariosLivres === null ? (
              <p className="text-sm text-gray-500">Carregando horários...</p>
            ) : horariosLivres.length === 0 ? (
              <p className="text-sm text-gray-500">Nenhum horário livre neste dia. Escolha outra data.</p>
            ) : (
              <div className="grid grid-cols-3 gap-2">
                {horariosLivres.map((h) => (
                  <button
                    key={h.inicio}
                    onClick={() => setHorarioEscolhido(h)}
                    className={`rounded-lg border px-2 py-2 text-sm ${
                      horarioEscolhido?.inicio === h.inicio && horarioEscolhido.profissionalId === h.profissionalId
                        ? "border-(--cor-primaria) bg-blue-50 dark:bg-blue-950"
                        : "border-gray-200 dark:border-neutral-800"
                    }`}
                  >
                    {formatarHora(h.inicio)}
                  </button>
                ))}
              </div>
            )}
          </div>
        )}

        {etapa === 3 && (
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
              disabled={codigoEnviado}
              className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm disabled:opacity-60 dark:border-neutral-700 dark:bg-neutral-900"
            />
            <input
              placeholder="Telefone (+55...)"
              value={telefone}
              onChange={(e) => setTelefone(e.target.value)}
              disabled={codigoEnviado}
              className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm disabled:opacity-60 dark:border-neutral-700 dark:bg-neutral-900"
            />
            <input
              placeholder="E-mail"
              type="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              disabled={codigoEnviado}
              className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm disabled:opacity-60 dark:border-neutral-700 dark:bg-neutral-900"
            />

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
                  Digite o código de 6 dígitos enviado por WhatsApp e e-mail.
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
                    onClick={() => {
                      setCodigoEnviado(false);
                      setCodigo("");
                    }}
                    className="text-gray-500 hover:underline"
                  >
                    Mudar dados
                  </button>
                  <button onClick={enviarCodigo} disabled={reenviarEm > 0} className="text-(--cor-primaria) disabled:opacity-50">
                    {reenviarEm > 0 ? `Reenviar em ${reenviarEm}s` : "Reenviar código"}
                  </button>
                </div>
              </div>
            )}
          </div>
        )}

        {etapa === 4 && horarioEscolhido && (
          <div className="space-y-4">
            <h2 className="text-lg font-semibold text-gray-900 dark:text-neutral-50">Resumo</h2>

            <div>
              <h3 className="text-xs font-semibold uppercase text-gray-500">Quando</h3>
              <p className="text-sm text-gray-800 dark:text-neutral-200">
                {new Date(horarioEscolhido.inicio).toLocaleDateString("pt-BR", { weekday: "long", day: "2-digit", month: "long" })} às{" "}
                {formatarHora(horarioEscolhido.inicio)}
              </p>
            </div>

            <div>
              <h3 className="text-xs font-semibold uppercase text-gray-500">Onde</h3>
              <p className="text-sm text-gray-800 dark:text-neutral-200">{[negocio.bairro, negocio.cidade].filter(Boolean).join(", ")}</p>
            </div>

            <div>
              <h3 className="text-xs font-semibold uppercase text-gray-500">Serviços</h3>
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
              {new Date(resumoFinal.inicio).toLocaleDateString("pt-BR", { weekday: "long", day: "2-digit", month: "long" })} às{" "}
              {formatarHora(resumoFinal.inicio)}
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
              {formatarReais((etapa === 4 ? totalComDesconto : totalServicos))}
            </span>
          </div>
          <button
            disabled={
              (etapa === 1 && !podeContinuarEtapa1) ||
              (etapa === 2 && !podeContinuarEtapa2) ||
              (etapa === 3 && !podeConfirmar) ||
              (etapa === 4 && confirmando)
            }
            onClick={() => {
              if (etapa === 1) setEtapa(2);
              else if (etapa === 2) avancarParaResumo();
              else if (etapa === 3) setEtapa(4);
              else if (etapa === 4) confirmarAgendamento();
            }}
            className="w-full rounded-lg bg-(--cor-primaria) px-4 py-3 text-sm font-semibold text-white disabled:opacity-50"
          >
            {textoBotaoContinuar(etapa, podeContinuarEtapa1, podeContinuarEtapa2, podeConfirmar, confirmando)}
          </button>
        </div>
      )}
    </div>
  );
}

function textoBotaoContinuar(
  etapa: Etapa,
  podeContinuarEtapa1: boolean,
  podeContinuarEtapa2: boolean,
  podeConfirmar: boolean,
  confirmando: boolean,
): string {
  if (etapa === 1) return podeContinuarEtapa1 ? "Continuar" : "Selecione um serviço";
  if (etapa === 2) return podeContinuarEtapa2 ? "Continuar" : "Selecione um horário";
  if (etapa === 3) return podeConfirmar ? "Continuar" : "Valide o código";
  if (etapa === 4) return confirmando ? "Confirmando..." : "Confirmar Agendamento";
  return "Continuar";
}
