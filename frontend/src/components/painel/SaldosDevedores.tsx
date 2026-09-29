"use client";

import { useCallback, useEffect, useState, type FormEvent } from "react";
import { useAutenticacao, ErroApi } from "@/lib/auth-context";
import { Modal } from "@/components/Modal";
import { classeBotaoPrimario, classeBotaoSecundario, classeCartao, classeInput, classeLabel } from "@/components/estilos";
import { formatarDia } from "@/components/painel/Quinzenas";
import type { LancamentoSaldoResumo, SaldoDevedor, SaldoDoProfissional } from "@/lib/tipos";
import { dataLocalIso, formatarReais } from "@/lib/formatacao";

const ROTULO_TIPO = { Vale: "Vale", ConsumoInterno: "Consumo interno" } as const;

/** Um lançamento: vale (motivo) ou consumo (produto × quantidade), com o que ainda está em aberto. */
export function ItemLancamento({ lancamento, acoes }: { lancamento: LancamentoSaldoResumo; acoes?: React.ReactNode }) {
  const quitado = lancamento.aberto < lancamento.valor;
  return (
    <li className={`${classeCartao} flex flex-wrap items-start justify-between gap-2 p-3`}>
      <span>
        <span className="block text-sm font-medium text-gray-900 dark:text-neutral-50">
          {ROTULO_TIPO[lancamento.tipo]}
          {lancamento.produto && ` · ${lancamento.produto} × ${lancamento.quantidade}`}
        </span>
        <span className="block text-xs text-gray-500 dark:text-neutral-400">
          {formatarDia(lancamento.data)}
          {lancamento.descricao && ` · ${lancamento.descricao}`}
          {lancamento.lancadoPor && ` · lançado por ${lancamento.lancadoPor}`}
        </span>
        {quitado && (
          <span className="block text-xs text-green-700 dark:text-green-400">
            {lancamento.aberto === 0 ? "Quitado no fechamento" : `Em aberto: ${formatarReais(lancamento.aberto)}`}
          </span>
        )}
        {acoes}
      </span>
      <span className="font-semibold text-gray-900 dark:text-neutral-50">{formatarReais(lancamento.valor)}</span>
    </li>
  );
}

/**
 * Aba "Vales e consumo" (seção 7): o saldo devedor de cada profissional, o lançamento de vales e, no detalhe, editar e
 * excluir — lançamento já descontado num fechamento pede confirmação, porque muda aquele fechamento.
 */
export function VisaoSaldosDevedores() {
  const { chamarApi, temPermissao } = useAutenticacao();
  const podeLancarVale = temPermissao("LancarVales");
  const [lista, setLista] = useState<SaldoDoProfissional[] | null>(null);
  const [selecionado, setSelecionado] = useState<string | null>(null);
  const [valeAberto, setValeAberto] = useState(false);
  const [erro, setErro] = useState<string | null>(null);

  const carregar = useCallback(async () => {
    try {
      setErro(null);
      setLista(await chamarApi<SaldoDoProfissional[]>("/painel/saldos"));
    } catch {
      setErro("Não foi possível carregar os saldos.");
    }
  }, [chamarApi]);

  useEffect(() => {
    // Busca disparada pela montagem.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    carregar();
  }, [carregar]);

  if (selecionado) {
    return (
      <DetalheSaldo
        profissionalId={selecionado}
        aoVoltar={() => {
          setSelecionado(null);
          carregar();
        }}
      />
    );
  }

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="text-sm text-gray-600 dark:text-neutral-300">
          Vales e consumo interno descontam do que o profissional tem a receber, nunca do faturamento. Consumo se lança em Estoque.
        </p>
        {podeLancarVale && (
          <button type="button" className={classeBotaoPrimario} onClick={() => setValeAberto(true)}>
            Lançar vale
          </button>
        )}
      </div>
      {erro && <p className="text-sm text-red-600 dark:text-red-400">{erro}</p>}
      <ul className="space-y-2">
        {lista?.map((s) => (
          <li key={s.profissionalId}>
            <button
              type="button"
              onClick={() => setSelecionado(s.profissionalId)}
              className={`${classeCartao} flex w-full items-center justify-between gap-3 p-3 text-left hover:bg-gray-50 dark:hover:bg-neutral-800/60`}
            >
              <span>
                <span className="block font-medium text-gray-900 dark:text-neutral-50">
                  {s.nome}
                  {!s.ativo && <span className="ml-2 text-xs font-normal text-gray-500 dark:text-neutral-400">(inativo)</span>}
                </span>
                <span className="block text-xs text-gray-500 dark:text-neutral-400">
                  Vales {formatarReais(s.valesEmAberto)} · consumo {formatarReais(s.consumoEmAberto)}
                </span>
              </span>
              <span className="font-semibold text-gray-900 dark:text-neutral-50">{formatarReais(s.totalEmAberto)}</span>
            </button>
          </li>
        ))}
      </ul>

      {valeAberto && lista && (
        <ModalVale
          profissionais={lista.filter((s) => s.ativo)}
          aoFechar={async (lancou) => {
            setValeAberto(false);
            if (lancou) await carregar();
          }}
        />
      )}
    </div>
  );
}

function ModalVale({ profissionais, aoFechar }: { profissionais: SaldoDoProfissional[]; aoFechar: (lancou: boolean) => void }) {
  const { chamarApi } = useAutenticacao();
  const [profissionalId, setProfissionalId] = useState("");
  const [valor, setValor] = useState("");
  const [data, setData] = useState(dataLocalIso());
  const [motivo, setMotivo] = useState("");
  const [erro, setErro] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);

  async function lancar(evento: FormEvent) {
    evento.preventDefault();
    setErro(null);
    setEnviando(true);
    try {
      await chamarApi("/painel/saldos/vales", { metodo: "POST", corpo: { profissionalId, valor: Number(valor), data, motivo: motivo || null } });
      aoFechar(true);
    } catch (excecao) {
      setErro(excecao instanceof ErroApi ? excecao.message : "Não foi possível lançar o vale.");
    } finally {
      setEnviando(false);
    }
  }

  return (
    <Modal titulo="Lançar vale" aberto aoFechar={() => aoFechar(false)}>
      <form onSubmit={lancar} className="space-y-3">
        <label className="block">
          <span className={classeLabel}>Profissional</span>
          <select required className={classeInput} value={profissionalId} onChange={(e) => setProfissionalId(e.target.value)}>
            <option value="">Escolha...</option>
            {profissionais.map((p) => (
              <option key={p.profissionalId} value={p.profissionalId}>
                {p.nome}
              </option>
            ))}
          </select>
        </label>
        <div className="grid grid-cols-2 gap-3">
          <label className="block">
            <span className={classeLabel}>Valor (R$)</span>
            <input required type="number" min={0.01} step="0.01" className={classeInput} value={valor} onChange={(e) => setValor(e.target.value)} />
          </label>
          <label className="block">
            <span className={classeLabel}>Data</span>
            <input required type="date" className={classeInput} value={data} onChange={(e) => setData(e.target.value)} />
          </label>
        </div>
        <label className="block">
          <span className={classeLabel}>Motivo (opcional)</span>
          <input maxLength={500} className={classeInput} value={motivo} onChange={(e) => setMotivo(e.target.value)} />
        </label>
        {erro && <p className="text-sm text-red-600">{erro}</p>}
        <div className="flex justify-end gap-2 pt-2">
          <button type="button" className={classeBotaoSecundario} onClick={() => aoFechar(false)}>
            Cancelar
          </button>
          <button type="submit" disabled={enviando} className={classeBotaoPrimario}>
            {enviando ? "Lançando..." : "Lançar vale"}
          </button>
        </div>
      </form>
    </Modal>
  );
}

function DetalheSaldo({ profissionalId, aoVoltar }: { profissionalId: string; aoVoltar: () => void }) {
  const { chamarApi, temPermissao } = useAutenticacao();
  const [saldo, setSaldo] = useState<SaldoDevedor | null>(null);
  const [editando, setEditando] = useState<LancamentoSaldoResumo | null>(null);
  const [erro, setErro] = useState<string | null>(null);

  const carregar = useCallback(async () => {
    setSaldo(await chamarApi<SaldoDevedor>(`/painel/saldos/profissionais/${profissionalId}`));
  }, [chamarApi, profissionalId]);

  useEffect(() => {
    // Busca disparada pela montagem.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    carregar().catch(() => setErro("Não foi possível carregar o saldo."));
  }, [carregar]);

  const podeMexer = (l: LancamentoSaldoResumo) => temPermissao(l.tipo === "Vale" ? "LancarVales" : "GerenciarEstoque");

  async function excluir(l: LancamentoSaldoResumo) {
    const aviso = l.tipo === "ConsumoInterno" ? " O produto volta ao estoque." : "";
    if (!confirm(`Excluir este ${ROTULO_TIPO[l.tipo].toLowerCase()} de ${formatarReais(l.valor)}?${aviso}`)) return;
    setErro(null);
    try {
      await chamarApi(`/painel/saldos/${l.id}`, { metodo: "DELETE" });
    } catch (excecao) {
      if (excecao instanceof ErroApi && excecao.codigo === "quitado") {
        if (!confirm(excecao.message)) return;
        await chamarApi(`/painel/saldos/${l.id}?confirmarQuitado=true`, { metodo: "DELETE" });
      } else {
        setErro(excecao instanceof ErroApi ? excecao.message : "Não foi possível excluir.");
        return;
      }
    }
    await carregar();
  }

  if (!saldo) return erro ? <p className="text-sm text-red-600">{erro}</p> : <p className="text-sm text-gray-500">Carregando...</p>;

  return (
    <div className="space-y-4">
      <button type="button" className={classeBotaoSecundario} onClick={aoVoltar}>
        ← Voltar para os saldos
      </button>
      <h2 className="text-base font-semibold text-gray-900 dark:text-neutral-50">{saldo.nome}</h2>
      <CartoesSaldo saldo={saldo} />
      {erro && <p role="alert" className="text-sm text-red-600">{erro}</p>}
      {saldo.lancamentos.length === 0 ? (
        <p className="text-sm text-gray-500 dark:text-neutral-400">Nenhum vale ou consumo lançado.</p>
      ) : (
        <ul className="space-y-2">
          {saldo.lancamentos.map((l) => (
            <ItemLancamento
              key={l.id}
              lancamento={l}
              acoes={
                podeMexer(l) && (
                  <span className="mt-1 flex gap-3 text-xs">
                    <button type="button" className="text-marca-primaria hover:underline dark:text-marca-acento" onClick={() => setEditando(l)}>
                      Editar
                    </button>
                    <button type="button" className="text-red-600 hover:underline dark:text-red-400" onClick={() => excluir(l)}>
                      Excluir
                    </button>
                  </span>
                )
              }
            />
          ))}
        </ul>
      )}
      {editando && (
        <ModalEditarLancamento
          lancamento={editando}
          aoFechar={async (mudou) => {
            setEditando(null);
            if (mudou) await carregar();
          }}
        />
      )}
    </div>
  );
}

export function CartoesSaldo({ saldo }: { saldo: SaldoDevedor }) {
  return (
    <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
      {[
        ["Vales em aberto", saldo.valesEmAberto],
        ["Consumo em aberto", saldo.consumoEmAberto],
        ["Saldo devedor", saldo.totalEmAberto],
      ].map(([rotulo, valor]) => (
        <div key={rotulo as string} className={`${classeCartao} p-4`}>
          <div className="text-xs text-gray-500 dark:text-neutral-400">{rotulo}</div>
          <div className="mt-1 text-lg font-semibold text-gray-900 dark:text-neutral-50">{formatarReais(valor as number)}</div>
        </div>
      ))}
    </div>
  );
}

function ModalEditarLancamento({ lancamento, aoFechar }: { lancamento: LancamentoSaldoResumo; aoFechar: (mudou: boolean) => void }) {
  const { chamarApi } = useAutenticacao();
  const vale = lancamento.tipo === "Vale";
  const [valor, setValor] = useState(String(vale ? lancamento.valor : (lancamento.valorUnitario ?? 0)));
  const [data, setData] = useState(lancamento.data);
  const [descricao, setDescricao] = useState(lancamento.descricao ?? "");
  const [erro, setErro] = useState<string | null>(null);

  async function salvar(evento: FormEvent) {
    evento.preventDefault();
    setErro(null);
    const corpo = vale
      ? { valor: Number(valor), data, descricao: descricao || null }
      : { valorUnitario: Number(valor), descricao: descricao || null };
    try {
      await chamarApi(`/painel/saldos/${lancamento.id}`, { metodo: "PUT", corpo });
      aoFechar(true);
    } catch (excecao) {
      if (excecao instanceof ErroApi && excecao.codigo === "quitado") {
        if (!confirm(excecao.message)) return;
        try {
          await chamarApi(`/painel/saldos/${lancamento.id}`, { metodo: "PUT", corpo: { ...corpo, confirmarQuitado: true } });
          aoFechar(true);
        } catch (segunda) {
          setErro(segunda instanceof ErroApi ? segunda.message : "Não foi possível salvar.");
        }
      } else {
        setErro(excecao instanceof ErroApi ? excecao.message : "Não foi possível salvar.");
      }
    }
  }

  return (
    <Modal titulo={vale ? "Editar vale" : `Editar consumo — ${lancamento.produto}`} aberto aoFechar={() => aoFechar(false)}>
      <form onSubmit={salvar} className="space-y-3">
        <div className="grid grid-cols-2 gap-3">
          <label className="block">
            <span className={classeLabel}>{vale ? "Valor (R$)" : "Valor por unidade (R$)"}</span>
            <input required type="number" min={vale ? 0.01 : 0} step="0.01" className={classeInput} value={valor} onChange={(e) => setValor(e.target.value)} />
          </label>
          {vale && (
            <label className="block">
              <span className={classeLabel}>Data</span>
              <input required type="date" className={classeInput} value={data} onChange={(e) => setData(e.target.value)} />
            </label>
          )}
        </div>
        <label className="block">
          <span className={classeLabel}>{vale ? "Motivo (opcional)" : "Observação (opcional)"}</span>
          <input maxLength={500} className={classeInput} value={descricao} onChange={(e) => setDescricao(e.target.value)} />
        </label>
        {erro && <p className="text-sm text-red-600">{erro}</p>}
        <div className="flex justify-end gap-2 pt-2">
          <button type="button" className={classeBotaoSecundario} onClick={() => aoFechar(false)}>
            Cancelar
          </button>
          <button type="submit" className={classeBotaoPrimario}>
            Salvar
          </button>
        </div>
      </form>
    </Modal>
  );
}
