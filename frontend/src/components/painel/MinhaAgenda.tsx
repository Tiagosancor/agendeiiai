"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { useAutenticacao, ErroApi } from "@/lib/auth-context";
import { classeCartao, classeInput, classeLabel } from "@/components/estilos";
import { ModalConcluirAtendimento } from "@/components/painel/ModalConcluirAtendimento";
import { SemanaAgenda, inicioDaSemana, somarDias } from "@/components/painel/SemanaAgenda";
import type { AgendaSemana, AgendamentoResumo } from "@/lib/tipos";
import { dataLocalIso, formatarReais } from "@/lib/formatacao";

const formatarHora = (iso: string) => new Date(iso).toLocaleTimeString("pt-BR", { hour: "2-digit", minute: "2-digit" });

const ROTULO_STATUS: Record<string, string> = { EmAtendimento: "Em atendimento", Concluido: "Concluído" };

/**
 * "Minha agenda" (seção 7): o que o Profissional logado vê sem "gerenciar agenda" — a própria agenda, dia e semana, e
 * nos próprios atendimentos Iniciar, Concluir (com produtos, se ele também vende) e Faltou.
 */
export function MinhaAgenda() {
  const { chamarApi } = useAutenticacao();
  const [visao, setVisao] = useState<"dia" | "semana">("dia");
  const [data, setData] = useState(dataLocalIso());
  const [agenda, setAgenda] = useState<AgendamentoResumo[] | null>(null);
  const [semana, setSemana] = useState<AgendaSemana | null>(null);
  const [semVinculo, setSemVinculo] = useState(false);
  const [paraConcluir, setParaConcluir] = useState<AgendamentoResumo | null>(null);
  const [erro, setErro] = useState<string | null>(null);

  // Só a última busca escreve na tela: trocar de data ou de visão em seguida não deixa a resposta antiga por cima.
  const ultimaBusca = useRef(0);
  const carregar = useCallback(async () => {
    const busca = ++ultimaBusca.current;
    const atual = () => busca === ultimaBusca.current;
    try {
      setErro(null);
      if (visao === "dia") {
        const resposta = await chamarApi<AgendamentoResumo[] | undefined>(`/painel/minha-agenda?data=${data}`);
        if (!atual()) return;
        setSemVinculo(resposta === undefined);
        setAgenda(resposta ?? []);
      } else {
        setSemana(null);
        const resposta = await chamarApi<AgendaSemana | undefined>(`/painel/minha-agenda/semana?inicio=${inicioDaSemana(data)}`);
        if (!atual()) return;
        setSemVinculo(resposta === undefined);
        setSemana(resposta ?? null);
      }
    } catch {
      if (atual()) setErro("Não foi possível carregar a sua agenda.");
    }
  }, [chamarApi, visao, data]);

  useEffect(() => {
    // Busca disparada pela troca de visão e de data.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    carregar();
  }, [carregar]);

  async function acao(id: string, qual: "iniciar" | "faltou") {
    try {
      setErro(null);
      await chamarApi(`/painel/minha-agenda/${id}/${qual}`, { metodo: "POST" });
    } catch (excecao) {
      setErro(excecao instanceof ErroApi ? excecao.message : "Não foi possível concluir a ação.");
    }
    await carregar();
  }

  if (semVinculo) {
    return (
      <p className="rounded-lg border border-gray-200 p-4 text-sm text-gray-600 dark:border-neutral-800 dark:text-neutral-300">
        Seu acesso não está ligado a um profissional da agenda e você não tem permissão para gerenciar a agenda.
      </p>
    );
  }

  return (
    <div className="space-y-4">
      <div className="flex gap-1 border-b border-gray-200 dark:border-neutral-800" role="tablist" aria-label="Visão da agenda">
        {(
          [
            ["dia", "Dia"],
            ["semana", "Semana"],
          ] as const
        ).map(([valor, rotulo]) => (
          <button
            key={valor}
            type="button"
            role="tab"
            aria-selected={visao === valor}
            className={`-mb-px border-b-2 px-3 py-2 text-sm font-medium ${
              visao === valor
                ? "border-marca-primaria text-marca-primaria dark:border-marca-acento dark:text-marca-acento"
                : "border-transparent text-gray-500 hover:text-gray-700 dark:text-neutral-400 dark:hover:text-neutral-200"
            }`}
            onClick={() => setVisao(valor)}
          >
            {rotulo}
          </button>
        ))}
      </div>

      {erro && <p className="text-sm text-red-600">{erro}</p>}

      {visao === "semana" ? (
        <SemanaAgenda
          semana={semana}
          aoMudarSemana={(dias) => setData((atual) => somarDias(atual, dias))}
          aoEscolherDia={(dia) => {
            setData(dia);
            setVisao("dia");
          }}
        />
      ) : (
        <>
          <label className="block w-fit">
            <span className={classeLabel}>Data</span>
            <input type="date" className={classeInput} value={data} onChange={(e) => setData(e.target.value)} />
          </label>
          <div className={`${classeCartao} divide-y divide-gray-100 dark:divide-neutral-800`}>
            {agenda?.length === 0 && <p className="px-4 py-6 text-sm text-gray-500 dark:text-neutral-400">Nada na sua agenda neste dia.</p>}
            {agenda?.map((item) => (
              <div key={item.id} className="flex flex-wrap items-center justify-between gap-3 px-4 py-3">
                <div>
                  <p className="text-sm font-medium text-gray-900 dark:text-neutral-50">
                    {formatarHora(item.inicio)}–{formatarHora(item.fim)} · {ROTULO_STATUS[item.status] ?? item.status}
                  </p>
                  <p className="text-sm text-gray-700 dark:text-neutral-300">{item.clienteNome}</p>
                  <p className="text-xs text-gray-500 dark:text-neutral-400">
                    {item.servicos.join(", ")} · {formatarReais(item.total)}
                  </p>
                  {item.observacoes && <p className="text-xs text-gray-500 dark:text-neutral-400">Obs.: {item.observacoes}</p>}
                </div>
                {(item.status === "Agendado" || item.status === "EmAtendimento") && (
                  <div className="flex flex-wrap gap-2">
                    {item.status === "Agendado" && (
                      <button className="text-sm font-medium text-violet-700 hover:underline dark:text-violet-300" onClick={() => acao(item.id, "iniciar")}>
                        Iniciar atendimento
                      </button>
                    )}
                    <button className="text-sm text-green-700 hover:underline dark:text-green-400" onClick={() => setParaConcluir(item)}>
                      Concluir
                    </button>
                    {item.status === "Agendado" && (
                      <button className="text-sm text-red-700 hover:underline dark:text-red-400" onClick={() => acao(item.id, "faltou")}>
                        Faltou
                      </button>
                    )}
                  </div>
                )}
              </div>
            ))}
          </div>
        </>
      )}

      <ModalConcluirAtendimento
        agendamento={paraConcluir}
        caminhoBase="/painel/minha-agenda"
        aoFechar={async (mudou) => {
          setParaConcluir(null);
          if (mudou) await carregar();
        }}
      />
    </div>
  );
}
