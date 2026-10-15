import type { ItemOutbox, ResumoOutbox } from "@/services/outbox-plataforma-service";

export const ROTULOS_STATUS_OUTBOX: Record<string, string> = {
  Pendente: "Pendente",
  Enviando: "Em processamento",
  Enviada: "Enviada",
  Falhou: "Falha definitiva",
  Cancelada: "Cancelada com segurança",
  Desconhecida: "Resultado desconhecido",
};

export const ROTULOS_TIPO_OUTBOX: Record<string, string> = {
  Confirmacao: "Confirmação",
  Lembrete60: "Lembrete de 60 minutos",
  Cancelamento: "Cancelamento",
  ProfissionalNovo: "Novo agendamento ao profissional",
  ProfissionalRemarcado: "Remarcação ao profissional",
  ProfissionalCancelado: "Cancelamento ao profissional",
  LembreteRetorno: "Lembrete de retorno",
};

export const MENSAGENS_ERRO_OUTBOX: Record<string, string> = {
  tentativas_esgotadas: "Todas as tentativas de envio foram esgotadas.",
  prazo_esgotado: "O prazo máximo para enviar esta mensagem foi encerrado.",
  recusada_provedor: "O provedor recusou o destinatário ou a mensagem.",
  resultado_ambiguo: "O provedor pode ter aceitado a mensagem, mas a confirmação se perdeu.",
  lease_expirado_apos_inicio: "O processamento foi interrompido depois do início do envio.",
  expirada: "A mensagem perdeu a validade antes do envio.",
  agendamento_obsoleto: "O agendamento mudou e esta mensagem deixou de ser válida.",
  horario_alterado: "O horário foi alterado antes do envio.",
  sem_destinatario: "Não havia destinatário válido para este canal.",
  canal_desativado: "O canal estava desativado para o negócio.",
  opt_out: "O cliente optou por não receber esta comunicação.",
  cliente_inelegivel: "O cliente não atendia aos critérios desta comunicação.",
  negocio_inativo: "O negócio estava inativo no momento do processamento.",
  referencia_inexistente: "A referência necessária para o envio não existe mais.",
  falha_provedor: "O provedor apresentou uma falha temporária; uma nova tentativa pode estar agendada.",
  erro_interno: "O processamento encontrou uma falha interna temporária.",
};

export function rotuloStatus(status: string): string {
  return ROTULOS_STATUS_OUTBOX[status] ?? "Status ainda não reconhecido";
}

export function rotuloTipo(tipo: string): string {
  return ROTULOS_TIPO_OUTBOX[tipo] ?? "Tipo ainda não reconhecido";
}

export function mensagemErro(codigo: string | null): string {
  if (!codigo) return "Nenhum motivo técnico registrado.";
  return MENSAGENS_ERRO_OUTBOX[codigo] ?? "Código técnico ainda não reconhecido; consulte os registros do backend com segurança.";
}

export function formatarDataHora(iso: string | null): string {
  if (!iso) return "—";
  const data = new Date(iso);
  if (Number.isNaN(data.getTime())) return "Data indisponível";
  return new Intl.DateTimeFormat("pt-BR", { dateStyle: "short", timeStyle: "medium" }).format(data);
}

export function formatarDuracao(segundos: number | null): string {
  if (segundos === null || segundos < 0) return "Sem pendências";
  if (segundos < 60) return `há ${Math.max(1, Math.round(segundos))} segundos`;
  if (segundos < 3600) return `há ${Math.round(segundos / 60)} minutos`;
  if (segundos < 86400) return `há ${Math.round(segundos / 3600)} horas`;
  return `há ${Math.round(segundos / 86400)} dias`;
}

export function nivelResumo(resumo: ResumoOutbox): "saudavel" | "atencao" | "critico" {
  if (resumo.falhas > 0 || resumo.desconhecidas > 0) return "critico";
  if ((resumo.idadeDaPendenciaMaisAntigaSegundos ?? 0) > 300) return "atencao";
  return "saudavel";
}

export function classeStatus(status: string): string {
  switch (status) {
    case "Falhou":
      return "border-red-200 bg-red-50 text-red-800 dark:border-red-900 dark:bg-red-950/40 dark:text-red-300";
    case "Desconhecida":
      return "border-fuchsia-200 bg-fuchsia-50 text-fuchsia-800 dark:border-fuchsia-900 dark:bg-fuchsia-950/40 dark:text-fuchsia-300";
    case "Pendente":
    case "Enviando":
      return "border-amber-200 bg-amber-50 text-amber-800 dark:border-amber-900 dark:bg-amber-950/40 dark:text-amber-300";
    case "Enviada":
      return "border-emerald-200 bg-emerald-50 text-emerald-800 dark:border-emerald-900 dark:bg-emerald-950/40 dark:text-emerald-300";
    default:
      return "border-gray-200 bg-gray-50 text-gray-700 dark:border-neutral-700 dark:bg-neutral-800 dark:text-neutral-300";
  }
}

export function idsTecnicos(item: ItemOutbox) {
  return [item.id, item.negocioId, item.agendamentoId].filter(Boolean) as string[];
}
