import type { ChamarPlataforma } from "@/components/plataforma/ContextoPlataforma";

export const STATUS_OUTBOX = ["Pendente", "Enviando", "Enviada", "Falhou", "Cancelada", "Desconhecida"] as const;
export const TIPOS_OUTBOX = ["Confirmacao", "Lembrete60", "Cancelamento", "ProfissionalNovo", "ProfissionalRemarcado", "ProfissionalCancelado", "LembreteRetorno"] as const;
export const CANAIS_OUTBOX = ["Email", "WhatsApp"] as const;

export type StatusOutbox = (typeof STATUS_OUTBOX)[number];
export type TipoOutbox = (typeof TIPOS_OUTBOX)[number];
export type CanalOutbox = (typeof CANAIS_OUTBOX)[number];

export interface ItemOutbox {
  id: string;
  negocioId: string;
  tipo: TipoOutbox | string;
  canal: CanalOutbox | string;
  status: StatusOutbox | string;
  tentativas: number;
  criadaEm: string;
  atualizadaEm: string;
  proximaTentativaEm: string | null;
  expiraEm: string | null;
  enviadaEm: string | null;
  canceladaEm: string | null;
  ultimoErroCodigo: string | null;
  agendamentoId: string | null;
}

export interface ResumoOutbox {
  pendentes: number;
  enviando: number;
  falhas: number;
  desconhecidas: number;
  idadeDaPendenciaMaisAntigaSegundos: number | null;
  pendenciaMaisAntigaDesde: string | null;
  geradoEm: string;
  falhasRecentes: ItemOutbox[];
}

export interface PaginaOutbox {
  itens: ItemOutbox[];
  pagina: number;
  tamanho: number;
  total: number;
}

export interface FiltrosOutbox {
  status: string;
  tipo: string;
  canal: string;
  negocioId: string;
  de: string;
  ate: string;
  pagina: number;
  tamanho: number;
}

export function criarOutboxPlataformaService(chamar: ChamarPlataforma) {
  return {
    resumo(sinal?: AbortSignal) {
      return chamar<ResumoOutbox>("/plataforma/notificacoes/resumo", { sinal });
    },
    listar(filtros: FiltrosOutbox, sinal?: AbortSignal) {
      const parametros = new URLSearchParams();
      if (filtros.status) parametros.set("status", filtros.status);
      if (filtros.tipo) parametros.set("tipo", filtros.tipo);
      if (filtros.canal) parametros.set("canal", filtros.canal);
      if (filtros.negocioId.trim()) parametros.set("negocioId", filtros.negocioId.trim());
      if (filtros.de) parametros.set("de", new Date(`${filtros.de}T00:00:00`).toISOString());
      if (filtros.ate) parametros.set("ate", new Date(`${filtros.ate}T23:59:59.999`).toISOString());
      parametros.set("pagina", String(Math.max(1, filtros.pagina)));
      parametros.set("tamanho", String(Math.min(200, Math.max(1, filtros.tamanho))));
      return chamar<PaginaOutbox>(`/plataforma/notificacoes/outbox?${parametros}`, { sinal });
    },
  };
}
