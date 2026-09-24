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
  funcao: string | null;
}

export interface ProfissionalDetalhe {
  id: string;
  nome: string;
  telefone: string | null;
  email: string | null;
  ativo: boolean;
  fotoUrl: string | null;
  cpfMascarado: string | null;
  funcao: string | null;
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
  excluido: boolean;
}

export interface ExportacaoAgendamento {
  inicio: string;
  fim: string;
  status: string;
  servicos: string[];
  total: number;
  observacoes: string | null;
}

export interface ExportacaoCliente {
  id: string;
  nome: string;
  telefone: string;
  email: string | null;
  observacoes: string | null;
  origem: string;
  criadoEm: string;
  agendamentos: ExportacaoAgendamento[];
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
  emailContato: string | null;
  instagram: string | null;
  facebook: string | null;
  whatsApp: string | null;
  whatsAppAtivoParaConfirmacoes: boolean;
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
  clienteId: string | null;
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

export interface CupomResumo {
  id: string;
  codigo: string;
  tipo: "Percentual" | "ValorFixo";
  valor: number;
  validoAte: string | null;
  limiteUsos: number | null;
  usosAtuais: number;
  ativo: boolean;
  servicoIdsEscopo: string[];
}

// --- Sprint 3: página pública, assistente e código de confirmação ---

export interface NegocioPublico {
  id: string;
  slug: string;
  nomeExibido: string;
  tipo: string;
  fuso: string;
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
  aceitaAgendamentoOnline: boolean;
}

export interface ServicoPublico {
  id: string;
  nome: string;
  preco: number;
  duracaoMinutos: number;
  popular: boolean;
}

export interface CategoriaComServicosPublicos {
  categoriaId: string;
  nome: string;
  servicos: ServicoPublico[];
}

export interface ProfissionalPublico {
  id: string;
  nome: string;
  fotoUrl: string | null;
  funcao: string | null;
  servicoIds: string[];
}

export interface HorarioLivrePublico {
  inicio: string;
  profissionalId: string;
}

export interface CriarReservaPublica {
  profissionalId: string;
  servicoIds: string[];
  inicio: string;
}

export interface ConfirmarAgendamentoPublico {
  agendamentoId: string;
  tokenVerificacao: string;
  nome: string;
  telefone: string;
  email?: string | null;
  observacoes?: string | null;
  codigoCupom?: string | null;
}

export interface DetalhePublicoAgendamento {
  id: string;
  nomeNegocio: string;
  local: string;
  inicio: string;
  fim: string;
  servicos: string[];
  total: number;
  status: string;
}

// --- Sprint 4: notificações e financeiro ---

export type FormaPagamento = "Dinheiro" | "Cartao" | "Pix" | "Outro";

export const FORMAS_PAGAMENTO: { valor: FormaPagamento; rotulo: string }[] = [
  { valor: "Dinheiro", rotulo: "Dinheiro" },
  { valor: "Cartao", rotulo: "Cartão" },
  { valor: "Pix", rotulo: "Pix" },
  { valor: "Outro", rotulo: "Outro" },
];

export interface RegistrarPagamento {
  agendamentoId: string;
  valor: number;
  forma: FormaPagamento;
}

export interface PagamentoResumo {
  id: string;
  agendamentoId: string;
  valor: number;
  forma: FormaPagamento;
  criadoEm: string;
}

export interface FaturamentoPorProfissional {
  profissionalId: string;
  nomeProfissional: string;
  total: number;
  quantidade: number;
}

export interface FaturamentoPorServico {
  servicoId: string;
  nomeServico: string;
  total: number;
  quantidade: number;
}

export interface ResumoFinanceiro {
  total: number;
  quantidadeAtendimentos: number;
  porProfissional: FaturamentoPorProfissional[];
  porServico: FaturamentoPorServico[];
}

// --- Sprint 5: fidelidade e LGPD ---

export interface ProgramaFidelidadeResumo {
  selosNecessarios: number;
  descricaoRecompensa: string;
  ativo: boolean;
}

export interface DefinirProgramaFidelidade {
  selosNecessarios: number;
  descricaoRecompensa: string;
}

export interface ProgressoFidelidade {
  selosAtuais: number;
  selosNecessarios: number;
  podeResgatar: boolean;
  descricaoRecompensa: string | null;
}


// --- Ajuste 4: planos, cadastro e primeiros passos (seções 6.4, 6.5 e 7) ---

export type Periodicidade = "Mensal" | "Anual";

export interface PlanoPublico {
  id: string;
  nome: string;
  minimoProfissionais: number;
  maximoProfissionais: number;
  precoMensal: number;
  precoAnualPorMes: number;
  destaque: boolean;
}

export interface DisponibilidadeSlug {
  disponivel: boolean;
  motivo: string | null;
}

export interface PrimeirosPassos {
  servicosCadastrados: boolean;
  profissionaisCadastrados: boolean;
  horariosConfigurados: boolean;
  linkCopiado: boolean;
  dispensado: boolean;
  linkAgendamento: string;
  exibir: boolean;
}

export const TIPOS_NEGOCIO: { valor: string; rotulo: string }[] = [
  { valor: "Barbearia", rotulo: "Barbearia" },
  { valor: "Salao", rotulo: "Salão de beleza" },
  { valor: "ClinicaEstetica", rotulo: "Clínica de estética" },
  { valor: "Autonomo", rotulo: "Profissional autônomo" },
];
