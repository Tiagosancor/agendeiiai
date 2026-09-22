#!/usr/bin/env bash
# Backup do banco (seção 8.5.7): pg_dump em formato custom (já comprimido), cifrado com
# AES-256, salvo localmente e — se configurado — enviado para armazenamento S3-compatível
# (Cloudflare R2 ou Amazon S3), com retenção.
#
# Roda o pg_dump DENTRO do container do Postgres (via `docker compose exec`), em vez de
# exigir pg_dump/psql instalados no host — o único pré-requisito é o serviço "postgres"
# do docker-compose estar de pé. Ver docs/decisoes.md.
#
# Uso: scripts/backup.sh [pasta-de-destino]
set -euo pipefail

DIRETORIO_SCRIPT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RAIZ_PROJETO="$(dirname "$DIRETORIO_SCRIPT")"

# Carrega o .env da raiz (se existir) sem sobrescrever variáveis já definidas no ambiente.
if [ -f "$RAIZ_PROJETO/.env" ]; then
  set -a
  # shellcheck disable=SC1091
  source "$RAIZ_PROJETO/.env"
  set +a
fi

SERVICO_POSTGRES="${BACKUP_SERVICO_POSTGRES:-postgres}"
POSTGRES_USER="${POSTGRES_USER:-plataforma}"
POSTGRES_DB="${POSTGRES_DB:-plataforma}"
PASTA_DESTINO="${1:-$RAIZ_PROJETO/backups}"
RETENCAO_DIAS="${BACKUP_RETENCAO_DIAS:-30}"
CHAVE_CRIPTOGRAFIA="${BACKUP_CHAVE_CRIPTOGRAFIA:-}"

cd "$RAIZ_PROJETO"

if [ -z "$CHAVE_CRIPTOGRAFIA" ]; then
  echo "Erro: defina BACKUP_CHAVE_CRIPTOGRAFIA (senha de criptografia do backup) no .env." >&2
  exit 1
fi

if ! docker compose exec -T "$SERVICO_POSTGRES" pg_isready -U "$POSTGRES_USER" -d "$POSTGRES_DB" >/dev/null 2>&1; then
  echo "Erro: o serviço '$SERVICO_POSTGRES' do docker-compose não está respondendo. Rode 'docker compose up -d' primeiro." >&2
  exit 1
fi

mkdir -p "$PASTA_DESTINO"

CARIMBO="$(date -u +%Y%m%d-%H%M%S)"
NOME_BASE="plataforma-${CARIMBO}"
ARQUIVO_CIFRADO="$PASTA_DESTINO/${NOME_BASE}.dump.enc"

echo "Gerando dump de '$POSTGRES_DB' (formato custom, já comprimido) e cifrando..."
docker compose exec -T "$SERVICO_POSTGRES" pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Fc \
  | openssl enc -aes-256-cbc -pbkdf2 -salt -pass "pass:$CHAVE_CRIPTOGRAFIA" -out "$ARQUIVO_CIFRADO"

TAMANHO="$(du -h "$ARQUIVO_CIFRADO" | cut -f1)"
echo "Backup cifrado salvo em: $ARQUIVO_CIFRADO ($TAMANHO)"

# Envio opcional para armazenamento S3-compatível (R2 ou S3 — seção 8.5.7). Sem
# BACKUP_S3_BUCKET configurado ou sem o aws-cli, o backup fica só local — não é erro.
if [ -n "${BACKUP_S3_BUCKET:-}" ] && command -v aws >/dev/null 2>&1; then
  echo "Enviando para s3://${BACKUP_S3_BUCKET}/${NOME_BASE}.dump.enc..."
  ARGUMENTOS_ENDPOINT=()
  [ -n "${BACKUP_S3_ENDPOINT:-}" ] && ARGUMENTOS_ENDPOINT=(--endpoint-url "$BACKUP_S3_ENDPOINT")
  aws s3 cp "$ARQUIVO_CIFRADO" "s3://${BACKUP_S3_BUCKET}/${NOME_BASE}.dump.enc" "${ARGUMENTOS_ENDPOINT[@]}"
else
  echo "Envio para nuvem não configurado (BACKUP_S3_BUCKET ausente ou aws-cli não instalado) — backup ficou só local."
fi

echo "Aplicando retenção de $RETENCAO_DIAS dias em $PASTA_DESTINO..."
find "$PASTA_DESTINO" -name 'plataforma-*.dump.enc' -mtime "+$RETENCAO_DIAS" -print -delete

echo "Backup concluído."
