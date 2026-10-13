import { test, expect } from "@playwright/test";
import { ErroApi } from "../src/lib/api";
import { TentativaIdempotente, fingerprintDaIntencao } from "../src/lib/idempotencia";

test.describe("ciclo de vida da Idempotency-Key no frontend", () => {
  test("gera UUID, canonicaliza o payload e evita duas submissões simultâneas", async () => {
    const tentativa = new TentativaIdempotente();
    const chaves: string[] = [];
    let liberar!: () => void;
    const pendente = new Promise<void>((resolve) => (liberar = resolve));

    const primeira = tentativa.executar({ quantidade: 2, produto: "x" }, async (chave) => {
      chaves.push(chave);
      await pendente;
    });
    const segunda = tentativa.executar({ produto: "x", quantidade: 2 }, async (chave) => chaves.push(chave));

    expect(primeira).toBe(segunda);
    expect(chaves).toHaveLength(1);
    expect(chaves[0]).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i);
    expect(fingerprintDaIntencao({ b: 1, a: [2] })).toBe(fingerprintDaIntencao({ a: [2], b: 1 }));
    liberar();
    await primeira;
  });

  for (const [nome, erro] of [
    ["falha de rede", new TypeError("Failed to fetch")],
    ["erro 5xx", new ErroApi(503, "indisponível")],
    ["operação em andamento", new ErroApi(409, "em andamento", "operacao_em_andamento")],
  ] as const) {
    test(`${nome} preserva a chave no retry do mesmo payload`, async () => {
      const tentativa = new TentativaIdempotente();
      const chaves: string[] = [];
      await expect(
        tentativa.executar({ valor: 10 }, async (chave) => {
          chaves.push(chave);
          throw erro;
        }),
      ).rejects.toBe(erro);
      await tentativa.executar({ valor: 10 }, async (chave) => chaves.push(chave));
      expect(chaves[1]).toBe(chaves[0]);
    });
  }

  test("payload alterado, sucesso e abandono criam novas intenções", async () => {
    const tentativa = new TentativaIdempotente();
    const chaves: string[] = [];
    const falhar = (payload: unknown) =>
      tentativa.executar(payload, async (chave) => {
        chaves.push(chave);
        throw new ErroApi(500, "falha");
      });

    await expect(falhar({ valor: 10 })).rejects.toBeInstanceOf(ErroApi);
    await expect(falhar({ valor: 11 })).rejects.toBeInstanceOf(ErroApi);
    expect(chaves[1]).not.toBe(chaves[0]);

    await tentativa.executar({ valor: 11 }, async (chave) => chaves.push(chave));
    await tentativa.executar({ valor: 11 }, async (chave) => chaves.push(chave));
    expect(chaves[3]).not.toBe(chaves[2]);

    await expect(falhar({ valor: 12 })).rejects.toBeInstanceOf(ErroApi);
    tentativa.descartar();
    await tentativa.executar({ valor: 12 }, async (chave) => chaves.push(chave));
    expect(chaves[5]).not.toBe(chaves[4]);
  });

  test("422 de chave reutilizada descarta a tentativa", async () => {
    const tentativa = new TentativaIdempotente();
    const chaves: string[] = [];
    await expect(
      tentativa.executar({ valor: 10 }, async (chave) => {
        chaves.push(chave);
        throw new ErroApi(422, "reutilizada", "idempotency_key_reutilizada");
      }),
    ).rejects.toBeInstanceOf(ErroApi);
    await tentativa.executar({ valor: 10 }, async (chave) => chaves.push(chave));
    expect(chaves[1]).not.toBe(chaves[0]);
  });
});

test("smoke full-stack: repetir vale com a mesma chave produz um único lançamento", async ({ request }) => {
  const api = "http://localhost:5080";
  const login = await request.post(`${api}/painel/auth/login`, {
    data: { email: "admin@acme.dev", senha: "Admin!123" },
  });
  expect(login.ok(), await login.text()).toBe(true);
  const token = (await login.json()).accessToken as string;
  const autorizacao = { Authorization: `Bearer ${token}` };
  const saldos = (await (await request.get(`${api}/painel/saldos`, { headers: autorizacao })).json()) as {
    profissionalId: string | null;
    ativo: boolean;
  }[];
  const profissional = saldos.find((item) => item.ativo && item.profissionalId);
  expect(profissional).toBeDefined();

  const chave = crypto.randomUUID();
  const corpo = { profissionalId: profissional!.profissionalId, usuarioId: null, valor: 0.01, data: null, motivo: "Smoke idempotência frontend" };
  const headers = { ...autorizacao, "Idempotency-Key": chave };
  const primeira = await request.post(`${api}/painel/saldos/vales`, { headers, data: corpo });
  const segunda = await request.post(`${api}/painel/saldos/vales`, { headers, data: corpo });

  expect(primeira.status(), await primeira.text()).toBe(201);
  expect(segunda.status(), await segunda.text()).toBe(201);
  const id = (await primeira.json()) as string;
  expect(await segunda.json()).toBe(id);
  expect(segunda.headers()["idempotent-replayed"]).toBe("true");

  const remover = await request.delete(`${api}/painel/saldos/${id}`, { headers: autorizacao });
  expect(remover.ok(), await remover.text()).toBe(true);
});
