export default function NotFound() {
  return (
    <main className="flex min-h-screen flex-col items-center justify-center gap-2 px-6 text-center">
      <h1 className="text-2xl font-semibold">Página não encontrada</h1>
      <p className="text-neutral-500">
        Não existe um negócio ativo neste endereço, ou a página que você procura não existe.
      </p>
    </main>
  );
}
