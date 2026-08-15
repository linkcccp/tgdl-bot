# ============================================================
# tgdl-bot v2 Docker 镜像（沙盒：tgdl-bot + telegram-bot-api + yt-dlp + ffmpeg）
# 构建上下文 = docker/（CI 已把 dist/ 产物放入，见 .github/workflows/release.yml）
# ============================================================
FROM debian:12-slim

# buildx 提供的平台参数：amd64 / arm64（dist 目录按此组织）
ARG TARGETARCH

# 运行期依赖：
#   python3     -> yt-dlp 是 Python zipapp，运行时需要解释器
#   libssl3/zlib1g -> telegram-bot-api 动态链接所需
#   curl/ca-certificates -> HEALTHCHECK 与 HTTPS
#   passwd      -> useradd（构建用户）
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        python3 libssl3 zlib1g curl ca-certificates passwd \
    && rm -rf /var/lib/apt/lists/* \
    && useradd --system --no-create-home --uid 10001 tgdl-bot

# 运行目录布局：
#   /opt/tgdl-bot/tgdl-bot          主程序（自包含单文件）
#   /opt/tgdl-bot/api/telegram-bot-api  本地 Bot API Server
#   /opt/tgdl-bot/seed-bin/         镜像内置的 yt-dlp/ffmpeg（entrypoint 首启种子到 bin 卷）
#   /opt/tgdl-bot/bin/              运行期可写（/update 自更新 + 卷持久化）
#   /opt/tgdl-bot/api-data/         telegram-bot-api 数据（卷）
#   /var/lib/tgdl-bot/tmp/          下载临时目录（卷）
WORKDIR /opt/tgdl-bot

COPY docker-entrypoint.sh /usr/local/bin/docker-entrypoint.sh
COPY dist/${TARGETARCH}/tgdl-bot /opt/tgdl-bot/tgdl-bot
COPY dist/${TARGETARCH}/telegram-bot-api /opt/tgdl-bot/api/telegram-bot-api
COPY dist/yt-dlp /opt/tgdl-bot/seed-bin/yt-dlp
COPY dist/${TARGETARCH}/ffmpeg /opt/tgdl-bot/seed-bin/ffmpeg

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
    TGDL_YTDLP_PATH=/opt/tgdl-bot/bin/yt-dlp \
    TGDL_FFMPEG_PATH=/opt/tgdl-bot/bin/ffmpeg \
    TGDL_COOKIE_STORE_DIR=/opt/tgdl-bot/cookies

ENTRYPOINT ["/usr/local/bin/docker-entrypoint.sh"]
