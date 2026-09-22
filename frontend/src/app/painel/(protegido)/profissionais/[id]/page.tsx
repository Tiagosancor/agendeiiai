"use client";

import { use, useCallback, useEffect, useState, type FormEvent } from "react";
import Link from "next/link";
import { useAutenticacao } from "@/lib/auth-context";
import { classeBotaoPrimario, classeBotaoSecundario, classeCartao, classeInput, classeLabel } from "@/components/estilos";
import {
  NOMES_DIAS_SEMANA,
  type BloqueioResumo,
  type IntervaloTrabalho,
  type ProfissionalServicoResumo,
  type ServicoResumo,
} from "@/lib/tipos";

export default function PaginaDetalheProfissional({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);

  return (
    <div className="space-y-8">
      <Link href="/painel/profissionais" className="text-sm text-blue-600 hover:underline dark:text-blue-400">
        ← Voltar para profissionais
      </Link>

      <SecaoHorariosTrabalho profissionalId={id} />
      <SecaoBloqueios profissionalId={id} />
      <SecaoServicosVinculados profissionalId={id} />
    </div>
  );
}

function SecaoHorariosTrabalho({ profissionalId }: { profissionalId: string }) {
  const { chamarApi } = useAutenticacao();
  const [intervalos, setIntervalos] = useState<IntervaloTrabalho[]>([]);
  const [salvando, setSalvando] = useState(false);
  const [mensagem, setMensagem] = useState<string | null>(null);

  const carregar = useCallback(async () => {
    setIntervalos(await chamarApi<IntervaloTrabalho[]>(`/painel/profissionais/${profissionalId}/horarios`));
  }, [chamarApi, profissionalId]);

  useEffect(() => {
    // Busca disparada pela montagem, não estado derivado de props.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    carregar();
  }, [carregar]);

  function adicionarIntervalo() {
    setIntervalos((atual) => [...atual, { diaSemana: 1, inicio: "09:00:00", fim: "18:00:00" }]);
  }

  function removerIntervalo(indice: number) {
    setIntervalos((atual) => atual.filter((_, i) => i !== indice));
  }

  function atualizarIntervalo(indice: number, alteracao: Partial<IntervaloTrabalho>) {
    setIntervalos((atual) => atual.map((intervalo, i) => (i === indice ? { ...intervalo, ...alteracao } : intervalo)));
  }

  async function salvar() {
    setSalvando(true);
    setMensagem(null);
    try {
      await chamarApi(`/painel/profissionais/${profissionalId}/horarios`, { metodo: "PUT", corpo: intervalos });
      setMensagem("Horário de trabalho salvo.");
    } catch {
      setMensagem("Não foi possível salvar.");
    } finally {
      setSalvando(false);
    }
  }

  return (
    <section>
      <h2 className="mb-3 text-lg font-semibold text-gray-900 dark:text-neutral-50">Horário de trabalho</h2>
      <p className="mb-3 text-sm text-gray-500 dark:text-neutral-400">
        Um dia com almoço vira dois intervalos (ex.: 09:00–12:00 e 13:00–18:00).
      </p>

      <div className={`${classeCartao} space-y-2 p-4`}>
        {intervalos.map((intervalo, indice) => (
          <div key={indice} className="flex flex-wrap items-center gap-2">
            <select
              className={`${classeInput} w-36`}
              value={intervalo.diaSemana}
              onChange={(e) => atualizarIntervalo(indice, { diaSemana: Number(e.target.value) })}
            >
              {NOMES_DIAS_SEMANA.map((nome, dia) => (
                <option key={dia} value={dia}>
                  {nome}
                </option>
              ))}
            </select>
            <input
              type="time"
              className={`${classeInput} w-28`}
              value={intervalo.inicio.slice(0, 5)}
              onChange={(e) => atualizarIntervalo(indice, { inicio: `${e.target.value}:00` })}
            />
            <span>até</span>
            <input
              type="time"
              className={`${classeInput} w-28`}
              value={intervalo.fim.slice(0, 5)}
              onChange={(e) => atualizarIntervalo(indice, { fim: `${e.target.value}:00` })}
            />
            <button type="button" className="text-sm text-red-600 hover:underline dark:text-red-400" onClick={() => removerIntervalo(indice)}>
              Remover
            </button>
          </div>
        ))}
        {intervalos.length === 0 && <p className="text-sm text-gray-500 dark:text-neutral-400">Nenhum intervalo cadastrado.</p>}

        <div className="flex items-center justify-between pt-2">
          <button type="button" className={classeBotaoSecundario} onClick={adicionarIntervalo}>
            Adicionar intervalo
          </button>
          <button type="button" disabled={salvando} className={classeBotaoPrimario} onClick={salvar}>
            {salvando ? "Salvando..." : "Salvar horário"}
          </button>
        </div>
        {mensagem && <p className="text-sm text-gray-600 dark:text-neutral-300">{mensagem}</p>}
      </div>
    </section>
  );
}

function SecaoBloqueios({ profissionalId }: { profissionalId: string }) {
  const { chamarApi } = useAutenticacao();
  const [bloqueios, setBloqueios] = useState<BloqueioResumo[]>([]);
  const [inicio, setInicio] = useState("");
  const [fim, setFim] = useState("");
  const [motivo, setMotivo] = useState("");
  const [erro, setErro] = useState<string | null>(null);

  const carregar = useCallback(async () => {
    setBloqueios(await chamarApi<BloqueioResumo[]>(`/painel/profissionais/${profissionalId}/bloqueios`));
  }, [chamarApi, profissionalId]);

  useEffect(() => {
    // Busca disparada pela montagem, não estado derivado de props.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    carregar();
  }, [carregar]);

  async function criar(evento: FormEvent) {
    evento.preventDefault();
    setErro(null);
    try {
      await chamarApi(`/painel/profissionais/${profissionalId}/bloqueios`, {
        metodo: "POST",
        corpo: { inicioUtc: new Date(inicio).toISOString(), fimUtc: new Date(fim).toISOString(), motivo: motivo || null },
      });
      setInicio("");
      setFim("");
      setMotivo("");
      await carregar();
    } catch {
      setErro("Não foi possível criar o bloqueio.");
    }
  }

  async function remover(id: string) {
    await chamarApi(`/painel/bloqueios/${id}`, { metodo: "DELETE" });
    await carregar();
  }

  return (
    <section>
      <h2 className="mb-3 text-lg font-semibold text-gray-900 dark:text-neutral-50">Folgas e bloqueios</h2>

      <div className={`${classeCartao} mb-3 divide-y divide-gray-100 dark:divide-neutral-800`}>
        {bloqueios.map((b) => (
          <div key={b.id} className="flex items-center justify-between px-4 py-2 text-sm">
            <span>
              {new Date(b.inicioUtc).toLocaleString("pt-BR")} até {new Date(b.fimUtc).toLocaleString("pt-BR")}
              {b.motivo && ` — ${b.motivo}`}
            </span>
            <button className="text-red-600 hover:underline dark:text-red-400" onClick={() => remover(b.id)}>
              Remover
            </button>
          </div>
        ))}
        {bloqueios.length === 0 && <p className="px-4 py-3 text-sm text-gray-500 dark:text-neutral-400">Nenhum bloqueio cadastrado.</p>}
      </div>

      <form onSubmit={criar} className="flex flex-wrap items-end gap-2">
        <label>
          <span className={classeLabel}>Início</span>
          <input required type="datetime-local" className={classeInput} value={inicio} onChange={(e) => setInicio(e.target.value)} />
        </label>
        <label>
          <span className={classeLabel}>Fim</span>
          <input required type="datetime-local" className={classeInput} value={fim} onChange={(e) => setFim(e.target.value)} />
        </label>
        <label>
          <span className={classeLabel}>Motivo (opcional)</span>
          <input className={classeInput} value={motivo} onChange={(e) => setMotivo(e.target.value)} />
        </label>
        <button type="submit" className={classeBotaoPrimario}>
          Adicionar bloqueio
        </button>
      </form>
      {erro && <p className="mt-2 text-sm text-red-600">{erro}</p>}
    </section>
  );
}

function SecaoServicosVinculados({ profissionalId }: { profissionalId: string }) {
  const { chamarApi } = useAutenticacao();
  const [vinculados, setVinculados] = useState<ProfissionalServicoResumo[]>([]);
  const [todos, setTodos] = useState<ServicoResumo[]>([]);
  const [servicoParaVincular, setServicoParaVincular] = useState("");

  const carregar = useCallback(async () => {
    const [vinculosAtuais, listaServicos] = await Promise.all([
      chamarApi<ProfissionalServicoResumo[]>(`/painel/profissionais/${profissionalId}/servicos`),
      chamarApi<ServicoResumo[]>("/painel/servicos"),
    ]);
    setVinculados(vinculosAtuais);
    setTodos(listaServicos.filter((s) => s.ativo));
  }, [chamarApi, profissionalId]);

  useEffect(() => {
    // Busca disparada pela montagem, não estado derivado de props.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    carregar();
  }, [carregar]);

  async function vincular() {
    if (!servicoParaVincular) return;
    await chamarApi(`/painel/profissionais/${profissionalId}/servicos`, {
      metodo: "POST",
      corpo: { servicoId: servicoParaVincular, precoPersonalizado: null, duracaoPersonalizadaMinutos: null },
    });
    setServicoParaVincular("");
    await carregar();
  }

  async function desvincular(servicoId: string) {
    await chamarApi(`/painel/profissionais/${profissionalId}/servicos/${servicoId}`, { metodo: "DELETE" });
    await carregar();
  }

  const disponiveisParaVincular = todos.filter((s) => !vinculados.some((v) => v.servicoId === s.id));

  return (
    <section>
      <h2 className="mb-3 text-lg font-semibold text-gray-900 dark:text-neutral-50">Serviços executados</h2>

      <div className={`${classeCartao} mb-3 divide-y divide-gray-100 dark:divide-neutral-800`}>
        {vinculados.map((v) => (
          <div key={v.servicoId} className="flex items-center justify-between px-4 py-2 text-sm">
            <span>
              {v.nome} — R$ {v.preco.toFixed(2)} ({v.duracaoMinutos} min)
            </span>
            <button className="text-red-600 hover:underline dark:text-red-400" onClick={() => desvincular(v.servicoId)}>
              Remover
            </button>
          </div>
        ))}
        {vinculados.length === 0 && <p className="px-4 py-3 text-sm text-gray-500 dark:text-neutral-400">Nenhum serviço vinculado ainda.</p>}
      </div>

      {disponiveisParaVincular.length > 0 && (
        <div className="flex items-end gap-2">
          <label>
            <span className={classeLabel}>Vincular serviço</span>
            <select className={classeInput} value={servicoParaVincular} onChange={(e) => setServicoParaVincular(e.target.value)}>
              <option value="">Selecione...</option>
              {disponiveisParaVincular.map((s) => (
                <option key={s.id} value={s.id}>
                  {s.nome}
                </option>
              ))}
            </select>
          </label>
          <button type="button" className={classeBotaoPrimario} onClick={vincular}>
            Vincular
          </button>
        </div>
      )}
    </section>
  );
}
