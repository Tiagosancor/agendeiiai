"use client";

import { useEffect, useRef, useState, type CSSProperties } from "react";
import { useAutenticacao } from "@/lib/auth-context";
import type { AgendamentoNaGrade, CelulaGrade, ColunaGrade, GradeAgenda } from "@/lib/tipos";

const COR_STATUS: Record<string, string> = {
  Agendado: "bg-blue-100 text-blue-900 dark:bg-blue-950 dark:text-blue-100",
  EmAtendimento: "bg-violet-100 text-violet-900 dark:bg-violet-950 dark:text-violet-100",
  Concluido: "bg-green-100 text-green-900 dark:bg-green-950 dark:text-green-100",
  Reservado: "bg-yellow-100 text-yellow-900 dark:bg-yellow-950 dark:text-yellow-100",
};

const TEXTO_APAGADO = "text-gray-500 dark:text-neutral-400";

const ROTULO_STATUS: Record<string, string> = {
  Agendado: "Agendado",
  EmAtendimento: "Em atendimento",
  Concluido: "Concluído",
  Reservado: "Reservando",
};

/**
 * Visão "Dia" da agenda (seção 7): horários de 15 minutos nas linhas e um profissional ativo por coluna.
 * Livre é clicável (cria um agendamento ali); ocupado mostra cliente e serviço, colorido pelo status (e com
 * a marca de forçado); almoço, folga, bloqueio e fora do expediente aparecem diferentes e não clicam.
 */
export function GradeDoDia({
  data,
  versao,
  aoEscolherLivre,
  aoAbrirAgendamento,
}: {
  data: string;
  /** Muda depois de criar ou alterar um agendamento — recarrega a grade. */
  versao: number;
  aoEscolherLivre: (profissionalId: string, inicio: string) => void;
  aoAbrirAgendamento: (profissionalId: string) => void;
}) {
  const { chamarApi } = useAutenticacao();
  const [grade, setGrade] = useState<GradeAgenda | null>(null);
  const [erro, setErro] = useState<string | null>(null);
  const [esconderFolga, setEsconderFolga] = useState(false);
  const ultimaBusca = useRef(0);
  const rolagem = useRef<HTMLDivElement>(null);

  function navegarAteProfissional(id: string) {
    const area = rolagem.current;
    const cabecalho = area?.querySelector("thead tr");
    const coluna = Array.from(cabecalho?.children ?? []).find((elemento) =>
      elemento instanceof HTMLElement && elemento.dataset.profissionalId === id,
    );
    const horario = cabecalho?.firstElementChild;
    if (!area || !coluna || !horario) return;
    const destino = coluna.getBoundingClientRect().left - area.getBoundingClientRect().left
      + area.scrollLeft - horario.getBoundingClientRect().width;
    area.scrollTo({ left: Math.max(0, destino), behavior: window.matchMedia("(prefers-reduced-motion: reduce)").matches ? "auto" : "smooth" });
  }

  useEffect(() => {
    // Trocar a data depressa dispara várias buscas; só a última escreve na tela.
    const busca = ++ultimaBusca.current;
    chamarApi<GradeAgenda>(`/painel/agenda/grade?data=${data}`)
      .then((resultado) => {
        if (busca !== ultimaBusca.current) return;
        setGrade(resultado);
        setErro(null);
      })
      .catch(() => busca === ultimaBusca.current && setErro("Não foi possível carregar a agenda do dia."));
  }, [chamarApi, data, versao]);

  if (erro) return <p className="text-sm text-red-600">{erro}</p>;
  if (!grade) return <p className="text-sm text-gray-500 dark:text-neutral-400">Carregando...</p>;

  const colunas = grade.profissionais.filter((p) => !esconderFolga || !p.deFolga);

  return (
    <div className="painel-grade space-y-2">
      <label className="flex items-center gap-2 text-sm text-gray-700 dark:text-neutral-300">
        <input type="checkbox" checked={esconderFolga} onChange={(e) => setEsconderFolga(e.target.checked)} />
        Esconder quem está de folga
      </label>

      {colunas.length === 0 ? (
        <p className="text-sm text-gray-500 dark:text-neutral-400">Ninguém trabalha neste dia.</p>
      ) : (
        <>
        {colunas.length > 1 && <div className="painel-grade-navegacao">
          <label>
            <span>Profissional</span>
            <select key={colunas.map((p) => p.profissionalId).join(",")} defaultValue={colunas[0].profissionalId} onChange={(evento) => navegarAteProfissional(evento.target.value)}>
              {colunas.map((p) => <option key={p.profissionalId} value={p.profissionalId}>{p.nome}{p.deFolga ? " (folga)" : ""}</option>)}
            </select>
          </label>
          <p>Deslize para ver outros profissionais <span aria-hidden="true">↔</span></p>
        </div>}
        <div ref={rolagem} className="painel-grade-scroll overflow-x-auto rounded-lg border border-gray-200 dark:border-neutral-800" role="region" aria-label="Grade de profissionais" tabIndex={0}>
          <table className="w-full border-collapse text-xs" aria-label="Agenda do dia" style={{ "--painel-grade-colunas": colunas.length } as CSSProperties}>
            <colgroup><col className="painel-grade-coluna-horas" />{colunas.map((coluna) => <col key={coluna.profissionalId} />)}</colgroup>
            <thead className="sticky top-0 bg-white dark:bg-neutral-900">
              <tr>
                <th className="w-14 border-b border-gray-200 px-2 py-2 text-left font-medium text-gray-500 dark:border-neutral-800 dark:text-neutral-400">
                  Horário
                </th>
                {colunas.map((coluna) => (
                  <th
                    key={coluna.profissionalId}
                    data-profissional-id={coluna.profissionalId}
                    className="min-w-32 border-b border-l border-gray-200 px-2 py-2 text-left font-semibold text-gray-900 dark:border-neutral-800 dark:text-neutral-50"
                  >
                    {coluna.nome}
                    {coluna.deFolga && <span className="ml-1 font-normal text-gray-500 dark:text-neutral-400">(folga)</span>}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {grade.horarios.map((hora, linha) => (
                <tr key={hora}>
                  <th
                    scope="row"
                    className={`border-gray-100 px-2 text-left align-top font-normal text-gray-500 dark:border-neutral-800 dark:text-neutral-400 ${
                      hora.endsWith(":00") ? "border-t" : ""
                    }`}
                  >
                    {hora.endsWith(":00") || hora.endsWith(":30") ? hora : ""}
                  </th>
                  {colunas.map((coluna) => (
                    <Celula
                      key={coluna.profissionalId}
                      coluna={coluna}
                      linha={linha}
                      aoEscolherLivre={aoEscolherLivre}
                      aoAbrirAgendamento={aoAbrirAgendamento}
                    />
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        </>
      )}
    </div>
  );
}

function Celula({
  coluna,
  linha,
  aoEscolherLivre,
  aoAbrirAgendamento,
}: {
  coluna: ColunaGrade;
  linha: number;
  aoEscolherLivre: (profissionalId: string, inicio: string) => void;
  aoAbrirAgendamento: (profissionalId: string) => void;
}) {
  const celula = coluna.celulas[linha];
  const anterior: CelulaGrade | undefined = coluna.celulas[linha - 1];
  // O texto vai só na primeira célula de cada trecho (um agendamento, o almoço, um bloqueio).
  const comecaTrecho =
    !anterior || anterior.estado !== celula.estado || anterior.agendamentoId !== celula.agendamentoId || anterior.descricao !== celula.descricao;
  const base = "h-7 border-l border-gray-200 p-0 align-top dark:border-neutral-800";

  switch (celula.estado) {
    case "Livre":
      return (
        <td className={base}>
          <button
            type="button"
            aria-label={`Livre: ${coluna.nome} às ${celula.hora}`}
            className="h-7 w-full text-gray-300 hover:bg-green-50 hover:text-green-700 dark:text-neutral-700 dark:hover:bg-green-950 dark:hover:text-green-300"
            onClick={() => aoEscolherLivre(coluna.profissionalId, celula.inicio)}
          >
            +
          </button>
        </td>
      );
    case "Ocupado": {
      const agendamento: AgendamentoNaGrade | undefined = coluna.agendamentos.find((a) => a.id === celula.agendamentoId);
      const cor = COR_STATUS[agendamento?.status ?? ""] ?? "bg-gray-100 dark:bg-neutral-800";
      const forcado = agendamento?.forcado ? "border-l-4 border-l-amber-500" : "";
      return (
        <td className={`${base} ${cor} ${forcado}`}>
          <button
            type="button"
            className="h-7 w-full overflow-hidden px-1 text-left"
            title={agendamento ? `${agendamento.clienteNome} — ${agendamento.servicos.join(", ")}` : undefined}
            onClick={() => aoAbrirAgendamento(coluna.profissionalId)}
          >
            {comecaTrecho && agendamento && (
              <span className="block truncate">
                <strong>{agendamento.clienteNome}</strong> · {agendamento.servicos.join(", ")}
                <span className="ml-1 opacity-70">
                  ({ROTULO_STATUS[agendamento.status] ?? agendamento.status}
                  {agendamento.forcado ? ", forçado" : ""})
                </span>
              </span>
            )}
          </button>
        </td>
      );
    }
    case "Almoco":
      return <Bloqueada base={base} classe={`bg-gray-100 dark:bg-neutral-800 ${TEXTO_APAGADO}`} texto={comecaTrecho ? "Almoço" : ""} />;
    case "Folga":
      return <Bloqueada base={base} classe={`bg-gray-50 dark:bg-neutral-900 ${TEXTO_APAGADO}`} texto={linha === 0 ? "Folga" : ""} />;
    case "Bloqueio":
      return (
        <Bloqueada
          base={base}
          classe="bg-red-50 text-red-800 dark:bg-red-950 dark:text-red-200"
          texto={comecaTrecho ? `Bloqueio${celula.descricao ? `: ${celula.descricao}` : ""}` : ""}
        />
      );
    default:
      return <Bloqueada base={base} classe={`bg-gray-50 dark:bg-neutral-900/60 ${TEXTO_APAGADO}`} texto={comecaTrecho ? "Fora do expediente" : ""} />;
  }
}

function Bloqueada({ base, classe, texto }: { base: string; classe: string; texto: string }) {
  return <td className={`${base} ${classe} truncate px-1`}>{texto}</td>;
}
