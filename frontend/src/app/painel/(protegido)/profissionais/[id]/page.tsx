"use client";

import { use, useCallback, useEffect, useState, type FormEvent } from "react";
import Link from "next/link";
import { useAutenticacao } from "@/lib/auth-context";
import { classeBotaoPrimario, classeBotaoSecundario, classeCartao, classeInput, classeLabel } from "@/components/estilos";
import { formatarReais } from "@/lib/formatacao";
import { ErroApi } from "@/lib/api";
import { ACESSO_INICIAL, CamposAcesso, acessoParaApi, type EstadoAcesso } from "@/components/painel/CamposAcesso";
import {
  NOMES_DIAS_SEMANA,
  type BloqueioResumo,
  type IntervaloTrabalho,
  type ProfissionalDetalhe,
  type ProfissionalServicoResumo,
  type ServicoResumo,
} from "@/lib/tipos";

export default function PaginaDetalheProfissional({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  const { temPermissao } = useAutenticacao();
  const podeVerComissao = temPermissao("GerenciarComissoes") || temPermissao("VerComissoesDeTodos");

  return (
    <div className="space-y-8">
      <Link href="/painel/profissionais" className="text-sm text-marca-primaria hover:underline dark:text-marca-acento">
        ← Voltar para profissionais
      </Link>

      <SecaoAcesso profissionalId={id} />
      <SecaoHorariosTrabalho profissionalId={id} />
      <SecaoBloqueios profissionalId={id} />
      <SecaoServicosVinculados profissionalId={id} />
      {podeVerComissao && <SecaoComissao profissionalId={id} podeAlterar={temPermissao("GerenciarComissoes")} />}
    </div>
  );
}

/**
 * Nome do profissional e o acesso dele ao sistema (seção 7): sem usuário vinculado ele é só um nome na
 * agenda; "Dar acesso ao sistema" cria o login (senha agora ou convite por e-mail) a qualquer momento.
 */
function SecaoAcesso({ profissionalId }: { profissionalId: string }) {
  const { chamarApi, temPermissao } = useAutenticacao();
  const podeDarAcesso = temPermissao("GerenciarUsuarios");
  const [profissional, setProfissional] = useState<ProfissionalDetalhe | null>(null);
  const [formularioAberto, setFormularioAberto] = useState(false);
  const [acesso, setAcesso] = useState<EstadoAcesso>({ ...ACESSO_INICIAL, darAcesso: true });
  const [erro, setErro] = useState<string | null>(null);
  const [aviso, setAviso] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);

  const carregar = useCallback(async () => {
    setProfissional(await chamarApi<ProfissionalDetalhe>(`/painel/profissionais/${profissionalId}`));
  }, [chamarApi, profissionalId]);

  useEffect(() => {
    // Busca disparada pela montagem.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    carregar().catch(() => setErro("Não foi possível carregar o profissional."));
  }, [carregar]);

  function abrir() {
    setAcesso({ ...ACESSO_INICIAL, darAcesso: true, email: profissional?.email ?? "" });
    setErro(null);
    setFormularioAberto(true);
  }

  async function darAcesso(evento: FormEvent) {
    evento.preventDefault();
    setErro(null);
    setEnviando(true);
    try {
      await chamarApi(`/painel/profissionais/${profissionalId}/acesso`, { metodo: "POST", corpo: acessoParaApi(acesso) });
      setFormularioAberto(false);
      setAviso(acesso.enviarConvite ? `Convite enviado para ${acesso.email.trim()}.` : "Acesso criado.");
      await carregar();
    } catch (excecao) {
      setErro(excecao instanceof ErroApi ? excecao.message : "Não foi possível dar acesso.");
    } finally {
      setEnviando(false);
    }
  }

  if (!profissional) return erro ? <p className="text-sm text-red-600">{erro}</p> : null;

  return (
    <section className="space-y-3">
      <h1 className="text-lg font-semibold text-gray-900 dark:text-neutral-50">{profissional.nome}</h1>
      <div className={`${classeCartao} space-y-3 p-4 text-sm`}>
        <h2 className="font-semibold text-gray-900 dark:text-neutral-50">Acesso ao sistema</h2>
        {profissional.acesso ? (
          <p className="text-gray-700 dark:text-neutral-300">
            Entra com <strong>{profissional.acesso.email}</strong>
            {!profissional.acesso.ativo && " (acesso desativado)"}.
          </p>
        ) : (
          <p className="text-gray-700 dark:text-neutral-300">Sem acesso: aparece na agenda, mas não entra no sistema.</p>
        )}
        {aviso && <p className="text-green-700 dark:text-green-400">{aviso}</p>}

        {!profissional.acesso && podeDarAcesso && !formularioAberto && (
          <button type="button" className={classeBotaoSecundario} onClick={abrir}>
            Dar acesso ao sistema
          </button>
        )}

        {formularioAberto && (
          <form onSubmit={darAcesso} className="space-y-3">
            <CamposAcesso valor={acesso} aoMudar={setAcesso} perguntar={false} />
            {erro && <p className="text-red-600">{erro}</p>}
            <div className="flex justify-end gap-2">
              <button type="button" className={classeBotaoSecundario} onClick={() => setFormularioAberto(false)}>
                Cancelar
              </button>
              <button type="submit" disabled={enviando} className={classeBotaoPrimario}>
                {enviando ? "Criando..." : "Criar acesso"}
              </button>
            </div>
          </form>
        )}
      </div>
    </section>
  );
}

// Intervalo com uma chave só do front (nunca enviada à API) — mantém a identidade de
// cada linha estável entre re-renderizações agrupadas por dia (o índice no array plano
// muda de sentido quando filtramos por diaSemana, então não dá pra usar como chave/alvo
// de edição).
interface IntervaloEditavel extends IntervaloTrabalho {
  chave: string;
}

function SecaoHorariosTrabalho({ profissionalId }: { profissionalId: string }) {
  const { chamarApi } = useAutenticacao();
  const [intervalos, setIntervalos] = useState<IntervaloEditavel[]>([]);
  const [salvando, setSalvando] = useState(false);
  const [mensagem, setMensagem] = useState<string | null>(null);
  const [diaReplicando, setDiaReplicando] = useState<number | null>(null);
  const [diasAlvo, setDiasAlvo] = useState<number[]>([]);
  // Até o GET chegar, os cartões não aparecem: antes, "Adicionar intervalo" já ficava
  // clicável com a lista vazia e a resposta do carregamento apagava o que foi adicionado.
  const [carregado, setCarregado] = useState(false);

  const carregar = useCallback(async () => {
    const resposta = await chamarApi<IntervaloTrabalho[]>(`/painel/profissionais/${profissionalId}/horarios`);
    setIntervalos(resposta.map((intervalo) => ({ ...intervalo, chave: crypto.randomUUID() })));
    setCarregado(true);
  }, [chamarApi, profissionalId]);

  useEffect(() => {
    // Busca disparada pela montagem, não estado derivado de props.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    carregar();
  }, [carregar]);

  function adicionarIntervalo(dia: number) {
    setIntervalos((atual) => [...atual, { chave: crypto.randomUUID(), diaSemana: dia, inicio: "09:00:00", fim: "18:00:00" }]);
  }

  function removerIntervalo(chave: string) {
    setIntervalos((atual) => atual.filter((i) => i.chave !== chave));
  }

  function atualizarIntervalo(chave: string, alteracao: Partial<IntervaloTrabalho>) {
    setIntervalos((atual) => atual.map((intervalo) => (intervalo.chave === chave ? { ...intervalo, ...alteracao } : intervalo)));
  }

  function abrirReplicar(dia: number) {
    setDiaReplicando(dia);
    setDiasAlvo([]);
  }

  function alternarDiaAlvo(dia: number) {
    setDiasAlvo((atual) => (atual.includes(dia) ? atual.filter((d) => d !== dia) : [...atual, dia]));
  }

  function aplicarReplicar() {
    if (diaReplicando === null || diasAlvo.length === 0) return;

    const nomesAlvo = diasAlvo.map((d) => NOMES_DIAS_SEMANA[d]).join(", ");
    if (!confirm(`Substituir o horário de ${nomesAlvo} pelo horário de ${NOMES_DIAS_SEMANA[diaReplicando]}? Essa ação não pode ser desfeita.`)) {
      return;
    }

    const origem = intervalos.filter((i) => i.diaSemana === diaReplicando);
    setIntervalos((atual) => [
      // Descarta o que já existia nos dias de destino — é uma cópia de valores, não um
      // vínculo: cada dia continua editável depois, independente dos outros.
      ...atual.filter((i) => !diasAlvo.includes(i.diaSemana)),
      ...diasAlvo.flatMap((dia) => origem.map((i) => ({ chave: crypto.randomUUID(), diaSemana: dia, inicio: i.inicio, fim: i.fim }))),
    ]);
    setDiaReplicando(null);
  }

  async function salvar() {
    setSalvando(true);
    setMensagem(null);
    try {
      const corpo: IntervaloTrabalho[] = intervalos.map(({ diaSemana, inicio, fim }) => ({ diaSemana, inicio, fim }));
      await chamarApi(`/painel/profissionais/${profissionalId}/horarios`, { metodo: "PUT", corpo });
      setMensagem("Horário de trabalho salvo.");
    } catch {
      setMensagem("Não foi possível salvar.");
    } finally {
      setSalvando(false);
    }
  }

  return (
    <section>
      <h2 className="mb-3 text-lg font-semibold text-gray-900 dark:text-neutral-50">Horário de trabalho</h2>
      <p className="mb-3 text-sm text-gray-500 dark:text-neutral-400">
        Um dia com almoço vira dois intervalos (ex.: 09:00–12:00 e 13:00–18:00). Use &quot;Replicar para...&quot; para copiar o
        horário de um dia pra outros dias da semana.
      </p>

      {!carregado ? (
        <p className="text-sm text-gray-500 dark:text-neutral-400">Carregando horários...</p>
      ) : (
        <div className="space-y-3">
          {NOMES_DIAS_SEMANA.map((nome, dia) => {
            const doDia = intervalos.filter((i) => i.diaSemana === dia);

            return (
              <div key={dia} className={`${classeCartao} p-4`} data-testid={`dia-horario-${dia}`}>
                <div className="mb-2 flex flex-wrap items-center justify-between gap-2">
                  <h3 className="font-medium text-gray-900 dark:text-neutral-50">{nome}</h3>
                  <div className="flex gap-2">
                    <button type="button" className={classeBotaoSecundario} onClick={() => adicionarIntervalo(dia)}>
                      Adicionar intervalo
                    </button>
                    <button
                      type="button"
                      disabled={doDia.length === 0}
                      className={classeBotaoSecundario}
                      onClick={() => abrirReplicar(dia)}
                    >
                      Replicar para...
                    </button>
                  </div>
                </div>

                <div className="space-y-2">
                  {doDia.map((intervalo) => (
                    <div key={intervalo.chave} className="flex flex-wrap items-center gap-2">
                      <input
                        type="time"
                        className={`${classeInput} w-28`}
                        value={intervalo.inicio.slice(0, 5)}
                        onChange={(e) => atualizarIntervalo(intervalo.chave, { inicio: `${e.target.value}:00` })}
                      />
                      <span>até</span>
                      <input
                        type="time"
                        className={`${classeInput} w-28`}
                        value={intervalo.fim.slice(0, 5)}
                        onChange={(e) => atualizarIntervalo(intervalo.chave, { fim: `${e.target.value}:00` })}
                      />
                      <button
                        type="button"
                        className="text-sm text-red-600 hover:underline dark:text-red-400"
                        onClick={() => removerIntervalo(intervalo.chave)}
                      >
                        Remover
                      </button>
                    </div>
                  ))}
                  {doDia.length === 0 && <p className="text-sm text-gray-500 dark:text-neutral-400">Sem expediente nesse dia.</p>}
                </div>

                {diaReplicando === dia && (
                  <div className="mt-3 space-y-2 rounded-lg border border-marca-acento/40 bg-amber-50 p-3 dark:border-marca-acento/30 dark:bg-amber-950/40">
                    <p className="text-sm font-medium text-gray-700 dark:text-neutral-200">Replicar {nome} para:</p>
                    <div className="flex flex-wrap gap-3">
                      {NOMES_DIAS_SEMANA.map(
                        (nomeAlvo, diaAlvo) =>
                          diaAlvo !== dia && (
                            <label key={diaAlvo} className="flex items-center gap-1 text-sm text-gray-700 dark:text-neutral-200">
                              <input type="checkbox" checked={diasAlvo.includes(diaAlvo)} onChange={() => alternarDiaAlvo(diaAlvo)} />
                              {nomeAlvo}
                            </label>
                          ),
                      )}
                    </div>
                    <div className="flex gap-2">
                      <button type="button" disabled={diasAlvo.length === 0} className={classeBotaoPrimario} onClick={aplicarReplicar}>
                        Aplicar
                      </button>
                      <button type="button" className={classeBotaoSecundario} onClick={() => setDiaReplicando(null)}>
                        Cancelar
                      </button>
                    </div>
                  </div>
                )}
              </div>
            );
          })}
        </div>
      )}

      <div className="mt-3 flex items-center justify-between">
        {/* Salvar antes de carregar gravaria a lista vazia por cima do horário real (PUT substitui tudo). */}
        <button type="button" disabled={salvando || !carregado} className={classeBotaoPrimario} onClick={salvar}>
          {salvando ? "Salvando..." : "Salvar horário"}
        </button>
        {mensagem && <p className="text-sm text-gray-600 dark:text-neutral-300">{mensagem}</p>}
      </div>
    </section>
  );
}

/**
 * Comissão (%) do profissional (seção 7). Só aparece para quem gerencia ou vê as comissões; só
 * quem gerencia altera. Mudar vale para os atendimentos concluídos daqui em diante.
 */
function SecaoComissao({ profissionalId, podeAlterar }: { profissionalId: string; podeAlterar: boolean }) {
  const { chamarApi } = useAutenticacao();
  const [percentual, setPercentual] = useState("");
  const [percentualProduto, setPercentualProduto] = useState("");
  const [acertoPorQuinzena, setAcertoPorQuinzena] = useState(false);
  // O formulário só aparece carregado: senão a resposta, chegando depois, apagaria o que já foi digitado.
  const [carregado, setCarregado] = useState(false);
  const [mensagem, setMensagem] = useState<{ tipo: "ok" | "erro"; texto: string } | null>(null);
  const [salvando, setSalvando] = useState(false);

  useEffect(() => {
    // Busca disparada pela montagem, não estado derivado de props.
    chamarApi<{ percentual: number; acertoPorQuinzena: boolean; percentualProdutoVenda: number | null }>(
      `/painel/profissionais/${profissionalId}/comissao`,
    )
      .then((r) => {
        setPercentual(String(r.percentual).replace(".", ","));
        setPercentualProduto(String(r.percentualProdutoVenda ?? 0).replace(".", ","));
        setAcertoPorQuinzena(r.acertoPorQuinzena);
        setCarregado(true);
      })
      .catch(() => setMensagem({ tipo: "erro", texto: "Não foi possível carregar a comissão." }));
  }, [chamarApi, profissionalId]);

  async function salvar(evento: FormEvent) {
    evento.preventDefault();
    setMensagem(null);
    const valor = Number(percentual.trim().replace(",", "."));
    const valorProduto = Number(percentualProduto.trim().replace(",", "."));
    if (percentual.trim() === "" || Number.isNaN(valor) || percentualProduto.trim() === "" || Number.isNaN(valorProduto)) {
      setMensagem({ tipo: "erro", texto: "Informe um número de 0 a 100." });
      return;
    }

    setSalvando(true);
    try {
      await chamarApi(`/painel/profissionais/${profissionalId}/comissao`, {
        metodo: "PUT",
        corpo: { percentual: valor, acertoPorQuinzena, percentualProdutoVenda: valorProduto },
      });
      setMensagem({ tipo: "ok", texto: "Comissão salva. Vale para os atendimentos concluídos e as vendas feitas a partir de agora." });
    } catch (excecao) {
      setMensagem({ tipo: "erro", texto: excecao instanceof Error ? excecao.message : "Não foi possível salvar." });
    } finally {
      setSalvando(false);
    }
  }

  return (
    <section>
      <h2 className="mb-1 text-lg font-semibold text-gray-900 dark:text-neutral-50">Comissão</h2>
      <p className="mb-3 text-sm text-gray-500 dark:text-neutral-400">
        Percentual sobre o valor cobrado de cada serviço concluído (já com o desconto de cupom). Mudar não altera atendimentos já concluídos.
      </p>
      {!carregado ? (
        !mensagem && <p className="text-sm text-gray-500 dark:text-neutral-400">Carregando...</p>
      ) : (
        <>
          <form onSubmit={salvar} className="flex flex-wrap items-end gap-2">
            <label>
              <span className={classeLabel}>Comissão (%)</span>
              <input
                inputMode="decimal"
                className={`${classeInput} w-32`}
                value={percentual}
                onChange={(e) => setPercentual(e.target.value)}
                disabled={!podeAlterar}
                aria-describedby="ajuda-comissao"
              />
            </label>
            <label>
              <span className={classeLabel}>Comissão de produto (%)</span>
              <input
                inputMode="decimal"
                className={`${classeInput} w-32`}
                value={percentualProduto}
                onChange={(e) => setPercentualProduto(e.target.value)}
                disabled={!podeAlterar}
                aria-describedby="ajuda-comissao"
              />
            </label>
            {podeAlterar && (
              <button type="submit" disabled={salvando} className={classeBotaoPrimario}>
                {salvando ? "Salvando..." : "Salvar comissão"}
              </button>
            )}
          </form>
          <p id="ajuda-comissao" className="mt-1 text-xs text-gray-500 dark:text-neutral-400">
            De 0 a 100, com até duas casas decimais (ex.: 40 ou 12,5). A de produto vale para as vendas em que ele for o vendedor.
          </p>
          <label className="mt-3 flex items-start gap-2 text-sm text-gray-700 dark:text-neutral-300">
            <input
              type="checkbox"
              className="mt-0.5"
              checked={acertoPorQuinzena}
              onChange={(e) => setAcertoPorQuinzena(e.target.checked)}
              disabled={!podeAlterar}
            />
            <span>
              Acerto por quinzena
              <span className="block text-xs text-gray-500 dark:text-neutral-400">
                Entra no fechamento de comissões das quinzenas. Salve para aplicar.
              </span>
            </span>
          </label>
        </>
      )}
      {mensagem && (
        <p role={mensagem.tipo === "erro" ? "alert" : "status"} className={`mt-2 text-sm ${mensagem.tipo === "erro" ? "text-red-600 dark:text-red-400" : "text-green-700 dark:text-green-400"}`}>
          {mensagem.texto}
        </p>
      )}
    </section>
  );
}

function SecaoBloqueios({ profissionalId }: { profissionalId: string }) {
  const { chamarApi } = useAutenticacao();
  const [bloqueios, setBloqueios] = useState<BloqueioResumo[]>([]);
  const [inicio, setInicio] = useState("");
  const [fim, setFim] = useState("");
  const [motivo, setMotivo] = useState("");
  const [erro, setErro] = useState<string | null>(null);

  const carregar = useCallback(async () => {
    setBloqueios(await chamarApi<BloqueioResumo[]>(`/painel/profissionais/${profissionalId}/bloqueios`));
  }, [chamarApi, profissionalId]);

  useEffect(() => {
    // Busca disparada pela montagem, não estado derivado de props.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    carregar();
  }, [carregar]);

  async function criar(evento: FormEvent) {
    evento.preventDefault();
    setErro(null);
    try {
      await chamarApi(`/painel/profissionais/${profissionalId}/bloqueios`, {
        metodo: "POST",
        corpo: { inicioUtc: new Date(inicio).toISOString(), fimUtc: new Date(fim).toISOString(), motivo: motivo || null },
      });
      setInicio("");
      setFim("");
      setMotivo("");
      await carregar();
    } catch {
      setErro("Não foi possível criar o bloqueio.");
    }
  }

  async function remover(id: string) {
    await chamarApi(`/painel/bloqueios/${id}`, { metodo: "DELETE" });
    await carregar();
  }

  return (
    <section>
      <h2 className="mb-3 text-lg font-semibold text-gray-900 dark:text-neutral-50">Folgas e bloqueios</h2>

      <div className={`${classeCartao} mb-3 divide-y divide-gray-100 dark:divide-neutral-800`}>
        {bloqueios.map((b) => (
          <div key={b.id} className="flex items-center justify-between px-4 py-2 text-sm">
            <span>
              {new Date(b.inicioUtc).toLocaleString("pt-BR")} até {new Date(b.fimUtc).toLocaleString("pt-BR")}
              {b.motivo && ` — ${b.motivo}`}
            </span>
            <button className="text-red-600 hover:underline dark:text-red-400" onClick={() => remover(b.id)}>
              Remover
            </button>
          </div>
        ))}
        {bloqueios.length === 0 && <p className="px-4 py-3 text-sm text-gray-500 dark:text-neutral-400">Nenhum bloqueio cadastrado.</p>}
      </div>

      <form onSubmit={criar} className="flex flex-wrap items-end gap-2">
        <label>
          <span className={classeLabel}>Início</span>
          <input required type="datetime-local" className={classeInput} value={inicio} onChange={(e) => setInicio(e.target.value)} />
        </label>
        <label>
          <span className={classeLabel}>Fim</span>
          <input required type="datetime-local" className={classeInput} value={fim} onChange={(e) => setFim(e.target.value)} />
        </label>
        <label>
          <span className={classeLabel}>Motivo (opcional)</span>
          <input className={classeInput} value={motivo} onChange={(e) => setMotivo(e.target.value)} />
        </label>
        <button type="submit" className={classeBotaoPrimario}>
          Adicionar bloqueio
        </button>
      </form>
      {erro && <p className="mt-2 text-sm text-red-600">{erro}</p>}
    </section>
  );
}

function SecaoServicosVinculados({ profissionalId }: { profissionalId: string }) {
  const { chamarApi } = useAutenticacao();
  const [vinculados, setVinculados] = useState<ProfissionalServicoResumo[]>([]);
  const [todos, setTodos] = useState<ServicoResumo[]>([]);
  const [servicoParaVincular, setServicoParaVincular] = useState("");
  // Preço/duração próprios deste profissional (seção 7) — edição de um serviço por vez.
  const [editando, setEditando] = useState<{ servicoId: string; preco: string; duracao: string } | null>(null);

  const carregar = useCallback(async () => {
    const [vinculosAtuais, listaServicos] = await Promise.all([
      chamarApi<ProfissionalServicoResumo[]>(`/painel/profissionais/${profissionalId}/servicos`),
      chamarApi<ServicoResumo[]>("/painel/servicos"),
    ]);
    setVinculados(vinculosAtuais);
    setTodos(listaServicos.filter((s) => s.ativo));
  }, [chamarApi, profissionalId]);

  useEffect(() => {
    // Busca disparada pela montagem, não estado derivado de props.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    carregar();
  }, [carregar]);

  async function vincular() {
    if (!servicoParaVincular) return;
    await chamarApi(`/painel/profissionais/${profissionalId}/servicos`, {
      metodo: "POST",
      corpo: { servicoId: servicoParaVincular, precoPersonalizado: null, duracaoPersonalizadaMinutos: null },
    });
    setServicoParaVincular("");
    await carregar();
  }

  async function salvarPrecoProprio(servicoId: string, preco: number | null, duracao: number | null) {
    await chamarApi(`/painel/profissionais/${profissionalId}/servicos`, {
      metodo: "POST",
      corpo: { servicoId, precoPersonalizado: preco, duracaoPersonalizadaMinutos: duracao },
    });
    setEditando(null);
    await carregar();
  }

  async function desvincular(servicoId: string) {
    await chamarApi(`/painel/profissionais/${profissionalId}/servicos/${servicoId}`, { metodo: "DELETE" });
    await carregar();
  }

  const disponiveisParaVincular = todos.filter((s) => !vinculados.some((v) => v.servicoId === s.id));

  return (
    <section>
      <h2 className="mb-3 text-lg font-semibold text-gray-900 dark:text-neutral-50">Serviços executados</h2>

      <div className={`${classeCartao} mb-3 divide-y divide-gray-100 dark:divide-neutral-800`}>
        {vinculados.map((v) => {
          const padrao = todos.find((s) => s.id === v.servicoId);
          const proprio = padrao !== undefined && (padrao.preco !== v.preco || padrao.duracaoMinutos !== v.duracaoMinutos);

          if (editando?.servicoId === v.servicoId) {
            return (
              <form
                key={v.servicoId}
                className="flex flex-wrap items-end gap-2 px-4 py-2 text-sm"
                onSubmit={(e) => {
                  e.preventDefault();
                  salvarPrecoProprio(v.servicoId, Number(editando.preco.replace(",", ".")), Number(editando.duracao));
                }}
              >
                <span className="w-full font-medium">{v.nome}</span>
                <label>
                  <span className={classeLabel}>Preço (R$)</span>
                  <input
                    required
                    inputMode="decimal"
                    className={`${classeInput} w-28`}
                    value={editando.preco}
                    onChange={(e) => setEditando({ ...editando, preco: e.target.value })}
                  />
                </label>
                <label>
                  <span className={classeLabel}>Duração (min)</span>
                  <input
                    required
                    type="number"
                    min={5}
                    className={`${classeInput} w-24`}
                    value={editando.duracao}
                    onChange={(e) => setEditando({ ...editando, duracao: e.target.value })}
                  />
                </label>
                <button type="submit" className={classeBotaoPrimario}>
                  Salvar
                </button>
                {proprio && (
                  <button type="button" className={classeBotaoSecundario} onClick={() => salvarPrecoProprio(v.servicoId, null, null)}>
                    Usar o padrão do serviço
                  </button>
                )}
                <button type="button" className={classeBotaoSecundario} onClick={() => setEditando(null)}>
                  Cancelar
                </button>
              </form>
            );
          }

          return (
            <div key={v.servicoId} className="flex flex-wrap items-center justify-between gap-2 px-4 py-2 text-sm">
              <span>
                {v.nome} — {formatarReais(v.preco)} ({v.duracaoMinutos} min)
                {proprio && <span className="ml-2 text-xs text-gray-500 dark:text-neutral-400">preço próprio</span>}
              </span>
              <span className="space-x-3">
                <button
                  className="text-marca-primaria hover:underline dark:text-marca-acento"
                  onClick={() => setEditando({ servicoId: v.servicoId, preco: v.preco.toFixed(2).replace(".", ","), duracao: String(v.duracaoMinutos) })}
                >
                  Preço e duração
                </button>
                <button className="text-red-600 hover:underline dark:text-red-400" onClick={() => desvincular(v.servicoId)}>
                  Remover
                </button>
              </span>
            </div>
          );
        })}
        {vinculados.length === 0 && <p className="px-4 py-3 text-sm text-gray-500 dark:text-neutral-400">Nenhum serviço vinculado ainda.</p>}
      </div>

      {disponiveisParaVincular.length > 0 && (
        <div className="flex items-end gap-2">
          <label>
            <span className={classeLabel}>Vincular serviço</span>
            <select className={classeInput} value={servicoParaVincular} onChange={(e) => setServicoParaVincular(e.target.value)}>
              <option value="">Selecione...</option>
              {disponiveisParaVincular.map((s) => (
                <option key={s.id} value={s.id}>
                  {s.nome}
                </option>
              ))}
            </select>
          </label>
          <button type="button" className={classeBotaoPrimario} onClick={vincular}>
            Vincular
          </button>
        </div>
      )}
    </section>
  );
}
