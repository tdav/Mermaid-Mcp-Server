#!/usr/bin/env bash
#
# deploy/deploy.sh — установка и автообновление MermaidMcpServer на Linux-сервере.
#
# Запускается ОДИН раз — дальше сервер обновляется сам:
#   1. клонирует репозиторий в $SRC_DIR (или подтягивает свежий коммит);
#   2. собирает `dotnet publish` в .staging и подменяет каталог только после успеха;
#   3. ставит systemd-юнит mermaid-mcp.service и таймер mermaid-mcp-update.timer;
#   4. ждёт ответа GET / и при неудаче откатывается на прежнюю версию.
# Таймер раз в $UPDATE_INTERVAL запускает этот же скрипт; если новых коммитов нет,
# он выходит за секунду, ничего не пересобирая и не перезапуская.
#
# Первая установка (одной строкой, с чистого сервера):
#   curl -fsSL https://raw.githubusercontent.com/tdav/Mermaid-Mcp-Server/main/deploy/deploy.sh \
#     | sudo PUBLIC_BASE_URL=https://mermaid.example.com bash
#
# Повторные запуски вручную:
#   sudo bash /opt/mermaid-mcp/src/deploy/deploy.sh            # обновить, если есть новый коммит
#   sudo bash /opt/mermaid-mcp/src/deploy/deploy.sh --force    # пересобрать даже без новых коммитов
#   sudo bash /opt/mermaid-mcp/src/deploy/deploy.sh --no-timer # не ставить автообновление
#   sudo bash /opt/mermaid-mcp/src/deploy/deploy.sh --status   # состояние
#
# Требуется на сервере: git, curl, systemd, .NET SDK 10 (сборка идёт на сервере).
#
set -euo pipefail

# ---------------------------------------------------------------------------
# Конфигурация (всё переопределяется переменными окружения)
# ---------------------------------------------------------------------------
REPO_URL="${REPO_URL:-https://github.com/tdav/Mermaid-Mcp-Server.git}"
BRANCH="${BRANCH:-main}"
SRC_DIR="${SRC_DIR:-/opt/mermaid-mcp/src}"
APP_DIR="${APP_DIR:-/var/www/mermaid-mcp}"
PORT="${PORT:-40100}"                       # как в README (Deploy SystemD)
PUBLIC_BASE_URL="${PUBLIC_BASE_URL:-http://localhost:$PORT}"
UPDATE_INTERVAL="${UPDATE_INTERVAL:-5min}"
HEALTH_TIMEOUT="${HEALTH_TIMEOUT:-60}"      # секунд на старт сервиса

RUN_USER="mermaid"
SERVICE="mermaid-mcp"
ENV_FILE="/etc/mermaid-mcp/mermaid.env"
SYSTEMD_DIR="/etc/systemd/system"
STAGE_DIR="$APP_DIR.staging"
PREV_DIR="$APP_DIR.prev"
PROJECT="src/MermaidMcpServer/MermaidMcpServer.csproj"

FORCE=0
INSTALL_TIMER=1

log()  { printf '\n\033[1;36m=== %s\033[0m\n' "$*"; }
info() { printf '    %s\n' "$*"; }
die()  { printf '\033[1;31m  ✗ %s\033[0m\n' "$*" >&2; exit 1; }

# Скрипт сам себя обновляет (git reset подменяет его файл), а bash читает скрипт
# построчно в процессе выполнения. Поэтому всё тело — в функциях, а вызов main и
# `exit` стоят в одной строке в самом конце: к моменту подмены файла bash уже
# прочитал и разобрал всё нужное.

wait_healthy() {
    local waited=0
    while [ "$waited" -lt "$HEALTH_TIMEOUT" ]; do
        systemctl is-active --quiet "$SERVICE.service" || return 1
        # У приложения нет /health: корень отдаёт wwwroot/index.html (веб-UI).
        curl -fsS -o /dev/null --max-time 3 "http://127.0.0.1:$PORT/" 2>/dev/null && return 0
        sleep 2
        waited=$(( waited + 2 ))
    done
    return 1
}

status() {
    log "состояние"
    printf '  сервис:        %s\n' "$(systemctl is-active "$SERVICE.service" 2>/dev/null || echo '-')"
    printf '  автообновление: %s\n' "$(systemctl is-active "$SERVICE-update.timer" 2>/dev/null || echo '-')"
    printf '  версия:        %s\n' "$(cat "$APP_DIR/.commit" 2>/dev/null || echo '-')"
    curl -fsS -o /dev/null --max-time 3 "http://127.0.0.1:$PORT/" 2>/dev/null \
        && printf '  http :%s       ok\n' "$PORT" || printf '  http :%s       FAIL\n' "$PORT"
}

preflight() {
    log "0/5 preflight"
    [ "$(id -u)" -eq 0 ] || die "нужен root: sudo bash $0"

    local bin
    for bin in git curl systemctl flock dotnet; do
        command -v "$bin" >/dev/null || die "не найден $bin"
    done
    dotnet --list-sdks | grep -q '^10\.' \
        || die ".NET SDK 10 не найден (dotnet --list-sdks). Установка: https://learn.microsoft.com/dotnet/core/install/linux"

    # Два деплоя одновременно (таймер + ручной запуск) снесли бы друг другу staging.
    install -d -m 0755 "$(dirname "$SRC_DIR")"
    exec 9>"$(dirname "$SRC_DIR")/.deploy.lock"
    flock -n 9 || die "другой деплой уже идёт"

    id -u "$RUN_USER" >/dev/null 2>&1 \
        || { useradd --system --no-create-home --shell /usr/sbin/nologin "$RUN_USER"; info "создан пользователь $RUN_USER"; }

    # PNG рисует SkiaSharp. Mermaider меряет подписи метриками шрифта Inter; без него
    # подставляется более широкий DejaVu, и текст вылезает за рамки блоков. На минимальном
    # сервере шрифтов нет вовсе — тогда PNG выходит без текста. libfontconfig1 нужна
    # libSkiaSharp.so на x64. Не критично для запуска сервиса, поэтому только предупреждение.
    if command -v apt-get >/dev/null && ! dpkg -s fonts-inter libfontconfig1 >/dev/null 2>&1; then
        DEBIAN_FRONTEND=noninteractive apt-get install -y -qq --no-install-recommends fonts-inter libfontconfig1 >/dev/null \
            && info "установлены fonts-inter, libfontconfig1 (для PNG)" \
            || info "! не удалось поставить fonts-inter/libfontconfig1: PNG может выйти без текста или со съехавшими подписями"
    fi

    # Окружение сервиса. Создаётся один раз и дальше правится руками — деплой его не трогает.
    if [ ! -f "$ENV_FILE" ]; then
        install -d -m 0755 "$(dirname "$ENV_FILE")"
        cat > "$ENV_FILE" <<EOF
# Окружение mermaid-mcp.service. Деплой этот файл не перезаписывает.
ASPNETCORE_ENVIRONMENT=Production
# Базовый URL для ссылок на предпросмотр, которые возвращает render_mermaid.
Server__PublicBaseUrl=$PUBLIC_BASE_URL
DOTNET_PRINT_TELEMETRY_MESSAGE=false
DOTNET_CLI_TELEMETRY_OPTOUT=1
EOF
        info "создан $ENV_FILE (PublicBaseUrl=$PUBLIC_BASE_URL)"
    fi
}

sync_source() {
    log "1/5 исходники ($REPO_URL, $BRANCH)"
    if [ -d "$SRC_DIR/.git" ]; then
        git -C "$SRC_DIR" fetch --quiet --depth 1 origin "$BRANCH"
        git -C "$SRC_DIR" reset --quiet --hard FETCH_HEAD
    else
        rm -rf "$SRC_DIR"
        git clone --quiet --depth 1 --branch "$BRANCH" "$REPO_URL" "$SRC_DIR"
    fi
    NEW_COMMIT="$(git -C "$SRC_DIR" rev-parse HEAD)"
    OLD_COMMIT="$(cat "$APP_DIR/.commit" 2>/dev/null || true)"
    info "развёрнуто: ${OLD_COMMIT:0:8}  →  доступно: ${NEW_COMMIT:0:8}"
}

publish() {
    log "2/5 dotnet publish"
    # Ноды MSBuild по умолчанию живут после сборки и держат сотни МБ — на сервере не нужны.
    export MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
    rm -rf "$STAGE_DIR"
    dotnet publish "$SRC_DIR/$PROJECT" -c Release -o "$STAGE_DIR" \
        /p:UseAppHost=false --nologo -v q >/dev/null \
        || die "dotnet publish упал; работающая версия не тронута"
    dotnet build-server shutdown >/dev/null 2>&1 || true
}

write_units() {
    log "3/5 systemd-юниты"
    # libSkiaSharp.so для linux-arm64 (3.119.x) собрана с неразрешёнными символами
    # FT_Get_BDF_Property (freetype) и uuid_* (libuuid) и не линкуется с этими библиотеками:
    # первый же вызов PNG убивает процесс «symbol lookup error». Подгружаем их заранее.
    local preload=""
    if [ "$(uname -m)" = "aarch64" ]; then
        local libdir ft uuid
        libdir="$(dirname "$(ldconfig -p | awk '/libfreetype\.so\.6 .*aarch64/{print $NF; exit}')")"
        ft="$libdir/libfreetype.so.6"; uuid="$libdir/libuuid.so.1"
        if [ -e "$ft" ] && [ -e "$uuid" ]; then preload="Environment=LD_PRELOAD=$ft $uuid"
        else info "! нет libfreetype6/libuuid1: PNG на arm64 уронит процесс"; fi
    fi
    cat > "$SYSTEMD_DIR/$SERVICE.service" <<EOF
[Unit]
# Сгенерировано deploy/deploy.sh — правки затрутся следующим деплоем.
Description=MermaidMcpServer (port $PORT)
After=network-online.target
Wants=network-online.target

[Service]
# Type=exec: sd_notify приложение не шлёт, готовность проверяет деплой через GET /.
Type=exec
User=$RUN_USER
Group=$RUN_USER
WorkingDirectory=$APP_DIR
ExecStart=/usr/bin/dotnet $APP_DIR/MermaidMcpServer.dll
EnvironmentFile=$ENV_FILE
# Снаружи слушает только реверс-прокси; сервис — на loopback.
Environment=ASPNETCORE_URLS=http://127.0.0.1:$PORT
# Системному пользователю нужен записываемый HOME для кэшей dotnet / SkiaSharp.
Environment=HOME=$APP_DIR
$preload
Restart=always
RestartSec=5
# ASP.NET завершается штатно по SIGINT; SIGTERM для него — жёсткое убийство.
KillSignal=SIGINT
TimeoutStopSec=30
SyslogIdentifier=$SERVICE

NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=full
ProtectHome=true
ProtectKernelTunables=true
ProtectControlGroups=true
RestrictSUIDSGID=true

[Install]
WantedBy=multi-user.target
EOF

    if [ "$INSTALL_TIMER" -eq 1 ]; then
        # Таймер запускает этот же скрипт из клона репозитория, поэтому
        # новая версия deploy.sh подхватывается вместе с новым кодом.
        cat > "$SYSTEMD_DIR/$SERVICE-update.service" <<EOF
[Unit]
# Сгенерировано deploy/deploy.sh — правки затрутся следующим деплоем.
Description=Автообновление MermaidMcpServer
After=network-online.target
Wants=network-online.target

[Service]
Type=oneshot
Environment=REPO_URL=$REPO_URL BRANCH=$BRANCH SRC_DIR=$SRC_DIR APP_DIR=$APP_DIR PORT=$PORT UPDATE_INTERVAL=$UPDATE_INTERVAL
ExecStart=/bin/bash $SRC_DIR/deploy/deploy.sh
SyslogIdentifier=$SERVICE-update
EOF
        cat > "$SYSTEMD_DIR/$SERVICE-update.timer" <<EOF
[Unit]
Description=Проверка обновлений MermaidMcpServer раз в $UPDATE_INTERVAL

[Timer]
OnBootSec=2min
OnUnitActiveSec=$UPDATE_INTERVAL
# Разброс, чтобы несколько серверов не стучались в GitHub в одну секунду.
RandomizedDelaySec=30

[Install]
WantedBy=timers.target
EOF
    fi
    systemctl daemon-reload
}

swap_and_restart() {
    log "4/5 подмена и запуск"
    local had_prev=0

    systemctl stop "$SERVICE.service" 2>/dev/null || true

    rm -rf "$PREV_DIR"
    if [ -d "$APP_DIR" ]; then mv "$APP_DIR" "$PREV_DIR"; had_prev=1; fi
    mv "$STAGE_DIR" "$APP_DIR"
    echo "$NEW_COMMIT" > "$APP_DIR/.commit"
    chown -R "$RUN_USER":"$RUN_USER" "$APP_DIR"

    systemctl enable "$SERVICE.service" >/dev/null 2>&1 || true
    systemctl restart "$SERVICE.service"

    if wait_healthy; then
        info "✓ GET / отвечает на :$PORT"
        return 0
    fi

    journalctl -u "$SERVICE.service" -n 40 --no-pager >&2 || true
    systemctl stop "$SERVICE.service" 2>/dev/null || true
    if [ "$had_prev" -eq 1 ]; then
        rm -rf "$APP_DIR"
        mv "$PREV_DIR" "$APP_DIR"
        systemctl start "$SERVICE.service" || true
        die "новая версия ${NEW_COMMIT:0:8} не поднялась за ${HEALTH_TIMEOUT}s; откат на ${OLD_COMMIT:0:8}"
    fi
    die "сервис не поднялся за ${HEALTH_TIMEOUT}s (предыдущей версии для отката нет)"
}

enable_timer() {
    [ "$INSTALL_TIMER" -eq 1 ] || return 0
    log "5/5 автообновление"
    systemctl enable --now "$SERVICE-update.timer" >/dev/null 2>&1
    info "проверка обновлений каждые $UPDATE_INTERVAL (journalctl -u $SERVICE-update)"
}

main() {
    while [ $# -gt 0 ]; do
        case "$1" in
            --force)    FORCE=1 ;;
            --no-timer) INSTALL_TIMER=0 ;;
            --status)   status; return 0 ;;
            -h|--help)  sed -n '2,26p' "$0" | sed 's/^# \{0,1\}//'; return 0 ;;
            *)          die "неизвестный аргумент: $1 (см. --help)" ;;
        esac
        shift
    done

    preflight
    sync_source

    if [ "$FORCE" -eq 0 ] && [ "$NEW_COMMIT" = "$OLD_COMMIT" ] \
        && systemctl is-active --quiet "$SERVICE.service"; then
        info "обновлений нет — выход"
        return 0
    fi

    publish
    write_units
    swap_and_restart
    enable_timer
    status
}

main "$@"; exit $?
