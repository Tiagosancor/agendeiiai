import type { ConfiguracaoLembreteRetorno, RelatorioLembreteRetorno } from "@/lib/tipos";

type ChamarApi = <T>(caminho: string, opcoes?: {
  metodo?: "GET" | "POST" | "PUT" | "PATCH" | "DELETE";
  corpo?: unknown;
}) => Promise<T>;

export function criarLembreteRetornoService(chamarApi: ChamarApi) {
  return {
    obterConfiguracao: () =>
      chamarApi<ConfiguracaoLembreteRetorno>("/painel/configuracoes/lembrete-retorno"),
    salvarConfiguracao: (configuracao: ConfiguracaoLembreteRetorno) =>
      chamarApi<void>("/painel/configuracoes/lembrete-retorno", { metodo: "PUT", corpo: configuracao }),
    obterRelatorio: (dias = 30) =>
      chamarApi<RelatorioLembreteRetorno>(`/painel/lembrete-retorno/relatorio?dias=${dias}`),
    definirAceiteCliente: (clienteId: string, aceita: boolean) =>
      chamarApi<void>(`/painel/clientes/${clienteId}/aceita-lembrete-retorno`, {
        metodo: "PATCH",
        corpo: { aceita },
      }),
  };
}
