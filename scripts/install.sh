#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-only
# Copyright (C) 2026 linkcccp
# ============================================================
# tgdl-bot v2 一键部署（Docker 沙盒，任何支持 Docker 的发行版）
#
# 用法：
#   curl -fsSL https://raw.githubusercontent.com/linkcccp/tgdl-bot/main/scripts/install.sh | sudo bash
#
# 流程：root 检查 -> Docker 检测/自动安装 -> 交互式安装向导（有 TTY 时）：
#   语言选择 -> 必填项逐项输入与校验 -> 自动写 .env -> 拉取镜像并启动
#   无 TTY / CI 环境自动降级为非交互模式（生成 .env 模板供手动填写）
# 幂等：重复执行 = 拉取最新镜像并重建容器（.env 不会被覆盖；
#   .env 已存在且必填项齐全时跳过向导直接启动）。
# ============================================================
set -euo pipefail

REPO="${TGDL_REPO:-linkcccp/tgdl-bot}"
RAW_BASE="https://raw.githubusercontent.com/${REPO}/main"
INSTALL_DIR="/opt/tgdl-bot"
IMAGE="ghcr.io/${REPO}:latest"

# 必填项（数组顺序即向导提问顺序）
REQUIRED_KEYS=(TGDL_BOT_TOKEN TGDL_TARGET_CHANNELS TGDL_ALLOWED_USERS TGDL_API_ID TGDL_API_HASH)
MAX_ATTEMPTS=5

WIZ_LANG="zh"
SKIPPED=()
declare -A VALUES

log()  { printf '\033[1;32m[INFO ]\033[0m %s\n' "$*"; }
warn() { printf '\033[1;33m[警告 ]\033[0m %s\n' "$*" >&2; }
err()  { printf '\033[1;31m[错误 ]\033[0m %s\n' "$*" >&2; }
die()  { err "$*"; exit 1; }

# 双语输出：wiz "中文" "English"
wiz() {
    if [[ "$WIZ_LANG" == "en" ]]; then printf '%s\n' "$2"; else printf '%s\n' "$1"; fi
}

# ============================================================
# 校验函数（就地重问，单项连续 MAX_ATTEMPTS 次失败则跳过）
# ============================================================

# Bot Token：数字:字母数字下划线短横线，长度 <= 200
validate_token() {
    local v="$1"
    [[ -n "$v" ]] || return 1
    (( ${#v} <= 200 )) || return 1
    [[ "$v" =~ ^[0-9]+:[A-Za-z0-9_-]+$ ]]
}

# 目标频道/群组 ID：逗号分隔整数（允许负数，如 -100xxx…），至少 1 个，去重
validate_channels() {
    local v="$1" i
    [[ -n "$v" ]] || return 1
    local -a parts=()
    IFS=',' read -ra parts <<< "$v"
    (( ${#parts[@]} >= 1 )) || return 1
    local -A seen=()
    for i in "${parts[@]}"; do
        [[ "$i" =~ ^-?[0-9]+$ ]] || return 1
        [[ -n "${seen[$i]+x}" ]] && return 1
        seen[$i]=1
    done
    return 0
}

# 允许用户 ID：逗号分隔正整数，至少 1 个，去重
validate_users() {
    local v="$1" i
    [[ -n "$v" ]] || return 1
    local -a parts=()
    IFS=',' read -ra parts <<< "$v"
    (( ${#parts[@]} >= 1 )) || return 1
    local -A seen=()
    for i in "${parts[@]}"; do
        [[ "$i" =~ ^[1-9][0-9]*$ ]] || return 1
        [[ -n "${seen[$i]+x}" ]] && return 1
        seen[$i]=1
    done
    return 0
}

# API ID：正整数，int32 范围（先限位数再算术比较，防超长数字串溢出）
validate_api_id() {
    local v="$1"
    [[ "$v" =~ ^[1-9][0-9]*$ ]] || return 1
    (( ${#v} <= 10 )) || return 1
    (( v <= 2147483647 )) || return 1
}

# API Hash：32 位十六进制
validate_api_hash() {
    [[ "$1" =~ ^[0-9a-fA-F]{32}$ ]]
}

validate_key() {
    case "$1" in
    TGDL_BOT_TOKEN)       validate_token "$2" ;;
    TGDL_TARGET_CHANNELS) validate_channels "$2" ;;
    TGDL_ALLOWED_USERS)   validate_users "$2" ;;
    TGDL_API_ID)          validate_api_id "$2" ;;
    TGDL_API_HASH)        validate_api_hash "$2" ;;
    *) return 1 ;;
    esac
}

# ============================================================
# .env 辅助
# ============================================================

# 从 .env 读取键值（不做 source，避免执行任意内容）
get_env() {
    local key="$1" line
    [[ -f "$INSTALL_DIR/.env" ]] || return 1
    line="$(grep -E "^${key}=" "$INSTALL_DIR/.env" | head -n1 || true)"
    [[ -n "$line" ]] || return 1
    printf '%s' "${line#*=}"
}

env_has_all_required() {
    local key
    for key in "${REQUIRED_KEYS[@]}"; do
        [[ -n "$(get_env "$key" || true)" ]] || return 1
    done
    return 0
}

# .env 是否仍是未编辑的上游模板（新装场景：占位值非空但未真正配置）
env_is_pristine_template() {
    local tmp
    tmp="$(mktemp)"
    if curl -fsSL --retry 2 "$RAW_BASE/docker/.env.example" -o "$tmp" 2>/dev/null; then
        diff -q "$tmp" "$INSTALL_DIR/.env" >/dev/null 2>&1
        local rc=$?
        rm -f "$tmp"
        return "$rc"
    fi
    rm -f "$tmp"
    return 1
}

# ============================================================
# 向导交互（/dev/tty 专用输入，与 curl 管道 stdin 隔离）
# ============================================================

choose_language() {
    local choice="" _
    for _ in $(seq 1 "$MAX_ATTEMPTS"); do
        printf '\n%s\n  1) 中文 / Chinese\n  2) English\n> ' \
            "$(wiz "请选择安装向导语言（同时作为 Bot 全局默认语言）" \
                   "Select wizard language (also the bot's default language)")"
        read -r choice < /dev/tty || choice=""
        case "$choice" in
        1|zh|cn|中文) WIZ_LANG="zh"; return 0 ;;
        2|en|english) WIZ_LANG="en"; return 0 ;;
        *) warn "$(wiz "输入无效，请输入 1 或 2。" "Invalid input, please enter 1 or 2.")" ;;
        esac
    done
    WIZ_LANG="zh"
    warn "$(wiz "未正确选择，默认使用中文向导。" "Invalid selection, defaulting to Chinese wizard.")"
}

prompt_for() {
    local key="$1" current="$2"
    case "$key" in
    TGDL_BOT_TOKEN)
        wiz "Bot Token（@BotFather 创建 bot 后获得）— 当前：${current}" \
            "Bot Token (create a bot via @BotFather) — current: ${current}" ;;
    TGDL_TARGET_CHANNELS)
        wiz "目标频道/群组 ID（逗号分隔，支持负数，如 -1001234567890）— 当前：${current}" \
            "Target channel/group IDs (comma separated, negative allowed, e.g. -1001234567890) — current: ${current}" ;;
    TGDL_ALLOWED_USERS)
        wiz "允许私聊使用的用户 ID（逗号分隔正整数）— 当前：${current}" \
            "Allowed user IDs for private chat (comma separated positive integers) — current: ${current}" ;;
    TGDL_API_ID)
        wiz "Telegram API ID（正整数，my.telegram.org 获取）— 当前：${current}" \
            "Telegram API ID (positive integer, get at my.telegram.org) — current: ${current}" ;;
    TGDL_API_HASH)
        wiz "Telegram API Hash（32 位十六进制）— 当前：${current}" \
            "Telegram API Hash (32 hex chars) — current: ${current}" ;;
    esac
}

err_for() {
    local key="$1"
    case "$key" in
    TGDL_BOT_TOKEN)
        err "$(wiz "格式错误：应为「数字:字母数字」，如 123456789:AAaa-xx_yy。请重新输入。" \
                  "Invalid format: expected \"digits:alnum\", e.g. 123456789:AAaa-xx_yy. Please retry.")" ;;
    TGDL_TARGET_CHANNELS)
        err "$(wiz "格式错误：应为逗号分隔的整数（可负，如 -1001234567890），且不能重复。请重新输入。" \
                  "Invalid format: comma separated integers (negative allowed, e.g. -1001234567890), no duplicates. Please retry.")" ;;
    TGDL_ALLOWED_USERS)
        err "$(wiz "格式错误：应为逗号分隔的正整数，且不能重复。请重新输入。" \
                  "Invalid format: comma separated positive integers, no duplicates. Please retry.")" ;;
    TGDL_API_ID)
        err "$(wiz "格式错误：应为正整数（int 范围）。请重新输入。" \
                  "Invalid format: expected a positive integer (int range). Please retry.")" ;;
    TGDL_API_HASH)
        err "$(wiz "格式错误：应为 32 位十六进制（0-9a-fA-F）。请重新输入。" \
                  "Invalid format: expected 32 hex chars (0-9a-fA-F). Please retry.")" ;;
    esac
}

wizard() {
    local key current val ok _
    printf '\n========== %s ==========\n' \
        "$(wiz "必填项配置（直接回车可保留当前值）" "Required settings (press Enter to keep current value)")"
    for key in "${REQUIRED_KEYS[@]}"; do
        current="$(get_env "$key" || true)"
        current="${current:-(未填写 / empty)}"
        ok=0
        for _ in $(seq 1 "$MAX_ATTEMPTS"); do
            prompt_for "$key" "$current"
            printf '> '
            read -r val < /dev/tty || val=""
            val="${val#"${val%%[![:space:]]*}"}"   # 去首部空白
            val="${val%"${val##*[![:space:]]}"}"   # 去尾部空白
            if validate_key "$key" "$val"; then
                VALUES[$key]="$val"
                ok=1
                break
            fi
            err_for "$key"
        done
        if [[ "$ok" -eq 1 ]]; then
            log "$(wiz "${key} 校验通过 ✓" "${key} OK")"
        else
            warn "$(wiz "${key} 连续 ${MAX_ATTEMPTS} 次校验失败，已跳过；请稍后手动填写。" \
                      "${key} failed ${MAX_ATTEMPTS} attempts, skipped; please fill in manually later.")"
            SKIPPED+=("$key")
        fi
    done
}

# 基于 .env.example 模板写 .env：sed 替换必填项 -> 追加 TGDL_LANGUAGE -> chmod 600
write_env() {
    local key val
    for key in "${!VALUES[@]}"; do
        val="${VALUES[$key]}"
        if grep -qE "^${key}=" "$INSTALL_DIR/.env"; then
            sed -i "s|^${key}=.*|${key}=${val}|" "$INSTALL_DIR/.env"
        else
            printf '%s=%s\n' "$key" "$val" >> "$INSTALL_DIR/.env"
        fi
    done
    # 向导语言 = 部署者语言 -> Bot 全局默认语言（后续可用 /language 或改 .env 调整）
    if grep -qE "^TGDL_LANGUAGE=" "$INSTALL_DIR/.env"; then
        sed -i "s|^TGDL_LANGUAGE=.*|TGDL_LANGUAGE=${WIZ_LANG}|" "$INSTALL_DIR/.env"
    else
        printf 'TGDL_LANGUAGE=%s\n' "$WIZ_LANG" >> "$INSTALL_DIR/.env"
    fi
    chmod 600 "$INSTALL_DIR/.env"
    log "$(wiz "已写入 ${INSTALL_DIR}/.env（权限 600）" "Wrote ${INSTALL_DIR}/.env (mode 600)")"
}

# ============================================================
# 安装执行（两条路径共用）
# ============================================================

# 下载 compose.yaml / .env 模板（如缺失）
ensure_files() {
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
    fi
}

# 容器是否可视为"已就绪"：存在、处于运行状态且使用的镜像与本次拉取的 $IMAGE 一致。
# 任一不满足（compose 启动失败但旧容器残留、容器崩溃、镜像已更新未重建）都需 docker run 兜底重建。
container_ok() {
    docker inspect tgdl-bot >/dev/null 2>&1 || return 1
    [[ "$(docker inspect -f '{{.State.Running}}' tgdl-bot 2>/dev/null)" == "true" ]] || return 1
    local cid mid
    cid="$(docker inspect -f '{{.Image}}' tgdl-bot 2>/dev/null || true)"
    mid="$(docker inspect -f '{{.Id}}' "$IMAGE" 2>/dev/null || true)"
    [[ -n "$cid" && -n "$mid" ]] || return 1
    # docker inspect 的镜像 ID 可能是完整（sha256:…）或短 ID，取前缀比较
    [[ "$cid" == "$mid" ]] || [[ "${cid:0:19}" == "${mid:0:19}" ]]
}

start_services() {
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

    if [[ "$started" -eq 0 ]] || ! container_ok; then
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

    # 清理悬空旧镜像，避免磁盘堆积；不影响在用镜像与卷
    log "清理悬空旧镜像…"
    docker image prune -f >/dev/null 2>&1 || true
}

# 无 TTY 降级：保留原有非交互行为（模板 + 手动填写提示）
non_interactive_install() {
    warn "检测到非交互环境（/dev/tty 不可用），跳过安装向导；已生成 .env 模板，请手动填写后重启。"
    warn "Non-interactive environment detected; wizard skipped. Please edit .env manually and restart."
    ensure_files
    echo "    已生成 ${INSTALL_DIR}/.env，请填写后重启。"
    start_services
    print_tips
}

print_tips() {
    cat <<EOF

============================================================
$(wiz "安装完成。常用操作：" "Installation complete. Quick reference:")
$(wiz "  1) 修改配置：编辑 ${INSTALL_DIR}/.env 后重启" \
       "  1) Change config: edit ${INSTALL_DIR}/.env then restart")
$(wiz "       cd ${INSTALL_DIR} && docker compose up -d" \
       "       cd ${INSTALL_DIR} && docker compose up -d")
$(wiz "  2) 查看状态与日志：docker ps / docker logs -f tgdl-bot" \
       "  2) Status & logs: docker ps / docker logs -f tgdl-bot")
$(wiz "  3) 升级：cd ${INSTALL_DIR} && docker compose pull && docker compose up -d" \
       "  3) Upgrade: cd ${INSTALL_DIR} && docker compose pull && docker compose up -d")
$(wiz "  4) 私聊向 bot 发送 /update 自动更新 yt-dlp/ffmpeg（存于 tgdl-bin 卷）" \
       "  4) Send /update to the bot to update yt-dlp/ffmpeg (stored in tgdl-bin volume)")
$(wiz "  5) 中文文档：https://github.com/linkcccp/tgdl-bot" \
       "  5) Docs: https://github.com/linkcccp/tgdl-bot")
============================================================
EOF
    if (( ${#SKIPPED[@]} > 0 )); then
        warn "以下必填项未填写/未通过校验，请手动编辑 ${INSTALL_DIR}/.env 后执行 cd ${INSTALL_DIR} && docker compose up -d 重启："
        warn "The following required items were skipped; edit ${INSTALL_DIR}/.env then run cd ${INSTALL_DIR} && docker compose up -d:"
        for k in "${SKIPPED[@]}"; do warn "  - ${k}"; done
    fi
}

# ============================================================
# 主流程
# ============================================================

main() {
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

    # ---------- 3. 配置目录与基础文件 ----------
    mkdir -p "$INSTALL_DIR"
    cd "$INSTALL_DIR"
    ensure_files

    # ---------- 4. TTY 检测：无 TTY 降级为非交互 ----------
    # 注意：不能只测 [[ -r /dev/tty ]]（root 下对设备节点恒真），
    # 必须真正 open 一次（curl|sudo bash 且无终端时 open 失败）
    if ! { exec 3<> /dev/tty; } 2>/dev/null; then
        non_interactive_install
        exit 0
    fi

    # ---------- 5. 幂等：必填项齐全且 .env 非纯模板则跳过向导直接启动 ----------
    if env_has_all_required && ! env_is_pristine_template; then
        log "已检测到完整配置（.env 必填项齐全），跳过向导直接启动…"
        start_services
        print_tips
        exit 0
    fi

    # ---------- 6. 交互式向导 ----------
    choose_language
    wizard

    # ---------- 7. 写 .env 并启动 ----------
    write_env
    start_services
    print_tips
}

main "$@"