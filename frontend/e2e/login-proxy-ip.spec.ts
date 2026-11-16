import { test, expect } from "@playwright/test";
import { NextRequest } from "next/server";
import { POST } from "../src/app/painel/auth/[...caminho]/route";

const casos: { headers: Record<string, string>; esperado: string }[] = [
  { headers: { "x-forwarded-for": "203.0.113.7, 192.0.2.1" }, esperado: "203.0.113.7" },
  { headers: { "x-real-ip": "203.0.113.8" }, esperado: "203.0.113.8" },
];
for (const caso of casos) test(`proxy preserva cookie, resposta e IP ${caso.esperado}`, async () => {
  const original = globalThis.fetch;
  let cabecalhos: Headers | undefined;
  let corpo: unknown;
  globalThis.fetch = async (_url, opcoes) => {
    cabecalhos = new Headers(opcoes?.headers);
    corpo = opcoes?.body;
    return new Response('{"accessToken":"exemplo"}', { status: 200, headers: { "Content-Type": "application/json", "Set-Cookie": "refresh_token=exemplo; HttpOnly; Path=/painel/auth; SameSite=Strict" } });
  };
  try {
    const resposta = await POST(new NextRequest("http://app.exemplo.test/painel/auth/login", {
      method: "POST", headers: { ...caso.headers, cookie: "refresh_token=anterior", "Content-Type": "application/json" },
      body: '{"identificador":"usuario.exemplo","senha":"Senha123"}',
    }), { params: Promise.resolve({ caminho: ["login"] }) });
    expect(cabecalhos?.get("x-forwarded-for")).toBe(caso.esperado);
    expect(cabecalhos?.get("cookie")).toBe("refresh_token=anterior");
    expect(corpo).toBe('{"identificador":"usuario.exemplo","senha":"Senha123"}');
    expect(resposta.status).toBe(200);
    expect(resposta.headers.get("cache-control")).toBe("no-store");
    expect(resposta.headers.get("set-cookie")).toContain("HttpOnly");
    expect(await resposta.json()).toEqual({ accessToken: "exemplo" });
  } finally { globalThis.fetch = original; }
});
