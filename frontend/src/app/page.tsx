import Link from "next/link";

/**
 * Placeholder da página pública do negócio — a página de verdade (seção 6.1) entra na
 * Sprint 3, junto com o assistente de agendamento. Por ora, só um link para o painel.
 */
export default function Home() {
  return (
    <main className="flex min-h-screen flex-col items-center justify-center gap-4 px-6 text-center">
      <h1 className="text-2xl font-semibold text-gray-900 dark:text-neutral-50">Agendei</h1>
      <p className="max-w-sm text-sm text-gray-500 dark:text-neutral-400">
        A página pública de cada negócio chega na Sprint 3. Por enquanto, acesse o painel.
      </p>
      <Link
        href="/painel/login"
        className="rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium text-white transition hover:bg-blue-700"
      >
        Entrar no painel
      </Link>
    </main>
  );
}
