"use client";

import { useState } from "react";
import Link from "next/link";
import { useAutenticacao } from "@/lib/auth-context";

export function AvisoMinhaConta() {
  const { minhaConta, usuarioId } = useAutenticacao();
  const [dispensadoPara, setDispensadoPara] = useState<string | null>(null);
  if (!minhaConta || (dispensadoPara !== null && dispensadoPara === usuarioId) || (minhaConta.emailVerificado && !minhaConta.senhaDefinidaPorTerceiro)) return null;
  return <aside aria-label="Cadastro da sua conta" className="mb-4 space-y-2 rounded-lg border border-marca-acento/40 bg-marca-acento/10 p-3 text-sm">
    {!minhaConta.emailVerificado && <p>Complete seu cadastro: adicione e confirme um e-mail para poder recuperar a senha.</p>}
    {minhaConta.senhaDefinidaPorTerceiro && <p>Sua senha foi definida pelo administrador. Troque-a em Minha conta.</p>}
    <div className="flex flex-wrap gap-4"><Link className="font-semibold underline" href="/painel/minha-conta">Minha conta</Link><button className="underline" type="button" onClick={() => setDispensadoPara(usuarioId)}>Dispensar nesta sessão</button></div>
  </aside>;
}
