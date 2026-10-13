import type { ClienteBusca } from "@/lib/tipos";

export interface CadastroRapidoCliente {
  nome: string;
  telefone: string;
  email: string | null;
}

type OpcoesChamada = {
  metodo?: "GET" | "POST" | "PUT" | "PATCH" | "DELETE";
  corpo?: unknown;
  sinal?: AbortSignal;
};

type ChamarApi = <T>(caminho: string, opcoes?: OpcoesChamada) => Promise<T>;

/** Isola o contrato da busca/cadastro rápido para a Agenda não depender dos detalhes da API. */
export function criarClientesAgendaService(chamarApi: ChamarApi) {
  return {
    buscar(termo: string, sinal?: AbortSignal): Promise<ClienteBusca[]> {
      const parametros = new URLSearchParams({ termo: termo.trim(), limite: "10" });
      return chamarApi<ClienteBusca[]>(`/painel/agenda/clientes/busca?${parametros}`, { sinal });
    },

    cadastrar(dados: CadastroRapidoCliente): Promise<ClienteBusca> {
      return chamarApi<ClienteBusca>("/painel/agenda/clientes/rapido", { metodo: "POST", corpo: dados });
    },
  };
}
