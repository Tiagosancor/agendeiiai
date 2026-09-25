"use client";

import Link from "next/link";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { ErroApi, requisicaoApi, requisicaoAutenticacaoPainel } from "@/lib/api";
import type { DisponibilidadeSlug, Periodicidade, PlanoPublico } from "@/lib/tipos";
import { TIPOS_NEGOCIO } from "@/lib/tipos";
import { faixaProfissionais, forcaSenha, formatarReais, paraE164, sugerirSlug } from "@/lib/formatacao";
import { classeInput, classeLabel } from "@/components/estilos";
import { CaptchaTurnstile } from "@/components/CaptchaTurnstile";

type Etapa = 1 | 2 | 3 | 4 | 5;

type SituacaoSlug = { estado: "vazio" | "verificando" | "livre" | "indisponivel"; motivo?: string | null };

const ROTULOS_FORCA = ["Fraca", "Razoável", "Boa", "Forte"];

/**
 * Cadastro de negócio novo em etapas (seção 6.5), no mesmo formato do assistente de
 * agendamento: barra de progresso, rodapé fixo com o resumo do plano e Continuar. A conta
 * só é criada depois do código do e-mail validado; a `Idempotency-Key` é gerada uma vez
 * por cadastro, então clique duplo ou reenvio nunca cria dois negócios.
 */
export function AssistenteCadastro({
  dominio,
  planoInicialId,
  periodicidadeInicial,
  chaveSiteCaptcha,
}: {
  dominio: string;
  planoInicialId: string | null;
  periodicidadeInicial: Periodicidade;
  chaveSiteCaptcha: string | null;
}) {
  const [etapa, setEtapa] = useState<Etapa>(1);
  const [erro, setErro] = useState<string | null>(null);

  const [planos, setPlanos] = useState<PlanoPublico[] | null>(null);
  const [planoId, setPlanoId] = useState<string | null>(planoInicialId);
  const [periodicidade, setPeriodicidade] = useState<Periodicidade>(periodicidadeInicial);

  const [nomeNegocio, setNomeNegocio] = useState("");
  const [tipo, setTipo] = useState("");
  // Sem edição manual, o endereço acompanha o nome; depois de editado, vale o que a pessoa digitou.
  const [slugManual, setSlugManual] = useState<string | null>(null);
  const slug = slugManual ?? sugerirSlug(nomeNegocio);
  const [verificacaoSlug, setVerificacaoSlug] = useState<{ slug: string; situacao: SituacaoSlug } | null>(null);
  const [slugRecusadoNoServidor, setSlugRecusadoNoServidor] = useState<{ slug: string; motivo: string } | null>(null);

  const [nome, setNome] = useState("");
  const [email, setEmail] = useState("");
  const [telefone, setTelefone] = useState("");
  const [senha, setSenha] = useState("");
  const [aceite, setAceite] = useState(false);

  const [codigo, setCodigo] = useState("");
  const [podeReenviarEm, setPodeReenviarEm] = useState(0);
  const [agora, setAgora] = useState(() => Date.now());
  const [processando, setProcessando] = useState(false);

  const [tokenCaptcha, setTokenCaptcha] = useState<string | null>(null);
  const [versaoCaptcha, setVersaoCaptcha] = useState(0);
  const chaveIdempotencia = useRef<string | null>(null);
  // O código é de uso único: numa nova tentativa (ex.: queda de conexão), reaproveita o token já obtido.
  const tokenCadastro = useRef<string | null>(null);

  useEffect(() => {
    requisicaoApi<PlanoPublico[]>("/cadastro/planos")
      .then((lista) => {
        setPlanos(lista);
        setPlanoId((atual) => (atual && lista.some((p) => p.id === atual) ? atual : null));
      })
      .catch(() => setErro("Não foi possível carregar os planos. Recarregue a página."));
  }, []);

  // Checagem de disponibilidade enquanto digita (só conveniência — o servidor revalida).
  useEffect(() => {
    if (!slug) return;
    const temporizador = setTimeout(() => {
      requisicaoApi<DisponibilidadeSlug>(`/cadastro/slugs/${encodeURIComponent(slug)}`)
        .then((r) => setVerificacaoSlug({ slug, situacao: { estado: r.disponivel ? "livre" : "indisponivel", motivo: r.motivo } }))
        .catch(() =>
          setVerificacaoSlug({ slug, situacao: { estado: "indisponivel", motivo: "Não deu para verificar agora. Tente de novo." } }),
        );
    }, 400);
    return () => clearTimeout(temporizador);
  }, [slug]);

  const situacaoSlug: SituacaoSlug = !slug
    ? { estado: "vazio" }
    : slugRecusadoNoServidor?.slug === slug
      ? { estado: "indisponivel", motivo: slugRecusadoNoServidor.motivo }
      : verificacaoSlug?.slug === slug
        ? verificacaoSlug.situacao
        : { estado: "verificando" };

  useEffect(() => {
    if (etapa !== 4) return;
    const intervalo = setInterval(() => setAgora(Date.now()), 1000);
    return () => clearInterval(intervalo);
  }, [etapa]);

  const plano = planos?.find((p) => p.id === planoId) ?? null;
  const precoPorMes = plano ? (periodicidade === "Anual" ? plano.precoAnualPorMes : plano.precoMensal) : null;
  const telefoneE164 = paraE164(telefone);
  const nivelSenha = forcaSenha(senha);

  const dadosValidos = useMemo(
    () =>
      nome.trim().length > 0 &&
      /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email.trim()) &&
      /^\+[1-9]\d{7,14}$/.test(telefoneE164) &&
      nivelSenha > 0 &&
      aceite,
    [nome, email, telefoneE164, nivelSenha, aceite],
  );
  const captchaPronto = !chaveSiteCaptcha || tokenCaptcha !== null;

  const usarCaptcha = useCallback(() => {
    const token = tokenCaptcha;
    if (chaveSiteCaptcha) setVersaoCaptcha((v) => v + 1);
    return token;
  }, [tokenCaptcha, chaveSiteCaptcha]);

  async function enviarCodigo() {
    setErro(null);
    setProcessando(true);
    try {
      await requisicaoApi("/cadastro/codigos", {
        metodo: "POST",
        corpo: { email: email.trim().toLowerCase(), tokenCaptcha: usarCaptcha() },
      });
      setPodeReenviarEm(Date.now() + 60_000);
      setCodigo("");
      tokenCadastro.current = null;
      setEtapa(4);
    } catch (excecao) {
      setErro(excecao instanceof ErroApi ? excecao.message : "Não foi possível enviar o código. Tente de novo.");
    } finally {
      setProcessando(false);
    }
  }

  async function confirmarECriarConta() {
    setErro(null);
    setProcessando(true);
    const emailNormalizado = email.trim().toLowerCase();

    try {
      tokenCadastro.current ??= (
        await requisicaoApi<{ tokenCadastro: string }>("/cadastro/codigos/validar", {
          metodo: "POST",
          corpo: { email: emailNormalizado, codigo },
        })
      ).tokenCadastro;

      chaveIdempotencia.current ??= crypto.randomUUID();
      await requisicaoApi("/cadastro", {
        metodo: "POST",
        cabecalhos: { "Idempotency-Key": chaveIdempotencia.current },
        corpo: {
          tokenCadastro: tokenCadastro.current,
          planoId,
          periodicidade,
          nomeNegocio: nomeNegocio.trim(),
          tipoNegocio: tipo,
          slug,
          nome: nome.trim(),
          email: emailNormalizado,
          telefone: telefoneE164,
          senha,
          aceiteTermos: aceite,
          tokenCaptcha: usarCaptcha(),
        },
      });

      // Entra pelo proxy same-origin do painel: o cookie de sessão nasce no domínio certo.
      await requisicaoAutenticacaoPainel("login", { email: emailNormalizado, senha });
      setEtapa(5);
    } catch (excecao) {
      if (excecao instanceof ErroApi && excecao.codigo === "SlugEmUso") {
        setSlugManual(slug);
        setSlugRecusadoNoServidor({ slug, motivo: excecao.message });
        setEtapa(2);
      }
      // Dados mudaram depois de uma falha: próxima tentativa é outro cadastro, com outra chave.
      if (excecao instanceof ErroApi && excecao.status !== 400) chaveIdempotencia.current = null;
      if (excecao instanceof ErroApi && excecao.status === 401) tokenCadastro.current = null;
      setErro(excecao instanceof ErroApi ? excecao.message : "Não foi possível criar a conta. Tente de novo.");
    } finally {
      setProcessando(false);
    }
  }

  const podeContinuar =
    (etapa === 1 && plano !== null) ||
    (etapa === 2 && nomeNegocio.trim().length > 0 && tipo !== "" && situacaoSlug.estado === "livre") ||
    (etapa === 3 && dadosValidos && captchaPronto) ||
    (etapa === 4 && /^\d{6}$/.test(codigo) && captchaPronto);

  function continuar() {
    setErro(null);
    if (etapa === 1) setEtapa(2);
    else if (etapa === 2) setEtapa(3);
    else if (etapa === 3) enviarCodigo();
    else if (etapa === 4) confirmarECriarConta();
  }

  const segundosParaReenviar = Math.max(0, Math.ceil((podeReenviarEm - agora) / 1000));
  const linkPublico = `${slug || "seu-negocio"}.${dominio}`;

  return (
    <div data-testid="assistente-cadastro" className="fixed inset-0 z-40 flex flex-col bg-background">
      <div className="flex items-center justify-between border-b border-gray-200 px-4 py-3 dark:border-neutral-800">
        {etapa > 1 && etapa < 5 ? (
          <button onClick={() => setEtapa((e) => (e - 1) as Etapa)} className="text-sm text-gray-600 dark:text-neutral-400">
            ← Voltar
          </button>
        ) : (
          <span className="w-14" />
        )}
        <div className="flex gap-1" aria-label={`Etapa ${Math.min(etapa, 4)} de 4`}>
          {[1, 2, 3, 4].map((n) => (
            <span key={n} className={`h-1.5 w-8 rounded-full ${etapa >= n ? "bg-marca-primaria dark:bg-marca-acento" : "bg-gray-200 dark:bg-neutral-800"}`} />
          ))}
        </div>
        <Link href="/" aria-label="Fechar" className="w-14 text-right text-gray-400 hover:text-gray-700 dark:hover:text-neutral-200">
          ✕
        </Link>
      </div>

      <div className="flex-1 overflow-y-auto">
        <div className="mx-auto w-full max-w-lg px-4 py-6">
          {erro && (
            <p role="alert" className="mb-4 rounded-lg bg-red-50 px-3 py-2 text-sm text-red-700 dark:bg-red-950 dark:text-red-300">
              {erro}
            </p>
          )}

          {etapa === 1 && (
            <section>
              <h1 className="font-display mb-1 text-2xl font-bold">Escolha seu plano</h1>
              <p className="mb-5 text-sm text-gray-600 dark:text-neutral-400">
                Todos têm os mesmos recursos. Muda só quantos profissionais atendem pela agenda.
              </p>

              <div role="radiogroup" aria-label="Periodicidade" className="mb-4 inline-flex rounded-lg border border-gray-300 p-1 dark:border-neutral-700">
                {(["Mensal", "Anual"] as Periodicidade[]).map((p) => (
                  <button
                    key={p}
                    role="radio"
                    aria-checked={periodicidade === p}
                    onClick={() => setPeriodicidade(p)}
                    className={`rounded-md px-4 py-1.5 text-sm font-medium ${periodicidade === p ? "bg-marca-primaria text-white dark:bg-marca-acento dark:text-marca-primaria" : "text-gray-700 dark:text-neutral-300"}`}
                  >
                    {p}
                  </button>
                ))}
              </div>

              <div role="radiogroup" aria-label="Plano" className="space-y-3">
                {planos === null && !erro && <p className="text-sm text-gray-500">Carregando planos...</p>}
                {planos?.map((p) => {
                  const escolhido = p.id === planoId;
                  const porMes = periodicidade === "Anual" ? p.precoAnualPorMes : p.precoMensal;
                  return (
                    <button
                      key={p.id}
                      role="radio"
                      aria-checked={escolhido}
                      onClick={() => setPlanoId(p.id)}
                      className={`flex w-full items-center justify-between rounded-xl border-2 px-4 py-3 text-left transition ${
                        escolhido ? "border-marca-primaria dark:border-marca-acento" : "border-gray-200 dark:border-neutral-800"
                      }`}
                    >
                      <span>
                        <span className="flex items-center gap-2 font-semibold">
                          {p.nome}
                          {p.destaque && <span className="rounded-full bg-marca-acento/15 px-2 py-0.5 text-xs font-medium text-marca-acento">Mais escolhido</span>}
                        </span>
                        <span className="text-sm text-gray-600 dark:text-neutral-400">{faixaProfissionais(p)}</span>
                      </span>
                      <span className="text-right">
                        <span className="block font-semibold">{formatarReais(porMes)}/mês</span>
                        {periodicidade === "Anual" && (
                          <span className="block text-xs text-gray-500 dark:text-neutral-400">{formatarReais(porMes * 12)} por ano</span>
                        )}
                      </span>
                    </button>
                  );
                })}
              </div>
            </section>
          )}

          {etapa === 2 && (
            <section className="space-y-4">
              <div>
                <h1 className="font-display mb-1 text-2xl font-bold">Seu negócio</h1>
                <p className="text-sm text-gray-600 dark:text-neutral-400">É assim que seus clientes vão ver você.</p>
              </div>

              <label className="block">
                <span className={classeLabel}>Nome do negócio</span>
                <input className={classeInput} value={nomeNegocio} onChange={(e) => setNomeNegocio(e.target.value)} maxLength={120} autoFocus />
              </label>

              <label className="block">
                <span className={classeLabel}>Tipo</span>
                <select className={classeInput} value={tipo} onChange={(e) => setTipo(e.target.value)}>
                  <option value="">Selecione</option>
                  {TIPOS_NEGOCIO.map((t) => (
                    <option key={t.valor} value={t.valor}>
                      {t.rotulo}
                    </option>
                  ))}
                </select>
              </label>

              <label className="block">
                <span className={classeLabel}>Endereço da sua página</span>
                <div className="flex items-center rounded-lg border border-gray-300 focus-within:border-marca-primaria dark:border-neutral-700 dark:focus-within:border-marca-acento">
                  <input
                    className="w-full min-w-0 rounded-l-lg bg-transparent px-3 py-2 text-sm outline-none dark:text-neutral-50"
                    value={slug}
                    onChange={(e) => setSlugManual(e.target.value.toLowerCase().replace(/[^a-z0-9-]/g, "").slice(0, 30))}
                    aria-describedby="situacao-slug"
                  />
                  <span className="shrink-0 pr-3 text-sm text-gray-500 dark:text-neutral-400">.{dominio}</span>
                </div>
                <p id="situacao-slug" className="mt-1 text-sm" aria-live="polite">
                  {situacaoSlug.estado === "verificando" && <span className="text-gray-500">Verificando...</span>}
                  {situacaoSlug.estado === "livre" && <span className="text-green-700 dark:text-green-400">Disponível: {linkPublico}</span>}
                  {situacaoSlug.estado === "indisponivel" && <span className="text-red-700 dark:text-red-400">{situacaoSlug.motivo}</span>}
                </p>
              </label>
            </section>
          )}

          {etapa === 3 && (
            <section className="space-y-4">
              <div>
                <h1 className="font-display mb-1 text-2xl font-bold">Seus dados de acesso</h1>
                <p className="text-sm text-gray-600 dark:text-neutral-400">Você vai entrar no painel com este e-mail e senha.</p>
              </div>

              <label className="block">
                <span className={classeLabel}>Seu nome</span>
                <input className={classeInput} value={nome} onChange={(e) => setNome(e.target.value)} autoComplete="name" />
              </label>
              <label className="block">
                <span className={classeLabel}>E-mail</span>
                <input className={classeInput} type="email" value={email} onChange={(e) => setEmail(e.target.value)} autoComplete="email" />
              </label>
              <label className="block">
                <span className={classeLabel}>Telefone (WhatsApp)</span>
                <input
                  className={classeInput}
                  type="tel"
                  value={telefone}
                  onChange={(e) => setTelefone(e.target.value)}
                  placeholder="(71) 99999-9999"
                  autoComplete="tel"
                />
              </label>
              <label className="block">
                <span className={classeLabel}>Senha</span>
                <input
                  className={classeInput}
                  type="password"
                  value={senha}
                  onChange={(e) => setSenha(e.target.value)}
                  autoComplete="new-password"
                  aria-describedby="forca-senha"
                />
                <div id="forca-senha" className="mt-2">
                  <div className="flex gap-1">
                    {[0, 1, 2].map((n) => (
                      <span
                        key={n}
                        className={`h-1 flex-1 rounded-full ${senha && nivelSenha > n ? "bg-marca-primaria dark:bg-marca-acento" : "bg-gray-200 dark:bg-neutral-800"}`}
                      />
                    ))}
                  </div>
                  <p className="mt-1 text-xs text-gray-500 dark:text-neutral-400">
                    {senha ? `${ROTULOS_FORCA[nivelSenha]}. ` : ""}Pelo menos 8 caracteres, com letras e números.
                  </p>
                </div>
              </label>

              <label className="flex items-start gap-2 text-sm text-gray-700 dark:text-neutral-300">
                <input type="checkbox" checked={aceite} onChange={(e) => setAceite(e.target.checked)} className="mt-0.5" />
                <span>
                  Li e aceito os{" "}
                  <Link href="/termos" target="_blank" className="underline">
                    Termos de Uso
                  </Link>{" "}
                  e a{" "}
                  <Link href="/privacidade" target="_blank" className="underline">
                    Política de Privacidade
                  </Link>
                  .
                </span>
              </label>

              {chaveSiteCaptcha && <CaptchaTurnstile chaveSite={chaveSiteCaptcha} versao={versaoCaptcha} aoObterToken={setTokenCaptcha} />}
            </section>
          )}

          {etapa === 4 && (
            <section className="space-y-4">
              <div>
                <h1 className="font-display mb-1 text-2xl font-bold">Confirme seu e-mail</h1>
                <p className="text-sm text-gray-600 dark:text-neutral-400">
                  Enviamos um código de 6 dígitos para <strong>{email.trim().toLowerCase()}</strong>.
                </p>
              </div>
              <label className="block">
                <span className={classeLabel}>Código</span>
                <input
                  className={`${classeInput} text-center text-lg tracking-[0.5em]`}
                  inputMode="numeric"
                  autoComplete="one-time-code"
                  maxLength={6}
                  value={codigo}
                  onChange={(e) => setCodigo(e.target.value.replace(/\D/g, ""))}
                  autoFocus
                />
              </label>
              <div className="flex justify-between text-sm">
                <button onClick={() => setEtapa(3)} className="text-gray-600 underline dark:text-neutral-400">
                  Mudar dados
                </button>
                <button
                  onClick={enviarCodigo}
                  disabled={segundosParaReenviar > 0 || processando}
                  className="text-gray-600 underline disabled:no-underline disabled:opacity-60 dark:text-neutral-400"
                >
                  {segundosParaReenviar > 0 ? `Reenviar em ${segundosParaReenviar}s` : "Reenviar código"}
                </button>
              </div>
              {chaveSiteCaptcha && <CaptchaTurnstile chaveSite={chaveSiteCaptcha} versao={versaoCaptcha} aoObterToken={setTokenCaptcha} />}
            </section>
          )}

          {etapa === 5 && (
            <section className="py-6 text-center">
              <h1 className="font-display mb-2 text-2xl font-bold">Tudo pronto!</h1>
              <p className="mb-1 text-gray-700 dark:text-neutral-300">
                A conta de <strong>{nomeNegocio.trim()}</strong> foi criada e seu teste grátis de 30 dias já começou.
              </p>
              <p className="mb-6 text-sm text-gray-600 dark:text-neutral-400">
                Seu link de agendamento: <strong>{linkPublico}</strong>
              </p>
              <Link href="/painel" className="inline-block rounded-lg bg-marca-primaria px-6 py-3 text-sm font-semibold text-white hover:bg-marca-primaria-hover dark:bg-marca-acento dark:text-marca-primaria">
                Ir para o painel
              </Link>
            </section>
          )}
        </div>
      </div>

      {etapa < 5 && (
        <div className="border-t border-gray-200 px-4 py-3 dark:border-neutral-800">
          <div className="mx-auto w-full max-w-lg">
            <div className="mb-2 flex items-center justify-between gap-3 text-sm">
              <span className="text-gray-600 dark:text-neutral-400">
                {plano ? `${plano.nome} · ${periodicidade.toLowerCase()}` : "Escolha um plano"}
                <span className="block text-xs font-medium text-marca-acento">30 dias grátis, sem cartão</span>
              </span>
              {precoPorMes !== null && (
                <span className="text-right text-gray-600 dark:text-neutral-400">
                  Depois, <strong className="text-foreground">{formatarReais(precoPorMes)}/mês</strong>
                </span>
              )}
            </div>
            <button
              disabled={!podeContinuar || processando}
              onClick={continuar}
              className="w-full rounded-lg bg-marca-primaria px-4 py-3 text-sm font-semibold text-white transition hover:bg-marca-primaria-hover disabled:opacity-50 dark:bg-marca-acento dark:text-marca-primaria dark:hover:bg-marca-acento/90"
            >
              {textoBotao(etapa, podeContinuar, processando)}
            </button>
          </div>
        </div>
      )}
    </div>
  );
}

function textoBotao(etapa: Etapa, podeContinuar: boolean, processando: boolean): string {
  if (processando) return etapa === 4 ? "Criando sua conta..." : "Enviando...";
  if (etapa === 1) return podeContinuar ? "Continuar" : "Selecione um plano";
  if (etapa === 2) return podeContinuar ? "Continuar" : "Preencha os dados do negócio";
  if (etapa === 3) return podeContinuar ? "Enviar código" : "Preencha seus dados";
  return podeContinuar ? "Confirmar e criar conta" : "Digite o código";
}
