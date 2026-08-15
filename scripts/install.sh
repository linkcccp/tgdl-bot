#!/usr/bin/env bash
# ============================================================
# tgdl-bot v2 一键部署（Docker 沙盒，任何支持 Docker 的发行版）
#
# 用法：
#   curl -fsSL https://raw.githubusercontent.com/linkcccp/tgdl-bot/main/scripts/install.sh | sudo bash
#
# 流程：root 检查 -> Docker 检测/自动安装 -> 生成 .env -> 拉取镜像并启动 -> 提示
# 幂等：重复执行 = 拉取最新镜像并重建容器（配置 .env 不会被覆盖）。
# ============================================================
set -euo pipefail

REPO="${TGDL_REPO:-linkcccp/tgdl-bot}"
RAW_BASE="https://raw.githubusercontent.com/${REPO}/main"
INSTALL_DIR="/opt/tgdl-bot"
IMAGE="ghcr.io/${REPO}:latest"

log()  { printf '\033[1;32m[INFO ]\033[0m %s\n' "$*"; }
warn() { printf '\033[1;33m[警告 ]\033[0m %s\n' "$*" >&2; }
err()  { printf '\033[1;31m[错误 ]\033[0m %s\n' "$*" >&2; }
die()  { err "$*"; exit 1; }

# ---------- 1. root 检查 ----------
if [[ $EUID -ne 0 ]]; then
    die "需要 root 权限，请使用：curl -fsSL https://raw.githubusercontent.com/${REPO}/main/scripts/install.sh | sudo bash"
fi

# ---------- 2. Docker 检测 / 自动安装 ----------
if command -v docker >/dev/null 2>&1 && docker info >/dev/null 2>&1; then
    log "检测到 Docker：$(docker --version 2>/dev/null || echo 未知版本)"
else
    log "未检测到可用的 Docker，开始自动安装…"
    if ! curl -fsSL https://get.docker.com | sh; then
        warn "官方安装脚本失败，尝试 apt 安装 docker.io…"
        apt-get update -qq || true
        apt-get install -y -qq docker.io || die "Docker 安装失败，请手动安装 Docker 后重新执行本脚本。"
    fi
    command -v systemctl >/dev/null 2>&1 && systemctl enable --now docker 2>/dev/null || true
    if ! docker info >/dev/null 2>&1; then
        die "Docker 已安装但守护进程不可用，请手动启动 Docker 后重新执行本脚本。"
    fi
    log "Docker 已安装：$(docker --version 2>/dev/null || echo 未知版本)"
fi

# ---------- 3. 配置目录与 .env ----------
mkdir -p "$INSTALL_DIR"
cd "$INSTALL_DIR"

if [[ ! -f compose.yaml ]]; then
    log "下载 compose.yaml…"
    curl -fsSL --retry 3 --retry-delay 2 -o compose.yaml "$RAW_BASE/docker/compose.yaml" \
        || die "下载 compose.yaml 失败，请检查网络后重试。"
fi

if [[ ! -f .env ]]; then
    log "生成 .env 模板…"
    curl -fsSL --retry 3 --retry-delay 2 -o .env "$RAW_BASE/docker/.env.example" \
        || die "下载 .env 模板失败，请检查网络后重试。"
    chmod 600 .env
    echo "    已生成 ${INSTALL_DIR}/.env，请填写后重启。"
fi

# ---------- 4. 拉取镜像并启动 ----------
log "拉取镜像 ${IMAGE}…"
docker pull "$IMAGE" >/dev/null || die "镜像拉取失败，请检查网络后重试。"

started=0
if docker compose version >/dev/null 2>&1; then
    log "使用 docker compose 启动…"
    docker compose up -d || { warn "docker compose 启动失败，尝试 docker run 兜底…"; :; }
    started=1
elif command -v docker-compose >/dev/null 2>&1; then
    log "使用 docker-compose 启动…"
    docker-compose up -d || { warn "docker-compose 启动失败，尝试 docker run 兜底…"; :; }
    started=1
fi

if [[ "$started" -eq 0 ]] || ! docker inspect tgdl-bot >/dev/null 2>&1; then
    log "使用 docker run 启动…"
    docker rm -f tgdl-bot 2>/dev/null || true
    docker run -d --name tgdl-bot --restart unless-stopped \
        --env-file "$INSTALL_DIR/.env" \
        -v tgdl-data:/opt/tgdl-bot/api-data \
        -v tgdl-tmp:/var/lib/tgdl-bot/tmp \
        -v tgdl-bin:/opt/tgdl-bot/bin \
        "$IMAGE" || die "容器启动失败，请检查配置后重试。"
fi

log "容器状态："
docker ps --filter name=tgdl-bot --format '  {{.Names}}  {{.Status}}  {{.Image}}'

# 清理旧镜像（悬空），避免磁盘堆积；不影响在用镜像与卷
log "清理悬空旧镜像…"
docker image prune -f >/dev/null 2>&1 || true

# ---------- 5. 提示 ----------
cat <<EOF

============================================================
安装完成。首次使用前请填写配置（必做）：
  1) 编辑 ${INSTALL_DIR}/.env
       - TGDL_BOT_TOKEN      （@BotFather 创建 bot 后获得）
       - TGDL_TARGET_CHANNELS（目标频道/群组 ID，逗号分隔）
       - TGDL_ALLOWED_USERS  （允许使用的用户 ID，逗号分隔）
       - TGDL_API_ID / TGDL_API_HASH（https://my.telegram.org 获取）
  2) 重启生效：
       cd ${INSTALL_DIR}
       sudo docker compose up -d
  3) 查看状态与日志：
       docker ps
       docker logs -f tgdl-bot
  4) 升级到最新版本：
       cd ${INSTALL_DIR} && sudo docker compose pull && sudo docker compose up -d
  5) 私聊向 bot 发送 /update 可自动更新 yt-dlp 与 ffmpeg（存于 tgdl-bin 卷）。
============================================================
EOF

log "安装完成。"
