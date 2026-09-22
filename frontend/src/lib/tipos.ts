// Tipos espelhando os DTOs do backend (Plataforma.Aplicacao). Sem geração automática —
// mantenha em sincronia manualmente se os DTOs do backend mudarem.

export type Perfil = "Administrador" | "Recepcionista" | "Profissional";

export const PERFIS: { valor: Perfil; rotulo: string }[] = [
  { valor: "Administrador", rotulo: "Administrador" },
  { valor: "Recepcionista", rotulo: "Recepcionista" },
  { valor: "Profissional", rotulo: "Profissional" },
];

export type Permissao =
  | "GerenciarUsuarios"
  | "GerenciarProfissionais"
  | "GerenciarServicos"
  | "GerenciarClientes"
  | "GerenciarConfiguracoesDoNegocio"
  | "VerAgendaDeOutrosProfissionais"
  | "GerenciarAgenda"
  | "VerFinanceiro"
  | "GerenciarCupons"
  | "GerenciarFidelidade";

export const PERMISSOES: { valor: Permissao; rotulo: string }[] = [
  { valor: "GerenciarUsuarios", rotulo: "Gerenciar usuários" },
  { valor: "GerenciarProfissionais", rotulo: "Gerenciar profissionais" },
  { valor: "GerenciarServicos", rotulo: "Gerenciar serviços" },
  { valor: "GerenciarClientes", rotulo: "Gerenciar clientes" },
  { valor: "GerenciarConfiguracoesDoNegocio", rotulo: "Gerenciar dados do negócio" },
  { valor: "VerAgendaDeOutrosProfissionais", rotulo: "Ver agenda de outros profissionais" },
  { valor: "GerenciarAgenda", rotulo: "Gerenciar agenda" },
  { valor: "VerFinanceiro", rotulo: "Ver financeiro" },
  { valor: "GerenciarCupons", rotulo: "Gerenciar cupons" },
  { valor: "GerenciarFidelidade", rotulo: "Gerenciar fidelidade" },
];

export interface UsuarioResumo {
  id: string;
  nome: string;
  email: string;
  perfil: Perfil;
  ativo: boolean;
  fotoUrl: string | null;
}

export interface UsuarioDetalhe {
  id: string;
  nome: string;
  email: string;
  telefone: string | null;
  perfil: Perfil;
  ativo: boolean;
  fotoUrl: string | null;
  cpfMascarado: string | null;
  permissoes: Permissao[];
}

export interface ProfissionalResumo {
  id: string;
  nome: string;
  ativo: boolean;
  fotoUrl: string | null;
}

export interface ProfissionalDetalhe {
  id: string;
  nome: string;
  telefone: string | null;
  email: string | null;
  ativo: boolean;
  fotoUrl: string | null;
  cpfMascarado: string | null;
}

export interface CategoriaResumo {
  id: string;
  nome: string;
  ativa: boolean;
}

export interface ServicoResumo {
  id: string;
  categoriaId: string;
  nome: string;
  preco: number;
  duracaoMinutos: number;
  popular: boolean;
  ativo: boolean;
}

export interface ClienteResumo {
  id: string;
  nome: string;
  telefone: string;
  email: string | null;
  observacoes: string | null;
}

export interface HorarioFuncionamentoDia {
  diaSemana: number;
  abertura: string | null;
  fechamento: string | null;
  fechado: boolean;
}

export interface PerfilNegocio {
  slug: string;
  nomeExibido: string;
  tipo: string;
  logoUrl: string | null;
  corPrimaria: string | null;
  corSecundaria: string | null;
  tituloPagina: string | null;
  subtituloPagina: string | null;
  textoSobre: string | null;
  bairro: string | null;
  cidade: string | null;
  rua: string | null;
  numero: string | null;
  cep: string | null;
  telefone: string | null;
  instagram: string | null;
  facebook: string | null;
  whatsApp: string | null;
  horarioFuncionamento: HorarioFuncionamentoDia[];
}

export const NOMES_DIAS_SEMANA = ["Domingo", "Segunda", "Terça", "Quarta", "Quinta", "Sexta", "Sábado"];

// --- Sprint 2: agenda e disponibilidade ---

export interface IntervaloTrabalho {
  diaSemana: number;
  inicio: string; // "HH:mm:ss"
  fim: string;
}

export interface BloqueioResumo {
  id: string;
  profissionalId: string;
  inicioUtc: string;
  fimUtc: string;
  motivo: string | null;
}

export interface ProfissionalServicoResumo {
  servicoId: string;
  nome: string;
  preco: number;
  duracaoMinutos: number;
}

export interface AgendamentoResumo {
  id: string;
  profissionalId: string;
  clienteId: string;
  clienteNome: string;
  inicio: string;
  fim: string;
  status: string;
  observacoes: string | null;
  servicos: string[];
  total: number;
}

export interface CriarAgendamento {
  profissionalId: string;
  clienteId: string;
  servicoIds: string[];
  inicio: string;
  observacoes?: string | null;
}

export interface RespostaConflitoAgendamento {
  title: string;
  proximosHorariosLivres: string[];
}
