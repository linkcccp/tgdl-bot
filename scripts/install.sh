#!/usr/bin/env bash
# ============================================================
# tgdl-bot 一条命令安装脚本（Debian 12 / x86_64）
#
# 用法：
#   curl -fsSL https://raw.githubusercontent.com/linkcccp/tgdl-bot/main/scripts/install.sh | sudo bash
#
# 环境变量（可选）：
#   TGDL_VERSION   指定安装版本（如 v1.0.0），默认最新 Release
#   TGDL_REPO      指定仓库，默认 linkcccp/tgdl-bot
#   TGDL_API_BASE  指定 GitHub API 地址，默认 https://api.github.com
#
# 流程：前置检查 -> 拉取 Release -> 下载/解压 -> 可选自动装 telegram-bot-api
#       -> 复用 deploy/install.sh -> 验证 -> 提示填写配置
# 可重入：config.conf / api.env 不会被覆盖；相同版本二进制不会被重复覆盖。
# ============================================================
set -euo pipefail

REPO="${TGDL_REPO:-linkcccp/tgdl-bot}"
API_BASE="${TGDL_API_BASE:-https://api.github.com}"
INSTALL_VERSION="${TGDL_VERSION:-}"

INSTALL_DIR="/opt/tgdl-bot"
SERVICE_USER="tgdl-bot"

log()  { printf '\033[1;32m[INFO ]\033[0m %s\n' "$*"; }
warn() { printf '\033[1;33m[警告 ]\033[0m %s\n' "$*" >&2; }
err()  { printf '\033[1;31m[错误 ]\033[0m %s\n' "$*" >&2; }
die()  { err "$*"; exit 1; }

# ---------- 1. root 检查 ----------
if [[ $EUID -ne 0 ]]; then
    die "需要 root 权限，请使用：curl -fsSL https://raw.githubusercontent.com/${REPO}/main/scripts/install.sh | sudo bash"
fi

# ---------- 2. 前置检查：系统与依赖 ----------
ARCH="$(uname -m)"
[[ "$ARCH" == "x86_64" ]] || die "仅支持 x86_64 架构（当前：$ARCH）。"

if [[ -f /etc/os-release ]]; then
    # shellcheck disable=SC1091
    . /etc/os-release
    case "${ID:-} ${ID_LIKE:-}" in
        *debian*) : ;;
        *) die "仅支持 Debian 系系统（当前：ID=${ID:-未知}）。" ;;
    esac
    if [[ -n "${VERSION_ID:-}" && "$VERSION_ID" != "12" ]]; then
        warn "当前 Debian 版本为 ${VERSION_ID}，官方验证环境为 Debian 12，可能存在差异。"
    fi
else
    die "无法识别操作系统（缺少 /etc/os-release）。"
fi

log "安装依赖：curl、jq、xz-utils"
apt-get update -qq || die "apt 更新失败，请检查网络与软件源后重试。"
apt-get install -y -qq curl jq xz-utils >/dev/null || die "依赖安装失败，请检查网络后重试。"

# ---------- 3. 获取 Release 信息并下载 ----------
TMPDIR="$(mktemp -d)"
trap 'rm -rf "$TMPDIR"' EXIT

if [[ -n "$INSTALL_VERSION" ]]; then
    RELEASE_URL="${API_BASE}/repos/${REPO}/releases/tags/${INSTALL_VERSION}"
else
    RELEASE_URL="${API_BASE}/repos/${REPO}/releases/latest"
fi

log "获取 Release 信息：${RELEASE_URL}"
RELEASE_JSON="$(curl -fsSL --retry 3 --retry-delay 2 "$RELEASE_URL")" \
    || die "获取 Release 信息失败（仓库不存在、网络错误或 GitHub API 限流）。"

TAG="$(printf '%s' "$RELEASE_JSON" | jq -r '.tag_name // empty')"
[[ -n "$TAG" ]] || die "Release 信息不包含 tag_name。"

ASSET_URL="$(printf '%s' "$RELEASE_JSON" \
    | jq -r '.assets[] | select(.name | test("tgdl-bot-.*-linux-x64\\.tar\\.gz$")) | .browser_download_url' \
    | head -n1)"
[[ -n "$ASSET_URL" ]] || die "Release ${TAG} 中未找到 tgdl-bot-*-linux-x64.tar.gz 资产。"

log "下载版本：${TAG}"
TARBALL="$TMPDIR/tgdl-bot.tar.gz"
curl -fsSL --retry 3 --retry-delay 2 -o "$TARBALL" "$ASSET_URL" || die "下载资产失败：${ASSET_URL}"

log "解压压缩包…"
tar -xzf "$TARBALL" -C "$TMPDIR"

BOT_BIN="$TMPDIR/tgdl-bot"
[[ -f "$BOT_BIN" ]] || die "压缩包中未找到 tgdl-bot 二进制。"
chmod +x "$BOT_BIN"
[[ -x "$BOT_BIN" ]] || die "tgdl-bot 二进制不可执行。"
[[ -f "$TMPDIR/deploy/install.sh" ]] || die "压缩包中未找到 deploy/install.sh。"

# ---------- 4. telegram-bot-api 二进制（随 Release 附带） ----------
# telegram-bot-api 作为子项目（third_party/telegram-bot-api）随仓库维护，
# 由 GitHub Actions 在 Release 时编译好并打进 tgdl-bot-*-linux-x64.tar.gz。
# VPS 无需本地 clone/构建，直接安装压缩包内的二进制即可（弱性能 VPS 友好）。
TBA_BIN="$TMPDIR/telegram-bot-api"
if [[ ! -f "$TBA_BIN" ]]; then
    warn "压缩包中未找到 telegram-bot-api 二进制（该 Release 可能较旧或 CI 未附带构建产物）。"
    warn "Bot 依赖本地 Bot API Server 才能工作，请手动安装："
    warn "  将 linux x64 的 telegram-bot-api 放到 /opt/tgdl-bot/api/telegram-bot-api 后：systemctl restart telegram-bot-api"
else
    chmod +x "$TBA_BIN" 2>/dev/null || true
    log "压缩包内已含 telegram-bot-api 二进制，将由 deploy/install.sh 安装。"
fi

# ---------- 5. 复用现有部署逻辑 ----------
log "执行部署脚本（创建用户/目录、安装 systemd 单元）…"
bash "$TMPDIR/deploy/install.sh" "$BOT_BIN"

# ---------- 6. 验证 ----------
log "开始安装验证…"

if command -v systemctl >/dev/null 2>&1; then
    for svc in telegram-bot-api tgdl-bot; do
        ACTIVE="$(systemctl is-active "$svc" 2>/dev/null || echo unknown)"
        ENABLED="$(systemctl is-enabled "$svc" 2>/dev/null || echo unknown)"
        printf '  %-18s active=%-10s enabled=%-10s\n' "$svc" "$ACTIVE" "$ENABLED"
    done

    echo "  --- 最近日志 ERROR 检查 ---"
    if command -v journalctl >/dev/null 2>&1 \
        && journalctl -u tgdl-bot --since "-5 min" --no-pager 2>/dev/null | grep -qE "\[ERROR\]"; then
        warn "tgdl-bot 最近日志存在 ERROR（配置为占位符时无法连接 Telegram 属正常现象）。"
    else
        log "tgdl-bot 最近日志无 ERROR。"
    fi
else
    warn "未检测到 systemd（受限环境），跳过服务验证。"
fi

if [[ -x "$INSTALL_DIR/tgdl-bot" ]]; then
    log "运行 --smoke-test 自检（约 4 秒）…"
    runuser -u "$SERVICE_USER" -- "$INSTALL_DIR/tgdl-bot" --smoke-test 3 \
        >"$TMPDIR/smoke.log" 2>&1 &
    SMOKE_PID=$!

    # 独立测量：扫描 /proc 中所有 "tgdl-bot --smoke-test" 进程的 VmRSS 取最大值
    # （runuser 会 fork 真实子进程，直接读 $SMOKE_PID 会得到 wrapper 的小数值）
    # 注意：进程可能在读取间隙退出，需在子 shell 内抑制 bash 的重定向报错，
    # 避免刷出 "No such file or directory" 噪音。
    MAX_RSS_KB=0
    for _ in 1 2 3 4; do
        for d in /proc/[0-9]*; do
            if ( tr '\0' ' ' <"$d/cmdline" 2>/dev/null ) 2>/dev/null | grep -q "tgdl-bot --smoke-test"; then
                V="$( ( awk '/VmRSS/{print $2}' "$d/status" 2>/dev/null ) 2>/dev/null || echo 0 )"
                [[ "${V:-0}" -gt "$MAX_RSS_KB" ]] && MAX_RSS_KB=$V
            fi
        done
        sleep 1
    done
    PROC_RSS_MB=$((MAX_RSS_KB / 1024))

    if wait "$SMOKE_PID"; then
        log "自检通过（退出码 0）。"
    else
        warn "自检退出码非 0，输出最后几行："
        tail -n 5 "$TMPDIR/smoke.log" | sed 's/^/      /'
    fi

    # 交叉验证：解析 bot 自报 RSS（WorkingSet）
    SELF_RSS_MB="$(grep -oE 'RSS ≈ [0-9]+ MB' "$TMPDIR/smoke.log" | tail -n1 | grep -oE '[0-9]+' | head -n1)"
    RSS_MB=""
    if (( MAX_RSS_KB > 0 )); then
        RSS_MB=$PROC_RSS_MB
    elif [[ -n "$SELF_RSS_MB" ]]; then
        RSS_MB=$((SELF_RSS_MB))
    fi

    if [[ -n "$RSS_MB" ]]; then
        if (( RSS_MB < 100 )); then
            log "内存 RSS 约 ${RSS_MB} MB（目标 < 100MB，达标）。"
        else
            warn "内存 RSS 约 ${RSS_MB} MB，超出 100MB 目标。"
        fi
    else
        warn "无法获取进程 RSS（进程可能已提前退出）。"
    fi
else
    warn "未找到 ${INSTALL_DIR}/tgdl-bot，跳过自检。"
fi

# ---------- 7. 提示填写配置 ----------
cat <<'EOF'

============================================================
安装完成。首次使用前请填写配置（必做）：
  1) 编辑 /opt/tgdl-bot/config.conf
       - BotToken        （@BotFather 创建 bot 后获得）
       - TargetChannelIds（目标频道/群组 ID，可多个，逗号分隔）
       - AllowedUserIds  （允许使用 bot 的用户 ID）
  2) 编辑 /opt/tgdl-bot/api.env
       - API_ID / API_HASH（https://my.telegram.org 获取）
  3) 重启服务生效：
       sudo nano /opt/tgdl-bot/config.conf
       sudo nano /opt/tgdl-bot/api.env
       sudo systemctl restart telegram-bot-api tgdl-bot
  4) 查看状态与日志：
       systemctl status tgdl-bot
       journalctl -u tgdl-bot -f
  5) 私聊向 bot 发送 /update 可自动安装最新 yt-dlp 与 ffmpeg。
============================================================
EOF

log "安装完成。"
