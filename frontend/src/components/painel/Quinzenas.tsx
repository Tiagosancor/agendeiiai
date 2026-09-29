"use client";

import { useCallback, useEffect, useState, type FormEvent } from "react";
import { useAutenticacao, ErroApi } from "@/lib/auth-context";
import { classeBotaoPerigo, classeBotaoPrimario, classeBotaoSecundario, classeCartao, classeInput, classeLabel, classeTd, classeTh } from "@/components/estilos";
import type { AtendimentoPendente, DetalheQuinzena, QuinzenaResumo, QuinzenasDoProfissional } from "@/lib/tipos";
import { formatarReais } from "@/lib/formatacao";

/** "2026-09-01" (DateOnly da API) → "01/09/2026", sem passar por Date (evita virar o dia pelo fuso). */
export function formatarDia(iso: string): string {
  const [ano, mes, dia] = iso.split("-");
  return `${dia}/${mes}/${ano}`;
}

const formatarDataHora = (iso: string) =>
  new Date(iso).toLocaleString("pt-BR", { day: "2-digit", month: "2-digit", hour: "2-digit", minute: "2-digit" });

function SeloEstado({ estado }: { estado: string }) {
  const aberta = estado === "Aberta";
  return (
    <span
      className={`rounded-full px-2 py-0.5 text-xs font-medium ${
        aberta ? "bg-amber-50 text-amber-800 dark:bg-amber-950 dark:text-amber-300" : "bg-green-50 text-green-700 dark:bg-green-950 dark:text-green-300"
      }`}
    >
      {aberta ? "Aberta" : "Fechada"}
    </span>
  );
}

/** Em "Minhas comissões": a quinzena aberta (parcial) e as fechadas (valor final), só do próprio profissional. */
export function MinhasQuinzenas() {
  const { chamarApi } = useAutenticacao();
  const [dados, setDados] = useState<QuinzenasDoProfissional | null>(null);

  useEffect(() => {
    // Busca disparada pela montagem.
    chamarApi<QuinzenasDoProfissional>("/painel/comissoes/minhas/quinzenas").then(setDados).catch(() => setDados(null));
  }, [chamarApi]);

  if (!dados?.acertoPorQuinzena || dados.quinzenas.length === 0) return null;

  return (
    <section className="space-y-2">
      <h2 className="text-base font-semibold text-gray-900 dark:text-neutral-50">Minhas quinzenas</h2>
      <ul className="space-y-2">
        {dados.quinzenas.map((q) => (
          <li key={q.periodoId} className={`${classeCartao} flex items-center justify-between gap-3 p-3`}>
            <div>
              <div className="flex items-center gap-2 text-sm font-medium text-gray-900 dark:text-neutral-50">
                {formatarDia(q.inicio)} a {formatarDia(q.fim)} <SeloEstado estado={q.estado} />
              </div>
              <div className="text-xs text-gray-500 dark:text-neutral-400">
                {q.parcial ? "Valor parcial, ainda pode mudar" : "Valor final, a receber"}
                {q.totais.quantidadeServicos > 0 && ` · ${q.totais.quantidadeServicos} serviço(s)`}
              </div>
              <div className="text-xs text-gray-500 dark:text-neutral-400">
                {q.totais.totalComissao > 0 || q.comissaoProdutos === 0
                  ? `Serviços ${formatarReais(q.totais.totalComissao)}${q.comissaoProdutos > 0 ? ` + produtos ${formatarReais(q.comissaoProdutos)}` : ""}`
                  : `Produtos ${formatarReais(q.comissaoProdutos)}`}
                {q.vales + q.consumo > 0 && ` − vale/consumo ${formatarReais(q.vales + q.consumo)}`}
              </div>
              {q.saldoRestante > 0 && (
                <div className="text-xs text-amber-700 dark:text-amber-400">
                  Restam {formatarReais(q.saldoRestante)} de vale/consumo para a próxima quinzena.
                </div>
              )}
            </div>
            <span className="text-lg font-semibold text-gray-900 dark:text-neutral-50" aria-label="A receber">
              {formatarReais(q.liquido)}
            </span>
          </li>
        ))}
      </ul>
    </section>
  );
}

/** Aba "Quinzenas" do Administrador (seção 7): períodos, fechamento e reabertura. */
export function VisaoQuinzenas() {
  const { chamarApi, temPermissao } = useAutenticacao();
  const podeGerenciar = temPermissao("GerenciarComissoes");
  const [lista, setLista] = useState<QuinzenaResumo[] | null>(null);
  const [selecionada, setSelecionada] = useState<string | null>(null);
  const [erro, setErro] = useState<string | null>(null);

  const carregar = useCallback(async () => {
    try {
      setErro(null);
      setLista(await chamarApi<QuinzenaResumo[]>("/painel/quinzenas"));
    } catch {
      setErro("Não foi possível carregar as quinzenas.");
    }
  }, [chamarApi]);

  useEffect(() => {
    // Busca disparada pela montagem.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    carregar();
  }, [carregar]);

  if (selecionada) {
    return (
      <DetalheDaQuinzena
        id={selecionada}
        podeGerenciar={podeGerenciar}
        aoVoltar={() => {
          setSelecionada(null);
          carregar();
        }}
      />
    );
  }

  return (
    <div className="space-y-4">
      {podeGerenciar && <NovaQuinzena aoCriar={carregar} />}
      {erro && <p className="text-sm text-red-600 dark:text-red-400">{erro}</p>}
      {lista?.length === 0 && <p className="text-sm text-gray-500 dark:text-neutral-400">Nenhuma quinzena cadastrada ainda.</p>}
      <ul className="space-y-2">
        {lista?.map((q) => (
          <li key={q.id}>
            <button
              type="button"
              onClick={() => setSelecionada(q.id)}
              className={`${classeCartao} flex w-full items-center justify-between gap-3 p-3 text-left hover:bg-gray-50 dark:hover:bg-neutral-800/60`}
            >
              <span>
                <span className="flex items-center gap-2 font-medium text-gray-900 dark:text-neutral-50">
                  {formatarDia(q.inicio)} a {formatarDia(q.fim)} <SeloEstado estado={q.estado} />
                </span>
                {q.fechadoEm && (
                  <span className="block text-xs text-gray-500 dark:text-neutral-400">
                    Fechada em {new Date(q.fechadoEm).toLocaleDateString("pt-BR")}
                    {q.fechadoPor && ` por ${q.fechadoPor}`}
                  </span>
                )}
                {q.diasSemPeriodoAntes > 0 && (
                  <span className="block text-xs text-amber-700 dark:text-amber-400">
                    {q.diasSemPeriodoAntes} dia(s) sem quinzena antes desta
                  </span>
                )}
              </span>
              <span className="text-sm text-marca-primaria dark:text-marca-acento">Ver</span>
            </button>
          </li>
        ))}
      </ul>
    </div>
  );
}

function NovaQuinzena({ aoCriar }: { aoCriar: () => void }) {
  const { chamarApi } = useAutenticacao();
  const [inicio, setInicio] = useState("");
  const [fim, setFim] = useState("");
  const [mensagem, setMensagem] = useState<{ tipo: "ok" | "erro" | "aviso"; texto: string } | null>(null);

  // Sugestão da seguinte à última, editável (seção 7). Na abertura só preenche campo ainda vazio:
  // a resposta pode chegar depois de a pessoa já ter digitado, e não pode apagar o que ela escreveu.
  const sugerir = useCallback(
    async (sobrescrever: boolean) => {
      const sugestao = await chamarApi<{ inicio: string; fim: string }>("/painel/quinzenas/sugestao");
      setInicio((atual) => (sobrescrever || !atual ? sugestao.inicio : atual));
      setFim((atual) => (sobrescrever || !atual ? sugestao.fim : atual));
    },
    [chamarApi],
  );

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    sugerir(false).catch(() => undefined);
  }, [sugerir]);

  async function criar(evento: FormEvent) {
    evento.preventDefault();
    setMensagem(null);
    try {
      const resposta = await chamarApi<{ id: string; aviso: string | null }>("/painel/quinzenas", { metodo: "POST", corpo: { inicio, fim } });
      setMensagem(resposta.aviso ? { tipo: "aviso", texto: `Quinzena criada. ${resposta.aviso}` } : { tipo: "ok", texto: "Quinzena criada." });
      aoCriar();
      await sugerir(true);
    } catch (excecao) {
      setMensagem({ tipo: "erro", texto: excecao instanceof ErroApi ? excecao.message : "Não foi possível criar a quinzena." });
    }
  }

  const cor = { ok: "text-green-700 dark:text-green-400", erro: "text-red-600 dark:text-red-400", aviso: "text-amber-700 dark:text-amber-400" };

  return (
    <form onSubmit={criar} className={`${classeCartao} space-y-3 p-4`}>
      <h2 className="text-sm font-semibold text-gray-900 dark:text-neutral-50">Nova quinzena</h2>
      <div className="grid grid-cols-2 gap-3 sm:flex sm:items-end">
        <label>
          <span className={classeLabel}>Início</span>
          <input type="date" required className={classeInput} value={inicio} onChange={(e) => setInicio(e.target.value)} />
        </label>
        <label>
          <span className={classeLabel}>Fim</span>
          <input type="date" required className={classeInput} value={fim} min={inicio} onChange={(e) => setFim(e.target.value)} />
        </label>
        <button type="submit" className={`${classeBotaoPrimario} col-span-2`}>
          Criar quinzena
        </button>
      </div>
      {mensagem && <p className={`text-sm ${cor[mensagem.tipo]}`}>{mensagem.texto}</p>}
    </form>
  );
}

function DetalheDaQuinzena({ id, podeGerenciar, aoVoltar }: { id: string; podeGerenciar: boolean; aoVoltar: () => void }) {
  const { chamarApi } = useAutenticacao();
  const [detalhe, setDetalhe] = useState<DetalheQuinzena | null>(null);
  const [pendentesParaConfirmar, setPendentesParaConfirmar] = useState<AtendimentoPendente[] | null>(null);
  const [reabrindo, setReabrindo] = useState(false);
  const [motivo, setMotivo] = useState("");
  const [editando, setEditando] = useState(false);
  const [datas, setDatas] = useState({ inicio: "", fim: "" });
  const [erro, setErro] = useState<string | null>(null);
  const [ocupado, setOcupado] = useState(false);

  const carregar = useCallback(async () => {
    const dados = await chamarApi<DetalheQuinzena>(`/painel/quinzenas/${id}`);
    setDetalhe(dados);
    setDatas({ inicio: dados.quinzena.inicio, fim: dados.quinzena.fim });
  }, [chamarApi, id]);

  useEffect(() => {
    // Busca disparada pela montagem.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    carregar().catch(() => setErro("Não foi possível carregar a quinzena."));
  }, [carregar]);

  async function executar(acao: () => Promise<unknown>) {
    setErro(null);
    setOcupado(true);
    try {
      await acao();
      await carregar();
      return true;
    } catch (excecao) {
      setErro(excecao instanceof ErroApi ? excecao.message : "Não foi possível concluir a ação.");
      return false;
    } finally {
      setOcupado(false);
    }
  }

  async function fechar(confirmarPendentes: boolean) {
    setErro(null);
    setOcupado(true);
    try {
      await chamarApi(`/painel/quinzenas/${id}/fechar`, { metodo: "POST", corpo: { confirmarPendentes } });
      setPendentesParaConfirmar(null);
      await carregar();
    } catch (excecao) {
      if (excecao instanceof ErroApi && excecao.codigo === "pendentes") {
        setPendentesParaConfirmar((excecao.corpo?.pendentes as AtendimentoPendente[]) ?? []);
      } else {
        setErro(excecao instanceof ErroApi ? excecao.message : "Não foi possível fechar a quinzena.");
      }
    } finally {
      setOcupado(false);
    }
  }

  if (!detalhe) {
    return erro ? <p className="text-sm text-red-600 dark:text-red-400">{erro}</p> : <p className="text-sm text-gray-500">Carregando...</p>;
  }

  const { quinzena } = detalhe;
  const aberta = quinzena.estado === "Aberta";
  const soma = (valor: (l: DetalheQuinzena["linhas"][number]) => number) => detalhe.linhas.reduce((total, l) => total + valor(l), 0);

  return (
    <div className="space-y-4">
      <button type="button" className={classeBotaoSecundario} onClick={aoVoltar}>
        ← Voltar para as quinzenas
      </button>

      <div className="flex flex-wrap items-center gap-2">
        <h2 className="text-base font-semibold text-gray-900 dark:text-neutral-50">
          {formatarDia(quinzena.inicio)} a {formatarDia(quinzena.fim)}
        </h2>
        <SeloEstado estado={quinzena.estado} />
      </div>
      {detalhe.parcial ? (
        <p className="rounded-lg bg-amber-50 px-3 py-2 text-sm text-amber-800 dark:bg-amber-950 dark:text-amber-300">
          Valores parciais: ainda podem mudar conforme os atendimentos forem concluídos.
        </p>
      ) : (
        <p className="text-sm text-gray-600 dark:text-neutral-300">
          Fechada{quinzena.fechadoEm && ` em ${new Date(quinzena.fechadoEm).toLocaleString("pt-BR")}`}
          {quinzena.fechadoPor && ` por ${quinzena.fechadoPor}`}. Estes valores não mudam mais; para corrigir algo, reabra a quinzena.
        </p>
      )}

      {detalhe.linhas.length === 0 ? (
        <p className="text-sm text-gray-500 dark:text-neutral-400">
          Ninguém com &quot;acerto por quinzena&quot;. Ligue a opção na ficha de cada profissional (ou, para quem vende sem cadastro
          de profissional, na comissão de produto do usuário).
        </p>
      ) : (
        <div className={classeCartao}>
          <table className="w-full">
            <thead className="bg-gray-50 dark:bg-neutral-800/50">
              <tr>
                <th className={classeTh}>Profissional</th>
                <th className={`${classeTh} text-right`}>Serviços</th>
                <th className={`${classeTh} hidden text-right sm:table-cell`}>Atendido</th>
                <th className={`${classeTh} text-right`}>Comissão</th>
                <th className={`${classeTh} hidden text-right sm:table-cell`}>Produtos</th>
                <th className={`${classeTh} hidden text-right sm:table-cell`}>Vale/consumo</th>
                <th className={`${classeTh} text-right`}>A receber</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100 dark:divide-neutral-800">
              {detalhe.linhas.map((l) => (
                <tr key={l.profissionalId ?? `u:${l.usuarioId}`}>
                  <td className={classeTd}>
                    {l.nome}
                    {!l.profissionalId && <span className="block text-xs text-gray-500 dark:text-neutral-400">vendedor</span>}
                  </td>
                  <td className={`${classeTd} text-right`}>{l.totais.quantidadeServicos}</td>
                  <td className={`${classeTd} hidden text-right sm:table-cell`}>{formatarReais(l.totais.totalAtendido)}</td>
                  <td className={`${classeTd} text-right`}>{formatarReais(l.totais.totalComissao)}</td>
                  <td className={`${classeTd} hidden text-right sm:table-cell`}>{formatarReais(l.comissaoProdutos)}</td>
                  <td className={`${classeTd} hidden text-right sm:table-cell`}>
                    {l.vales + l.consumo > 0 ? `− ${formatarReais(l.vales + l.consumo)}` : "—"}
                  </td>
                  <td className={`${classeTd} text-right font-medium`}>
                    {formatarReais(l.liquido)}
                    {l.saldoRestante > 0 && (
                      <span className="block text-xs font-normal text-amber-700 dark:text-amber-400">
                        restam {formatarReais(l.saldoRestante)} para a próxima
                      </span>
                    )}
                  </td>
                </tr>
              ))}
              <tr>
                <td className={`${classeTd} font-semibold`} colSpan={2}>
                  Total
                </td>
                <td className={`${classeTd} hidden sm:table-cell`} />
                <td className={`${classeTd} text-right font-semibold`}>{formatarReais(soma((l) => l.totais.totalComissao))}</td>
                <td className={`${classeTd} hidden text-right font-semibold sm:table-cell`}>{formatarReais(soma((l) => l.comissaoProdutos))}</td>
                <td className={`${classeTd} hidden text-right font-semibold sm:table-cell`}>{formatarReais(soma((l) => l.vales + l.consumo))}</td>
                <td className={`${classeTd} text-right font-semibold`}>{formatarReais(soma((l) => l.liquido))}</td>
              </tr>
            </tbody>
          </table>
        </div>
      )}

      {aberta && detalhe.pendentes.length > 0 && !pendentesParaConfirmar && (
        <p className="text-sm text-amber-700 dark:text-amber-400">
          {detalhe.pendentes.length} atendimento(s) desta quinzena ainda sem conclusão.
        </p>
      )}

      {pendentesParaConfirmar && (
        <div className="space-y-2 rounded-lg border border-amber-300 p-3 dark:border-amber-800">
          <p className="text-sm font-medium text-amber-800 dark:text-amber-300">
            Estes atendimentos ainda não foram concluídos. Se fechar agora, eles ficam fora do fechamento e travados até a quinzena ser reaberta:
          </p>
          <ul className="text-sm text-gray-700 dark:text-neutral-300">
            {pendentesParaConfirmar.map((p) => (
              <li key={p.agendamentoId}>
                {formatarDataHora(p.inicio)} · {p.profissional} · {p.status}
              </li>
            ))}
          </ul>
          <div className="flex gap-2">
            <button type="button" className={classeBotaoPrimario} disabled={ocupado} onClick={() => fechar(true)}>
              Fechar mesmo assim
            </button>
            <button type="button" className={classeBotaoSecundario} onClick={() => setPendentesParaConfirmar(null)}>
              Voltar
            </button>
          </div>
        </div>
      )}

      {erro && <p role="alert" className="text-sm text-red-600 dark:text-red-400">{erro}</p>}

      {podeGerenciar && aberta && !pendentesParaConfirmar && (
        <div className="flex flex-wrap gap-2">
          <button type="button" className={classeBotaoPrimario} disabled={ocupado} onClick={() => fechar(false)}>
            Fechar quinzena
          </button>
          <button type="button" className={classeBotaoSecundario} onClick={() => setEditando((v) => !v)}>
            Editar datas
          </button>
          <button
            type="button"
            className={classeBotaoPerigo}
            disabled={ocupado}
            onClick={async () => {
              if (!confirm("Excluir esta quinzena? Os atendimentos continuam; só o período some.")) return;
              try {
                await chamarApi(`/painel/quinzenas/${id}`, { metodo: "DELETE" });
                aoVoltar();
              } catch (excecao) {
                setErro(excecao instanceof ErroApi ? excecao.message : "Não foi possível excluir a quinzena.");
              }
            }}
          >
            Excluir
          </button>
        </div>
      )}

      {podeGerenciar && aberta && editando && (
        <form
          className="flex flex-wrap items-end gap-2"
          onSubmit={async (e) => {
            e.preventDefault();
            if (await executar(() => chamarApi(`/painel/quinzenas/${id}`, { metodo: "PUT", corpo: datas }))) setEditando(false);
          }}
        >
          <label>
            <span className={classeLabel}>Início</span>
            <input type="date" required className={classeInput} value={datas.inicio} onChange={(e) => setDatas({ ...datas, inicio: e.target.value })} />
          </label>
          <label>
            <span className={classeLabel}>Fim</span>
            <input type="date" required className={classeInput} value={datas.fim} onChange={(e) => setDatas({ ...datas, fim: e.target.value })} />
          </label>
          <button type="submit" className={classeBotaoPrimario} disabled={ocupado}>
            Salvar datas
          </button>
        </form>
      )}

      {podeGerenciar && !aberta && !reabrindo && (
        <button type="button" className={classeBotaoSecundario} onClick={() => setReabrindo(true)}>
          Reabrir quinzena
        </button>
      )}

      {podeGerenciar && !aberta && reabrindo && (
        <form
          className="space-y-2"
          onSubmit={async (e) => {
            e.preventDefault();
            if (!confirm("Reabrir apaga os valores fechados desta quinzena. Continuar?")) return;
            if (await executar(() => chamarApi(`/painel/quinzenas/${id}/reabrir`, { metodo: "POST", corpo: { motivo } }))) {
              setReabrindo(false);
              setMotivo("");
            }
          }}
        >
          <label className="block">
            <span className={classeLabel}>Motivo da reabertura (obrigatório)</span>
            <textarea required maxLength={500} rows={2} className={classeInput} value={motivo} onChange={(e) => setMotivo(e.target.value)} />
          </label>
          <div className="flex gap-2">
            <button type="submit" className={classeBotaoPrimario} disabled={ocupado || !motivo.trim()}>
              Reabrir
            </button>
            <button type="button" className={classeBotaoSecundario} onClick={() => setReabrindo(false)}>
              Cancelar
            </button>
          </div>
        </form>
      )}
    </div>
  );
}
