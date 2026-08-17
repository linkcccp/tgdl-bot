#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-only
# Copyright (C) 2026 linkcccp
# ============================================================
# tgdl-bot Docker 容器入口：
#   目录/卷权限 -> 种子 yt-dlp/ffmpeg -> 生成配置 -> 启 telegram-bot-api
#   -> 等就绪 -> 前台运行 tgdl-bot -> 优雅关闭
# 配置方式：
#   1) 挂载 TGDL_CONFIG_FILE 指向的文件（或镜像默认 /opt/tgdl-bot/config.conf），则直接使用；
#   2) 否则按 TGDL_* 环境变量生成 config.conf（必填：TGDL_BOT_TOKEN、
#      TGDL_TARGET_CHANNELS、TGDL_ALLOWED_USERS；tba 必填 TGDL_API_ID/TGDL_API_HASH）。
# ============================================================
set -euo pipefail

INSTALL_DIR=/opt/tgdl-bot
CONFIG_FILE="${TGDL_CONFIG_FILE:-${TGDL_CONFIG:-/opt/tgdl-bot/config.conf}}"
API_DIR="$INSTALL_DIR/api"
SEED_DIR="$INSTALL_DIR/seed-bin"
BIN_DIR="$INSTALL_DIR/bin"
API_DATA_DIR="$INSTALL_DIR/api-data"
TMP_DIR="${TGDL_DOWNLOAD_TMP:-/var/lib/tgdl-bot/tmp}"
COOKIE_DIR="${TGDL_COOKIE_STORE_DIR:-/opt/tgdl-bot/api-data/cookies}"
RUN_USER=tgdl-bot

log()  { printf '\033[1;32m[INFO ]\033[0m %s\n' "$*"; }
warn() { printf '\033[1;33m[警告 ]\033[0m %s\n' "$*" >&2; }
err()  { printf '\033[1;31m[错误 ]\033[0m %s\n' "$*" >&2; }
die()  { err "$*"; exit 1; }

require() {
    local name="$1" value="${!1:-}"
    [[ -n "$value" ]] || die "缺少环境变量 ${name}，请设置后重启容器。"
}

# 与 ConfigParser.IsBool 对齐：大小写不敏感，接受 true/yes/on/1/false/no/off/0。
# 与旧实现（=~ ^(true|false)$ 大小写敏感）不同，TGDL_*_TRUE 等大写写法不再被静默丢弃，
# 写出的值由 bot 解析（大小写不敏感）后与原意一致。
is_bool() { [[ "${1,,}" =~ ^(true|yes|on|1|false|no|off|0)$ ]]; }

# ---------- 1. 目录与卷权限（挂载卷可能为空且 root 属主） ----------
mkdir -p "$BIN_DIR" "$API_DATA_DIR" "$TMP_DIR" "$COOKIE_DIR"
chown -R "$RUN_USER":"$RUN_USER" "$INSTALL_DIR" /var/lib/tgdl-bot 2>/dev/null || true

# ---------- 2. 种子 yt-dlp/ffmpeg 到 bin 卷（首启） ----------
if [[ -d "$SEED_DIR" ]] && { [[ ! -x "$BIN_DIR/yt-dlp" ]] || [[ ! -x "$BIN_DIR/ffmpeg" ]]; }; then
    cp -a "$SEED_DIR"/. "$BIN_DIR"/ 2>/dev/null || true
    chmod +x "$BIN_DIR/yt-dlp" "$BIN_DIR/ffmpeg" 2>/dev/null || true
    log "已将镜像内置 yt-dlp/ffmpeg 种子到 ${BIN_DIR}"
fi
if [[ ! -x "$BIN_DIR/yt-dlp" ]] || [[ ! -x "$BIN_DIR/ffmpeg" ]]; then
    warn "yt-dlp/ffmpeg 不可用，可稍后私聊 bot 发送 /update 自动安装（需网络）。"
fi

# ---------- 3. 配置：优先使用已存在的文件，否则由环境变量生成 ----------
if [[ -f "$CONFIG_FILE" ]]; then
    log "使用已存在的配置文件：${CONFIG_FILE}"
else
    require TGDL_BOT_TOKEN
    require TGDL_TARGET_CHANNELS
    require TGDL_ALLOWED_USERS
    {
        printf 'BotToken = %s\n' "$TGDL_BOT_TOKEN"
        printf 'LocalApiBaseUrl = %s\n' "${TGDL_LOCAL_API_URL:-http://127.0.0.1:8081}"
        printf 'TargetChannelIds = %s\n' "$TGDL_TARGET_CHANNELS"
        printf 'AllowedUserIds = %s\n' "$TGDL_ALLOWED_USERS"
        printf 'DownloadTempDir = %s\n' "$TMP_DIR"
        printf 'YtDlpPath = %s\n' "$BIN_DIR/yt-dlp"
        printf 'FfmpegPath = %s\n' "$BIN_DIR/ffmpeg"
        [[ -n "${TGDL_LOG_LEVEL:-}" ]]        && printf 'LogLevel = %s\n' "$TGDL_LOG_LEVEL"
        [[ -n "${TGDL_MAX_CONCURRENT:-}" ]]   && printf 'MaxConcurrentDownloads = %s\n' "$TGDL_MAX_CONCURRENT"
        [[ -n "${TGDL_DOWNLOAD_TIMEOUT:-}" ]] && printf 'DownloadTimeoutSeconds = %s\n' "$TGDL_DOWNLOAD_TIMEOUT"
        [[ -n "${TGDL_DOWNLOAD_RETRIES:-}" ]] && printf 'DownloadRetries = %s\n' "$TGDL_DOWNLOAD_RETRIES"
        [[ -n "${TGDL_UPLOAD_RETRIES:-}" ]]  && printf 'UploadRetries = %s\n' "$TGDL_UPLOAD_RETRIES"
        [[ -n "${TGDL_MERGE_FORMAT:-}" ]]     && printf 'MergeFormat = %s\n' "$TGDL_MERGE_FORMAT"
        [[ -n "${TGDL_MAX_MEDIA_SIZE:-}" ]]   && printf 'MaxMediaSizeBytes = %s\n' "$TGDL_MAX_MEDIA_SIZE"
        [[ -n "${TGDL_EXTRACT_AUDIO:-}" ]]      && is_bool "$TGDL_EXTRACT_AUDIO"      && printf 'ExtractAudio = %s\n' "$TGDL_EXTRACT_AUDIO"
        [[ -n "${TGDL_SEND_TO_REQUESTER:-}" ]]  && is_bool "$TGDL_SEND_TO_REQUESTER"  && printf 'AlsoSendMediaToRequester = %s\n' "$TGDL_SEND_TO_REQUESTER"
        [[ -n "${TGDL_ALLOW_PRIVATE_URLS:-}" ]] && is_bool "$TGDL_ALLOW_PRIVATE_URLS" && printf 'AllowPrivateUrls = %s\n' "$TGDL_ALLOW_PRIVATE_URLS"
        [[ -n "${TGDL_ALLOW_PLAYLISTS:-}" ]]    && is_bool "$TGDL_ALLOW_PLAYLISTS"    && printf 'AllowPlaylists = %s\n' "$TGDL_ALLOW_PLAYLISTS"
        [[ -n "${TGDL_UPDATE_YTDLP:-}" ]]       && is_bool "$TGDL_UPDATE_YTDLP"       && printf 'UpdateYtDlp = %s\n' "$TGDL_UPDATE_YTDLP"
        [[ -n "${TGDL_UPDATE_FFMPEG:-}" ]]      && is_bool "$TGDL_UPDATE_FFMPEG"      && printf 'UpdateFfmpeg = %s\n' "$TGDL_UPDATE_FFMPEG"
        [[ -n "${TGDL_COOKIE_STORE_DIR:-}" ]] && printf 'CookieStoreDir = %s\n' "$TGDL_COOKIE_STORE_DIR"
        [[ -n "${TGDL_YTDLP_PROXY:-}" ]]     && printf 'YtDlpProxy = %s\n' "$TGDL_YTDLP_PROXY"
        [[ -n "${TGDL_YTDLP_EXTRA_ARGS:-}" ]] && printf 'YtDlpExtraArgs = %s\n' "$TGDL_YTDLP_EXTRA_ARGS"
        # 该键支持显式留空以禁用（写空值即可）
        if [[ -n "${TGDL_YTDLP_PLAYER_CLIENTS+x}" ]]; then
            printf 'YtDlpYoutubePlayerClients = %s\n' "$TGDL_YTDLP_PLAYER_CLIENTS"
        fi
        [[ -n "${TGDL_DEFAULT_MODE:-}" ]] && printf 'TgdlDefaultMode = %s\n' "$TGDL_DEFAULT_MODE"
        # 运行时状态目录：固定写入 tgdl-data 卷内（languages.json / overlay / pending-notify 跨重建持久），
        # 可由 TGDL_STATE_DIR 覆盖；不写则默认推导为 /var/lib/tgdl-bot（非卷，pull 重建会丢状态）
        printf 'StateDir = %s\n' "${TGDL_STATE_DIR:-$API_DATA_DIR}"
        # bot 全局默认语言：auto（跟随用户 language_code）/ en / zh；缺省即 auto
        [[ -n "${TGDL_LANGUAGE:-}" ]] && printf 'TgdlLanguage = %s\n' "$TGDL_LANGUAGE"
    } > "$CONFIG_FILE"
    chown "$RUN_USER":"$RUN_USER" "$CONFIG_FILE" 2>/dev/null || true
    log "已根据环境变量生成配置：${CONFIG_FILE}"
fi

# ---------- 4. telegram-bot-api 凭据 ----------
require TGDL_API_ID
require TGDL_API_HASH

# ---------- 5. 启动 telegram-bot-api（后台，降权 tgdl-bot） ----------
log "启动 telegram-bot-api（--local，端口 8081）…"
setpriv --reuid="$RUN_USER" --regid="$RUN_USER" --init-groups \
    "$API_DIR/telegram-bot-api" \
    --api-id="$TGDL_API_ID" \
    --api-hash="$TGDL_API_HASH" \
    --local \
    --dir="$API_DATA_DIR" \
    --http-port=8081 \
    --max-webhook-connections=100 &
TBA_PID=$!

# ---------- 6. 等待 tba 就绪（任何 HTTP 响应即认为已监听） ----------
ready=0
for _ in $(seq 1 30); do
    code="$(curl -s -o /dev/null -w '%{http_code}' http://127.0.0.1:8081/ 2>/dev/null || true)"
    if [[ -n "$code" && "$code" != "000" ]]; then ready=1; break; fi
    sleep 1
done
if [[ "$ready" -eq 0 ]]; then
    kill "$TBA_PID" 2>/dev/null || true
    die "telegram-bot-api 未在 30 秒内就绪，请检查 TGDL_API_ID / TGDL_API_HASH 与网络。"
fi
log "telegram-bot-api 就绪。"

# ---------- 7. 前台运行 bot，优雅处理信号 ----------
BOT_PID=0
# shellcheck disable=SC2329 # 由下方 trap shutdown TERM INT 间接调用
shutdown() {
    log "收到退出信号，正在关闭…"
    [[ "$BOT_PID" -ne 0 ]] && kill -TERM "$BOT_PID" 2>/dev/null || true
    [[ "$TBA_PID" -ne 0 ]] && kill -TERM "$TBA_PID" 2>/dev/null || true
    wait 2>/dev/null || true
    exit 0
}
trap shutdown TERM INT

log "启动 tgdl-bot…"
setpriv --reuid="$RUN_USER" --regid="$RUN_USER" --init-groups \
    "$INSTALL_DIR/tgdl-bot" --config "$CONFIG_FILE" &
BOT_PID=$!

wait "$BOT_PID"
RC=$?
kill "$TBA_PID" 2>/dev/null || true
exit "$RC"
