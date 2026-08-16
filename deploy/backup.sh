#!/usr/bin/env bash
#
# Резервное копирование MultiMessenger: дамп PostgreSQL, содержимое MinIO,
# файлы сессий мессенджеров.
#
# Запускается по таймеру systemd (см. Развёртывание.md). Настройки берутся
# из backup.env рядом со скриптом.
#
# Принцип из ТЗ 2.12: копия обязана лежать за пределами сервера. Локальный
# каталог — только промежуточная площадка; если отправка наружу не настроена,
# скрипт завершается с ошибкой, чтобы это не осталось незамеченным.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# shellcheck source=/dev/null
[ -f "$SCRIPT_DIR/backup.env" ] && source "$SCRIPT_DIR/backup.env"
# shellcheck source=/dev/null
[ -f "$SCRIPT_DIR/.env" ] && source "$SCRIPT_DIR/.env"

BACKUP_ROOT="${BACKUP_ROOT:-/var/backups/multimessenger}"
KEEP_DAYS="${BACKUP_KEEP_DAYS:-14}"
COMPOSE_FILE="${COMPOSE_FILE:-$SCRIPT_DIR/docker-compose.prod.yml}"
STAMP="$(date +%Y%m%d-%H%M%S)"
TARGET="$BACKUP_ROOT/$STAMP"

log() { echo "[$(date +%H:%M:%S)] $*"; }
fail() { echo "[ОШИБКА] $*" >&2; exit 1; }

mkdir -p "$TARGET"

# --- PostgreSQL ---------------------------------------------------------
# Формат custom, а не текстовый: сжат и позволяет восстанавливать выборочно.
log "Дамп PostgreSQL"
docker compose --file "$COMPOSE_FILE" exec -T postgres \
    pg_dump --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" --format=custom \
    > "$TARGET/postgres.dump"

[ -s "$TARGET/postgres.dump" ] || fail "дамп PostgreSQL пустой"

# --- MinIO --------------------------------------------------------------
# mc запускается внутри контейнера хранилища: снаружи клиента может не быть.
log "Копирование содержимого MinIO"
docker compose --file "$COMPOSE_FILE" exec -T minio \
    mc alias set backup http://localhost:9000 "$MINIO_ROOT_USER" "$MINIO_ROOT_PASSWORD" > /dev/null

docker compose --file "$COMPOSE_FILE" exec -T minio \
    mc mirror --overwrite --remove backup/multimessenger-media /tmp/minio-backup > /dev/null

docker compose --file "$COMPOSE_FILE" exec -T minio \
    tar --create --gzip --directory /tmp/minio-backup . > "$TARGET/minio-media.tar.gz"

# --- Файлы сессий -------------------------------------------------------
# Равносильны полному доступу к аккаунтам менеджеров: копия обязана лежать
# с правами 600 и уезжать только на доверенное хранилище.
#
# Том читается одноразовым контейнером, а не через exec в app: копия должна
# сниматься и тогда, когда приложение лежит, — именно в такой момент она нужнее всего.
log "Архивация файлов сессий"
docker run --rm --volume "${SESSIONS_VOLUME:-multimessenger_sessions}:/data:ro" alpine:3 \
    tar --create --gzip --directory /data . > "$TARGET/sessions.tar.gz"

chmod 600 "$TARGET"/*

# --- Отправка за пределы сервера ---------------------------------------
if [ -n "${BACKUP_REMOTE:-}" ]; then
    log "Отправка на $BACKUP_REMOTE"
    rsync --archive --compress --partial \
        -e "ssh -o StrictHostKeyChecking=yes -i ${BACKUP_SSH_KEY:-$HOME/.ssh/id_backup}" \
        "$TARGET" "$BACKUP_REMOTE/"
else
    fail "BACKUP_REMOTE не задан — копия осталась на том же сервере, что и данные. \
Это не резервное копирование: при потере сервера пропадёт и она."
fi

# --- Ротация ------------------------------------------------------------
log "Удаление копий старше $KEEP_DAYS дней"
find "$BACKUP_ROOT" -mindepth 1 -maxdepth 1 -type d -mtime "+$KEEP_DAYS" -exec rm -rf {} +

log "Готово: $TARGET ($(du -sh "$TARGET" | cut -f1))"
