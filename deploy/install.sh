#!/usr/bin/env bash
# ============================================================
# tgdl-bot 部署脚本（Debian 12 / root 执行）
# 用法：sudo bash deploy/install.sh <tgdl-bot 二进制路径>
# 例：  sudo bash deploy/install.sh publish/linux-x64/tgdl-bot
# ============================================================
set -euo pipefail

if [[ $EUID -ne 0 ]]; then
    echo "错误：请使用 root 执行本脚本（sudo bash $0 <binary>）。" >&2
    exit 1
fi

BOT_BIN="${1:-}"
if [[ -z "$BOT_BIN" || ! -f "$BOT_BIN" ]]; then
    echo "错误：请提供 tgdl-bot 二进制路径作为第一个参数。" >&2
    exit 1
fi

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
INSTALL_DIR="/opt/tgdl-bot"
BIN_DIR="$INSTALL_DIR/bin"
API_DIR="$INSTALL_DIR/api"
DATA_DIR="/var/lib/tgdl-bot"
API_DATA_DIR="/var/lib/tgdl-bot-api"
SERVICE_USER="tgdl-bot"

echo ">>> 1/6 安装依赖（curl、xz-utils）"
apt-get update -qq
apt-get install -y -qq curl xz-utils >/dev/null

echo ">>> 2/6 创建用户与目录"
id "$SERVICE_USER" >/dev/null 2>&1 || useradd --system --no-create-home --shell /usr/sbin/nologin "$SERVICE_USER"
mkdir -p "$INSTALL_DIR" "$BIN_DIR" "$API_DIR" "$DATA_DIR" "$API_DATA_DIR"
chown -R "$SERVICE_USER":"$SERVICE_USER" "$INSTALL_DIR" "$DATA_DIR" "$API_DATA_DIR"

echo ">>> 3/6 安装二进制与配置文件"
if [[ -f "$INSTALL_DIR/tgdl-bot" ]] && cmp -s "$BOT_BIN" "$INSTALL_DIR/tgdl-bot"; then
    echo "    已存在相同版本二进制，跳过覆盖。"
else
    install -o "$SERVICE_USER" -g "$SERVICE_USER" -m 0755 "$BOT_BIN" "$INSTALL_DIR/tgdl-bot"
fi

if [[ ! -f "$INSTALL_DIR/config.conf" ]]; then
    install -o "$SERVICE_USER" -g "$SERVICE_USER" -m 0644 "$SCRIPT_DIR/config.conf.example" "$INSTALL_DIR/config.conf"
    echo "    已生成 $INSTALL_DIR/config.conf，请填写后重启服务。"
fi

if [[ ! -f "$INSTALL_DIR/api.env" ]]; then
    install -o "$SERVICE_USER" -g "$SERVICE_USER" -m 0600 "$SCRIPT_DIR/api.env.example" "$INSTALL_DIR/api.env"
    echo "    已生成 $INSTALL_DIR/api.env，请填写 API_ID / API_HASH。"
fi

echo ">>> 4/6 安装 systemd 单元"
install -m 0644 "$SCRIPT_DIR/tgdl-bot.service" /etc/systemd/system/tgdl-bot.service
install -m 0644 "$SCRIPT_DIR/telegram-bot-api.service" /etc/systemd/system/telegram-bot-api.service
if command -v systemctl >/dev/null 2>&1; then
    systemctl daemon-reload
else
    echo "    [跳过] 未检测到 systemd（容器/受限环境），单元文件已安装到 /etc/systemd/system/"
fi

echo ">>> 5/6 安装 telegram-bot-api（本地 Bot API Server）"
if [[ ! -x "$API_DIR/telegram-bot-api" ]]; then
    # 优先从脚本同目录（deploy/）找；再退回父目录（压缩包根，与 tgdl-bot 并列）找
    TBA_SRC=""
    if [[ -f "$SCRIPT_DIR/telegram-bot-api" ]]; then
        TBA_SRC="$SCRIPT_DIR/telegram-bot-api"
    elif [[ -f "$(dirname "$SCRIPT_DIR")/telegram-bot-api" ]]; then
        TBA_SRC="$(dirname "$SCRIPT_DIR")/telegram-bot-api"
    fi

    if [[ -n "$TBA_SRC" ]]; then
        install -o "$SERVICE_USER" -g "$SERVICE_USER" -m 0755 "$TBA_SRC" "$API_DIR/telegram-bot-api"
    else
        echo "    [可选] 未找到 telegram-bot-api 二进制，请手动下载后放置到 $API_DIR/telegram-bot-api"
        echo "    下载：https://github.com/tdlib/telegram-bot-api/releases （linux amd64）"
    fi
fi

echo ">>> 6/6 启动服务"
if command -v systemctl >/dev/null 2>&1; then
    systemctl enable --now telegram-bot-api.service tgdl-bot.service || {
        echo "    警告：服务启动失败，请先填写 $INSTALL_DIR/config.conf 与 $INSTALL_DIR/api.env 后再启动："
        echo "    systemctl restart telegram-bot-api tgdl-bot"
    }
else
    echo "    [跳过] 未检测到 systemd，请手动启动：$INSTALL_DIR/tgdl-bot"
fi

echo ""
echo "部署完成。接下来："
echo "  1) 编辑 $INSTALL_DIR/config.conf（Token、频道/用户白名单等）"
echo "  2) 编辑 $INSTALL_DIR/api.env（API_ID / API_HASH）"
echo "  3) systemctl restart telegram-bot-api tgdl-bot"
echo "  4) systemctl status tgdl-bot 查看状态；journalctl -u tgdl-bot -f 查看日志"
echo "  5) 启动后向 bot 发送 /update 自动安装最新 yt-dlp 与 ffmpeg"
