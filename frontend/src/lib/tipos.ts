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
  | "GerenciarFidelidade"
  | "EditarCadastros"
  | "ExcluirCadastros"
  | "GerenciarComissoes"
  | "VerComissoesDeTodos"
  | "AjustarValorAtendimento"
  | "LancarAtendimentoSemAgendamento";

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
  { valor: "EditarCadastros", rotulo: "Editar cadastros (usuários, profissionais, serviços)" },
  { valor: "ExcluirCadastros", rotulo: "Excluir cadastros (usuários, profissionais, serviços)" },
  { valor: "GerenciarComissoes", rotulo: "Gerenciar comissões (alterar o percentual)" },
  { valor: "VerComissoesDeTodos", rotulo: "Ver comissões de todos os profissionais" },
  { valor: "AjustarValorAtendimento", rotulo: "Ajustar valor do atendimento (desconto/acréscimo)" },
  { valor: "LancarAtendimentoSemAgendamento", rotulo: "Lançar atendimento sem agendamento (encaixe)" },
];

/** Endereço de usuário/profissional/negócio — todos os campos opcionais. */
export interface Endereco {
  bairro: string | null;
  cidade: string | null;
  rua: string | null;
  numero: string | null;
  cep: string | null;
}

export const ENDERECO_VAZIO: Endereco = { bairro: null, cidade: null, rua: null, numero: null, cep: null };

/** Profissional ativo que executa todos os serviços do agendamento (seção 7). */
export interface OpcaoTransferencia {
  id: string;
  nome: string;
}

export interface AgendamentoFuturoParaExclusao {
  id: string;
  inicio: string;
  fim: string;
  cliente: string;
  servicos: string[];
  profissionaisPossiveis: OpcaoTransferencia[];
}

/** O que a janela de confirmação de exclusão explica (seção 7). */
export interface PreviaExclusao {
  nome: string;
  temHistorico: boolean;
  agendamentosFuturos: number;
  bloqueio: string | null;
  futuros: AgendamentoFuturoParaExclusao[];
}

export interface RespostaExclusao {
  mantidoNoHistorico: boolean;
}

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
  endereco: Endereco | null;
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
  endereco: Endereco | null;
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
  /** Opcional só para o cliente cadastrado no balcão (encaixe, seção 7). */
  telefone: string | null;
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
  /** Avisar o profissional também por WhatsApp (novo/remarcado/cancelado). O e-mail sai sempre. */
  whatsAppAvisoProfissional: boolean;
}

/** Saúde da conexão do WhatsApp (administração da plataforma). */
export type EstadoConexaoWhatsApp = "NaoSeAplica" | "Conectada" | "Desconectada" | "AguardandoQrCode" | "Indisponivel";

export interface SaudeWhatsApp {
  provedor: string;
  estado: EstadoConexaoWhatsApp;
  detalhe: string | null;
  consultadoEm: string;
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

export type EstadoAssinatura = "EmTeste" | "Ativa" | "Atrasada" | "Suspensa" | "Cancelada";

export interface AvisoAssinatura {
  estado: EstadoAssinatura;
  prazo: string | null;
  diasRestantes: number | null;
  destacado: boolean;
}

export interface CobrancaAssinatura {
  pagoEm: string;
  valor: number;
  forma: string;
  periodoInicio: string;
  periodoFim: string;
  origem: string;
}

export interface DetalheAssinatura {
  planoId: string;
  plano: string;
  periodicidade: Periodicidade;
  estado: EstadoAssinatura;
  precoMensalTravado: number;
  valorDoPeriodo: number;
  fimTeste: string | null;
  proximoVencimento: string | null;
  carenciaAte: string | null;
  profissionaisAtivos: number;
  maximoProfissionais: number;
  cobrancas: CobrancaAssinatura[];
}

export interface InstrucoesPagamento {
  link: string | null;
  chavePix: string | null;
  whatsAppContato: string | null;
  texto: string;
}

export const ROTULOS_ESTADO_ASSINATURA: Record<EstadoAssinatura, string> = {
  EmTeste: "Em teste",
  Ativa: "Ativa",
  Atrasada: "Pagamento pendente",
  Suspensa: "Suspensa",
  Cancelada: "Cancelada",
};

/** Comissões dos profissionais (seção 7) — espelha `Plataforma.Aplicacao.Comissoes`. */
export interface TotaisComissao {
  totalComissao: number;
  totalAtendido: number;
  quantidadeServicos: number;
}

export interface ItemComissao {
  agendamentoId: string;
  inicio: string;
  servico: string;
  cliente: string;
  valorCobrado: number;
  percentual: number;
  comissao: number;
}

export interface ComissoesDoProfissional {
  profissionalId: string | null;
  nomeProfissional: string | null;
  percentualAtual: number | null;
  totais: TotaisComissao;
  itens: ItemComissao[];
  pagina: number;
  tamanhoPagina: number;
  totalItens: number;
}

export interface ResumoComissaoProfissional {
  profissionalId: string;
  nome: string;
  ativo: boolean;
  percentualAtual: number;
  totais: TotaisComissao;
}

/** Quinzenas de acerto de comissões (seção 7) — espelha `IServicoQuinzenas`. */
export type EstadoQuinzena = "Aberta" | "Fechada";

export interface QuinzenaResumo {
  id: string;
  inicio: string;
  fim: string;
  estado: EstadoQuinzena;
  fechadoEm: string | null;
  fechadoPor: string | null;
  diasSemPeriodoAntes: number;
}

export interface LinhaQuinzena {
  profissionalId: string;
  nome: string;
  totais: TotaisComissao;
}

export interface AtendimentoPendente {
  agendamentoId: string;
  inicio: string;
  profissional: string;
  status: string;
}

export interface DetalheQuinzena {
  quinzena: QuinzenaResumo;
  parcial: boolean;
  linhas: LinhaQuinzena[];
  pendentes: AtendimentoPendente[];
}

export interface QuinzenaDoProfissional {
  periodoId: string;
  inicio: string;
  fim: string;
  estado: EstadoQuinzena;
  parcial: boolean;
  totais: TotaisComissao;
}

export interface QuinzenasDoProfissional {
  acertoPorQuinzena: boolean;
  quinzenas: QuinzenaDoProfissional[];
}

/** Ajuste de valor durante o atendimento (seção 7) — espelha `IServicoAjustesAtendimento`. */
export type TipoAjusteValor = "Desconto" | "Acrescimo";
export type ModoAjusteValor = "Reais" | "Percentual";

export interface AjusteRegistrado {
  em: string;
  tipo: TipoAjusteValor;
  modo: ModoAjusteValor;
  valorInformado: number;
  valorAntes: number;
  valorDepois: number;
  motivo: string;
  por: string | null;
  aposConclusao: boolean;
}

export interface LinhaValores {
  linhaId: string;
  servico: string;
  precoOriginal: number;
  valorCobrado: number;
  ajustes: AjusteRegistrado[];
}

export interface ValoresAtendimento {
  agendamentoId: string;
  status: string;
  descontoCupom: number;
  total: number;
  podeAjustar: boolean;
  linhas: LinhaValores[];
}
