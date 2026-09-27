"use client";

import { useId, useState, type InputHTMLAttributes, type ReactNode } from "react";
import { classeLabel } from "@/components/estilos";

interface PropsCampoSenha extends Omit<InputHTMLAttributes<HTMLInputElement>, "type"> {
  rotulo: string;
  classeRotulo?: string;
  /** Texto de ajuda abaixo do campo (ex.: regra da senha). */
  ajuda?: ReactNode;
}

/**
 * Campo de senha com o botão de olho para mostrar/ocultar o que foi digitado. Renderiza o
 * próprio `<label htmlFor>`: se o botão ficasse dentro de um `<label>` envolvendo o input, o
 * nome dele ("Mostrar senha") entraria no nome acessível do campo ("Senha Mostrar senha").
 * O `className` vai no input; quem chama envolve com um `<div>`, não com `<label>`.
 * O nome do botão é texto `sr-only`, não `aria-label`: o `getByLabel("Senha")` dos e2e
 * casa com `aria-label` por substring e acharia o botão também.
 */
export function CampoSenha({ rotulo, classeRotulo = classeLabel, ajuda, className = "", id, ...props }: PropsCampoSenha) {
  const idGerado = useId();
  const idCampo = id ?? idGerado;
  const idAjuda = `${idCampo}-ajuda`;
  const [visivel, setVisivel] = useState(false);

  return (
    <>
      <label htmlFor={idCampo} className={classeRotulo}>
        {rotulo}
      </label>
      <div className="relative">
        <input
          {...props}
          id={idCampo}
          type={visivel ? "text" : "password"}
          aria-describedby={ajuda ? idAjuda : props["aria-describedby"]}
          className={`${className} pr-10`}
        />
        <button
          type="button"
          onClick={() => setVisivel((v) => !v)}
          aria-controls={idCampo}
          title={visivel ? "Ocultar senha" : "Mostrar senha"}
          className="absolute inset-y-0 right-0 flex w-10 items-center justify-center rounded-r-lg text-gray-500 hover:text-gray-800 focus-visible:outline-2 focus-visible:outline-marca-primaria dark:text-neutral-400 dark:hover:text-neutral-100"
        >
          <span className="sr-only">{visivel ? "Ocultar senha" : "Mostrar senha"}</span>
          {visivel ? (
            <svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
              <path d="M17.94 17.94A10.07 10.07 0 0 1 12 20c-7 0-11-8-11-8a18.45 18.45 0 0 1 5.06-5.94" />
              <path d="M9.9 4.24A9.12 9.12 0 0 1 12 4c7 0 11 8 11 8a18.5 18.5 0 0 1-2.16 3.19" />
              <path d="M14.12 14.12a3 3 0 1 1-4.24-4.24" />
              <line x1="1" y1="1" x2="23" y2="23" />
            </svg>
          ) : (
            <svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
              <path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z" />
              <circle cx="12" cy="12" r="3" />
            </svg>
          )}
        </button>
      </div>
      {ajuda && (
        <div id={idAjuda} className="mt-1 text-xs text-gray-500 dark:text-neutral-400">
          {ajuda}
        </div>
      )}
    </>
  );
}
