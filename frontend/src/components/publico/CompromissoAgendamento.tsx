import type { DetalhePublicoAgendamento } from "@/lib/tipos";
import { formatarReais } from "@/lib/formatacao";
import { formatarHorarioEstabelecimento } from "@/lib/horario-estabelecimento";
import { formatarHoraDoNegocio } from "./datas-agendamento";

const ROTULOS: Record<string, string> = { EmAtendimento: "Em atendimento", Concluido: "Concluído" };
const SIMBOLOS: Record<string, string> = { Agendado: "✓", Cancelado: "×", Concluido: "✓", Faltou: "—", Expirado: "◷", EmAtendimento: "◷" };

/** Somente apresentação. Permissões e disponibilidade continuam sendo autoridade do servidor. */
export function CompromissoAgendamento({ detalhe, fuso }: { detalhe: DetalhePublicoAgendamento; fuso: string }) {
  return <article className="gestao-compromisso" aria-label="Detalhes do agendamento">
    <p className="gestao-status" data-estado={detalhe.status}><span aria-hidden="true">{SIMBOLOS[detalhe.status] ?? "•"}</span><span><span className="sr-only">Status: </span>{ROTULOS[detalhe.status] ?? detalhe.status}</span></p>
    <div className="gestao-quando">
      <time dateTime={detalhe.inicio} className="gestao-data">{formatarHorarioEstabelecimento(detalhe.inicio, fuso)}</time>
      <strong className="gestao-hora" aria-hidden="true">{formatarHoraDoNegocio(detalhe.inicio, fuso)}</strong>
    </div>
    <section className="gestao-servicos" aria-labelledby="compromisso-servicos">
      <h2 id="compromisso-servicos">{detalhe.servicos.length === 1 ? "Seu serviço" : "Seus serviços"}</h2>
      {detalhe.servicosDetalhe?.length ? <ul>{detalhe.servicosDetalhe.map(s => <li key={s.servicoId}><strong>{s.nome}</strong><span> · {s.duracaoMinutos} min · {formatarReais(s.preco)}</span></li>)}</ul> : <p className="gestao-servico-nome">{detalhe.servicos.join(", ")}</p>}
    </section>
    {detalhe.profissional?.nome && <p className="gestao-profissional"><span className="gestao-avatar" aria-hidden="true">{detalhe.profissional.nome.charAt(0)}</span><span>Profissional: <strong>{detalhe.profissional.nome}</strong></span></p>}
    <dl className="gestao-metadados">
      {detalhe.local && <div className="gestao-local"><dt>Onde você será atendido</dt><dd>{detalhe.local}</dd></div>}
      {detalhe.duracaoMinutos !== undefined && <div><dt className="sr-only">Duração</dt><dd>Duração: {detalhe.duracaoMinutos} minutos</dd></div>}
      <div><dt className="sr-only">Total</dt><dd className="gestao-total">Total: {formatarReais(detalhe.total)}</dd></div>
    </dl>
  </article>;
}
