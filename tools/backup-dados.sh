#!/usr/bin/env bash
#
# Backup do único estado que não se recria: o volume App_Data da aplicação, que guarda o banco
# SQLite (contas, tentativas, respostas) e as chaves de Data Protection.
#
# Perder as chaves não perde dado, mas invalida todo cookie de sessão e todo token antiforgery em
# circulação — daí elas irem no mesmo pacote do banco: restaurar um sem o outro deixa o ambiente
# num estado pior que o de antes.
#
# ⚠️ POR QUE ELE PARA O CONTAINER. O SQLite roda em modo WAL: parte das escritas confirmadas vive
# no arquivo -wal até o próximo checkpoint. Copiar os arquivos com a aplicação escrevendo produz
# uma cópia que às vezes abre, às vezes abre sem as últimas transações e às vezes não abre — e o
# defeito só aparece no dia da restauração. Parar por alguns segundos é o preço de uma cópia
# consistente sem depender de ter o binário do sqlite3 em lugar nenhum.
#
# Uso:
#   ./tools/backup-dados.sh                    # grava em ./backups
#   ./tools/backup-dados.sh /mnt/backups 30    # destino e dias de retenção
#
# Em cron (diário às 4h):
#   0 4 * * * cd /caminho/do/PrepHub && ./tools/backup-dados.sh /mnt/backups 30 >> /var/log/prephub-backup.log 2>&1
#
# ⚠️ Backup que nunca foi restaurado não é backup. Ver a seção "Restaurar" em docs/deploy.md.

set -euo pipefail

DESTINO="${1:-./backups}"
RETENCAO_DIAS="${2:-14}"
VOLUME="prephub_dados"
SERVICO="web"

carimbo="$(date +%Y-%m-%d_%H%M%S)"
arquivo="${DESTINO}/prephub-dados-${carimbo}.tar.gz"

mkdir -p "$DESTINO"

if ! docker volume inspect "$VOLUME" >/dev/null 2>&1; then
    echo "ERRO: volume '$VOLUME' não existe. A stack já subiu alguma vez nesta máquina?" >&2
    exit 1
fi

# Só religa o que estava ligado: rodar o script com a stack parada não deve subir nada.
estava_rodando=0
if [ -n "$(docker compose ps -q "$SERVICO" 2>/dev/null)" ] \
   && [ "$(docker inspect -f '{{.State.Running}}' "$(docker compose ps -q "$SERVICO")" 2>/dev/null)" = "true" ]; then
    estava_rodando=1
fi

religar() {
    if [ "$estava_rodando" = "1" ]; then
        echo "→ religando '$SERVICO'..."
        docker compose start "$SERVICO" >/dev/null
    fi
}
# trap: se o tar falhar ou alguém interromper com Ctrl+C, o site volta ao ar mesmo assim.
trap religar EXIT

if [ "$estava_rodando" = "1" ]; then
    echo "→ parando '$SERVICO' para copiar o volume de forma consistente..."
    docker compose stop "$SERVICO" >/dev/null
fi

echo "→ empacotando '$VOLUME' em ${arquivo}..."
docker run --rm \
    -v "${VOLUME}:/dados:ro" \
    -v "$(cd "$DESTINO" && pwd):/saida" \
    alpine:3 \
    tar czf "/saida/$(basename "$arquivo")" -C /dados .

# Verificação mínima: um tar que lista e contém o banco. Não prova que o SQLite abre — só que o
# pacote não saiu vazio, que é a falha mais comum e a mais fácil de passar despercebida.
if ! tar tzf "$arquivo" | grep -q 'prephub\.db$'; then
    echo "ERRO: o pacote não contém prephub.db — backup descartado." >&2
    rm -f "$arquivo"
    exit 1
fi

tamanho="$(du -h "$arquivo" | cut -f1)"
echo "✓ backup concluído: ${arquivo} (${tamanho})"

if [ "$RETENCAO_DIAS" -gt 0 ]; then
    removidos="$(find "$DESTINO" -name 'prephub-dados-*.tar.gz' -type f -mtime "+${RETENCAO_DIAS}" -print -delete | wc -l)"
    [ "$removidos" -gt 0 ] && echo "→ ${removidos} backup(s) com mais de ${RETENCAO_DIAS} dias removido(s)."
fi

exit 0
