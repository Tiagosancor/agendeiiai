#!/usr/bin/env bash
# Teste automatizado de restauração (seção 8.5.7 / Sprint 1, M): sobe dados de teste,
# faz backup, restaura num banco separado e confere tabelas presentes, dados intactos e
# isolamento por negócio (NegocioId preservado). Não toca no banco principal.
#
# Pré-requisito: `docker compose up -d` já rodando. Uso: scripts/testar-backup-restore.sh
set -euo pipefail

DIRETORIO_SCRIPT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RAIZ_PROJETO="$(dirname "$DIRETORIO_SCRIPT")"
cd "$RAIZ_PROJETO"

if [ -f .env ]; then
  set -a
  # shellcheck disable=SC1091
  source .env
  set +a
fi

SERVICO_POSTGRES="${BACKUP_SERVICO_POSTGRES:-postgres}"
POSTGRES_USER="${POSTGRES_USER:-plataforma}"
POSTGRES_DB="${POSTGRES_DB:-plataforma}"
BANCO_TESTE_RESTORE="plataforma_teste_restore_$(date +%s)"
PASTA_TEMP="$(mktemp -d)"
trap 'rm -rf "$PASTA_TEMP"; docker compose exec -T "$SERVICO_POSTGRES" psql -U "$POSTGRES_USER" -d postgres -c "DROP DATABASE IF EXISTS \"$BANCO_TESTE_RESTORE\";" >/dev/null 2>&1 || true' EXIT

FALHAS=0
verificar() {
  local descricao="$1" resultado="$2"
  if [ "$resultado" = "1" ]; then
    echo "  OK   - $descricao"
  else
    echo "  FALHOU - $descricao"
    FALHAS=$((FALHAS + 1))
  fi
}

echo "1) Semeando um negócio de teste diretamente no banco principal..."
NEGOCIO_ID="$(docker compose exec -T "$SERVICO_POSTGRES" psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -t -A -c "
  INSERT INTO negocios (id, slug, nome_exibido, tipo, fuso, ativo, criado_em)
  VALUES (gen_random_uuid(), 'teste-backup-$(date +%s)', 'Negócio do teste de backup', 'Barbearia', 'America/Sao_Paulo', true, now())
  RETURNING id;
" | head -n1)"
echo "   negocio_id=$NEGOCIO_ID"

echo "2) Rodando scripts/backup.sh..."
SAIDA_BACKUP="$("$DIRETORIO_SCRIPT/backup.sh" "$PASTA_TEMP")"
echo "$SAIDA_BACKUP"
ARQUIVO_BACKUP="$(echo "$SAIDA_BACKUP" | grep 'Backup cifrado salvo em:' | sed 's/.*em: //; s/ (.*//')"

echo "3) Rodando scripts/restore.sh num banco separado ($BANCO_TESTE_RESTORE)..."
"$DIRETORIO_SCRIPT/restore.sh" "$ARQUIVO_BACKUP" "$BANCO_TESTE_RESTORE" --force

echo "4) Conferindo o resultado..."

QTD_TABELAS="$(docker compose exec -T "$SERVICO_POSTGRES" psql -U "$POSTGRES_USER" -d "$BANCO_TESTE_RESTORE" -t -A -c \
  "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name IN ('negocios','usuarios','profissionais','servicos','clientes');")"
verificar "tabelas principais presentes (5 esperadas, achou $QTD_TABELAS)" "$([ "$QTD_TABELAS" -eq 5 ] && echo 1 || echo 0)"

EXTENSAO="$(docker compose exec -T "$SERVICO_POSTGRES" psql -U "$POSTGRES_USER" -d "$BANCO_TESTE_RESTORE" -t -A -c \
  "SELECT count(*) FROM pg_extension WHERE extname = 'btree_gist';")"
verificar "extensão btree_gist presente" "$([ "$EXTENSAO" -eq 1 ] && echo 1 || echo 0)"

NEGOCIO_RESTAURADO="$(docker compose exec -T "$SERVICO_POSTGRES" psql -U "$POSTGRES_USER" -d "$BANCO_TESTE_RESTORE" -t -A -c \
  "SELECT count(*) FROM negocios WHERE id = '$NEGOCIO_ID';")"
verificar "negócio de teste restaurado intacto (isolamento por negócio preservado)" "$([ "$NEGOCIO_RESTAURADO" -eq 1 ] && echo 1 || echo 0)"

echo "5) Limpando o negócio de teste do banco principal..."
docker compose exec -T "$SERVICO_POSTGRES" psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c \
  "DELETE FROM negocios WHERE id = '$NEGOCIO_ID';" >/dev/null

if [ "$FALHAS" -eq 0 ]; then
  echo "Teste de restauração de backup: PASSOU."
  exit 0
else
  echo "Teste de restauração de backup: FALHOU ($FALHAS verificação(ões))."
  exit 1
fi
