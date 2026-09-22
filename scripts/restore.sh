#!/usr/bin/env bash
# Restaura um backup gerado por scripts/backup.sh (seção 8.5.7). Roda pg_restore DENTRO
# do container do Postgres, do mesmo jeito que o backup.sh — ver docs/decisoes.md.
#
# Uso: scripts/restore.sh <arquivo.dump.enc> [nome-do-banco-destino] [--force]
#
# Sem "nome-do-banco-destino", restaura por cima do banco principal (POSTGRES_DB) — pede
# confirmação, porque apaga o schema atual antes de restaurar. Com um nome diferente,
# cria (se preciso) e restaura nele, sem tocar no banco principal — é o que o teste
# automatizado de restauração usa.
set -euo pipefail

DIRETORIO_SCRIPT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RAIZ_PROJETO="$(dirname "$DIRETORIO_SCRIPT")"

if [ -f "$RAIZ_PROJETO/.env" ]; then
  set -a
  # shellcheck disable=SC1091
  source "$RAIZ_PROJETO/.env"
  set +a
fi

ARQUIVO_CIFRADO="${1:-}"
BANCO_DESTINO="${2:-${POSTGRES_DB:-plataforma}}"
FORCAR=false
for arg in "$@"; do
  [ "$arg" = "--force" ] && FORCAR=true
done

SERVICO_POSTGRES="${BACKUP_SERVICO_POSTGRES:-postgres}"
POSTGRES_USER="${POSTGRES_USER:-plataforma}"
CHAVE_CRIPTOGRAFIA="${BACKUP_CHAVE_CRIPTOGRAFIA:-}"
RESTAURANDO_BANCO_PRINCIPAL=false
[ "$BANCO_DESTINO" = "${POSTGRES_DB:-plataforma}" ] && RESTAURANDO_BANCO_PRINCIPAL=true

cd "$RAIZ_PROJETO"

if [ -z "$ARQUIVO_CIFRADO" ] || [ ! -f "$ARQUIVO_CIFRADO" ]; then
  echo "Uso: $0 <arquivo.dump.enc> [nome-do-banco-destino] [--force]" >&2
  exit 1
fi

if [ -z "$CHAVE_CRIPTOGRAFIA" ]; then
  echo "Erro: defina BACKUP_CHAVE_CRIPTOGRAFIA no .env." >&2
  exit 1
fi

if ! docker compose exec -T "$SERVICO_POSTGRES" pg_isready -U "$POSTGRES_USER" >/dev/null 2>&1; then
  echo "Erro: o serviço '$SERVICO_POSTGRES' do docker-compose não está respondendo. Rode 'docker compose up -d' primeiro." >&2
  exit 1
fi

if [ "$RESTAURANDO_BANCO_PRINCIPAL" = true ] && [ "$FORCAR" != true ]; then
  read -r -p "Isso vai APAGAR e recriar o banco '$BANCO_DESTINO'. Confirma? [s/N] " resposta
  [ "$resposta" = "s" ] || [ "$resposta" = "S" ] || { echo "Cancelado."; exit 1; }
fi

echo "Preparando o banco de destino '$BANCO_DESTINO'..."
if [ "$RESTAURANDO_BANCO_PRINCIPAL" = true ]; then
  docker compose exec -T "$SERVICO_POSTGRES" psql -U "$POSTGRES_USER" -d "$BANCO_DESTINO" -v ON_ERROR_STOP=1 \
    -c "DROP SCHEMA public CASCADE; CREATE SCHEMA public;"
else
  # Banco de teste/paralelo: cria se ainda não existir, sem mexer no principal.
  docker compose exec -T "$SERVICO_POSTGRES" psql -U "$POSTGRES_USER" -d postgres -v ON_ERROR_STOP=1 \
    -c "SELECT 1 FROM pg_database WHERE datname = '$BANCO_DESTINO'" | grep -q 1 \
    || docker compose exec -T "$SERVICO_POSTGRES" psql -U "$POSTGRES_USER" -d postgres -v ON_ERROR_STOP=1 \
      -c "CREATE DATABASE \"$BANCO_DESTINO\""
fi

echo "Decifrando e restaurando em '$BANCO_DESTINO'..."
openssl enc -d -aes-256-cbc -pbkdf2 -salt -pass "pass:$CHAVE_CRIPTOGRAFIA" -in "$ARQUIVO_CIFRADO" \
  | docker compose exec -T "$SERVICO_POSTGRES" pg_restore -U "$POSTGRES_USER" -d "$BANCO_DESTINO" --no-owner

echo "Restauração concluída em '$BANCO_DESTINO'."
