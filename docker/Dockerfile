# ============================================================
# tgdl-bot v2 Docker 镜像（沙盒：tgdl-bot + telegram-bot-api + yt-dlp + ffmpeg）
# 构建上下文 = docker/（CI 已把 dist/{x64,arm64}/ 产物放入，见 .github/workflows/release.yml）
#
# 架构参数职责分离：
#   TARGETARCH：Docker/buildx 预定义 ARG，构建时自动注入标准值 amd64/arm64，
#               仅用于 deno 分支判断（上游官方资产命名 x86_64 / aarch64，即 x64 / arm64）。
#   DISTARCH  ：自命名架构（x64/arm64，对应 dist 目录名），buildx 不自动注入，
#               必须由 CI 显式传 --build-arg DISTARCH=<distarch>；仅用于 COPY 路径。
# ============================================================
FROM debian:12-slim

# Docker 标准注入值：amd64 / arm64（仅 deno 分支判断用；amd64 即 x64）
ARG TARGETARCH
# 自命名架构（dist 目录名）：x64 / arm64，CI 经 --build-arg 显式传入
ARG DISTARCH=x64

# 运行期依赖：
#   python3     -> yt-dlp 是 Python zipapp，运行时需要解释器
#   libssl3/zlib1g -> telegram-bot-api 动态链接所需
#   curl/ca-certificates -> HEALTHCHECK 与 HTTPS
#   passwd      -> useradd（构建用户）
#   xz-utils    -> /update 解压 BtbN ffmpeg 的 .tar.xz（GNU tar 解 xz 需调用外部 xz 命令，
#                  缺失会报 "tar (child): xz: Cannot exec"）；tar/gzip 为 Debian required 包，slim 自带
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        python3 libssl3 zlib1g curl ca-certificates passwd unzip xz-utils \
    && rm -rf /var/lib/apt/lists/* \
    && useradd --system --no-create-home --uid 10001 tgdl-bot

# JavaScript 运行时（deno）：yt-dlp 2026.07.04+ 的 YouTube 提取依赖它，
# 否则格式列表不完整会报 "Requested format is not available"。
# 仅装入容器沙盒内，不污染宿主机。deno 为自包含单文件，按 TARGETARCH 选取。
RUN ARCH="$([ "$TARGETARCH" = "arm64" ] && echo "aarch64-unknown-linux-gnu" || echo "x86_64-unknown-linux-gnu")" \
    && curl -fsSL -o /tmp/deno.zip \
        "https://github.com/denoland/deno/releases/latest/download/deno-${ARCH}.zip" \
    && unzip -o /tmp/deno.zip -d /usr/local/bin \
    && rm -f /tmp/deno.zip \
    && chmod +x /usr/local/bin/deno \
    && deno --version

# 架构交叉校验（防御性断言）：TARGETARCH 与 DISTARCH 必须匹配。
# CI 经 release.yml 显式传 --build-arg DISTARCH 必无误；此检查针对本地手动构建——
# 若忘了传 DISTARCH（默认 x64）而 TARGETARCH=arm64，会把 x64 二进制 COPY 进 arm64 镜像
# （构建成功但产出坏镜像），这里直接让构建失败。
RUN if [ "$DISTARCH" = "x64" ] && [ "$TARGETARCH" = "amd64" ]; then \
        exit 0; \
    elif [ "$DISTARCH" = "arm64" ] && [ "$TARGETARCH" = "arm64" ]; then \
        exit 0; \
    else \
        echo "DISTARCH/TARGETARCH 不匹配: $DISTARCH/$TARGETARCH" >&2; \
        exit 1; \
    fi

# 运行目录布局：
#   /opt/tgdl-bot/tgdl-bot          主程序（自包含单文件）
#   /opt/tgdl-bot/api/telegram-bot-api  本地 Bot API Server
#   /opt/tgdl-bot/seed-bin/         镜像内置的 yt-dlp/ffmpeg（entrypoint 首启种子到 bin 卷）
#   /opt/tgdl-bot/bin/              运行期可写（/update 自更新 + 卷持久化）
#   /opt/tgdl-bot/api-data/         telegram-bot-api 数据（卷）
#   /var/lib/tgdl-bot/tmp/          下载临时目录（卷）
WORKDIR /opt/tgdl-bot

COPY docker-entrypoint.sh /usr/local/bin/docker-entrypoint.sh
COPY dist/${DISTARCH}/tgdl-bot /opt/tgdl-bot/tgdl-bot
COPY dist/${DISTARCH}/telegram-bot-api /opt/tgdl-bot/api/telegram-bot-api
COPY dist/${DISTARCH}/yt-dlp /opt/tgdl-bot/seed-bin/yt-dlp
COPY dist/${DISTARCH}/ffmpeg /opt/tgdl-bot/seed-bin/ffmpeg

RUN chmod +x /usr/local/bin/docker-entrypoint.sh \
        /opt/tgdl-bot/tgdl-bot \
        /opt/tgdl-bot/api/telegram-bot-api \
        /opt/tgdl-bot/seed-bin/yt-dlp \
        /opt/tgdl-bot/seed-bin/ffmpeg \
    && mkdir -p /opt/tgdl-bot/bin /opt/tgdl-bot/api-data /var/lib/tgdl-bot/tmp \
    && chown -R tgdl-bot:tgdl-bot /opt/tgdl-bot /var/lib/tgdl-bot

# telegram-bot-api 就绪即代表服务可用（本地 Bot API 端口 8081）
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD ["bash", "-c", "code=$(curl -s -o /dev/null -w '%{http_code}' http://127.0.0.1:8081/ || true); [ \"$code\" != \"000\" ]"]

ENV TGDL_CONFIG=/opt/tgdl-bot/config.conf \
    TGDL_LOCAL_API_URL=http://127.0.0.1:8081 \
    TGDL_DOWNLOAD_TMP=/var/lib/tgdl-bot/tmp \
    TGDL_COOKIE_STORE_DIR=/opt/tgdl-bot/api-data/cookies

ENTRYPOINT ["/usr/local/bin/docker-entrypoint.sh"]
