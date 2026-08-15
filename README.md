# tgdl-bot

基于 **.NET 10** 的 Telegram 下载 Bot，以 **Docker 沙盒**发布：一个镜像打包全部依赖
（tgdl-bot + 本地 Telegram Bot API Server + yt-dlp + ffmpeg），不依赖宿主机任何程序，
任何能运行 Docker 的 Linux 发行版均可一键部署。

用户把视频/音乐链接发给 Bot（私聊或白名单频道/群组），Bot 调用 **yt-dlp** 下载，通过
**本地 Bot API Server（--local 模式）**推送到目标频道/群组，支持最大 **2GB** 上传。

## 功能特性

- 私聊 + 频道/群组双场景触发下载，推送到配置的目标频道/群组
- 双重白名单访问控制：私聊仅限白名单用户，频道/群组仅限白名单会话
- 基于本地 Bot API Server（`--local`）绕过 50MB 限制，支持 ~2GB 大文件
- **Docker 沙盒**：镜像内自带 telegram-bot-api / yt-dlp / ffmpeg，无宿主依赖、全发行版通用
- 内存占用低：bot 进程常驻 RSS ≈ 40MB（< 100MB 目标）
- `/update` 自更新 yt-dlp 与 ffmpeg（写 `tgdl-bin` 卷，跨容器重建保留）
- 并发下载队列、失败重试、进度通知、磁盘空间与超大文件检测
- 零信任：SSRF 防护、URL/命令/路径全面校验净化、临时目录 0700、日志脱敏、容器内非 root 运行
- 配置 12-factor：环境变量为主，也支持直接挂载 config.conf

## 架构概览

```
src/TGBot/
├── Config/       config.conf 解析与校验（中文错误提示）
├── Logging/      分级日志（控制台 + 可选文件）
├── Security/     URL/SSRF 校验、路径净化、临时目录管理、磁盘检查
├── Access/       双重白名单访问控制
├── Download/     IDownloader 抽象、YtDlpDownloader（进程调用）、并发闸门、任务注册表、输出解析
├── Update/       IUpdater 抽象、版本对比、原子替换、远程版本源（yt-dlp GitHub / ffmpeg johnvansickle）
├── Messaging/    ITelegramClient 抽象（Telegram.Bot 实现）、上传服务、文案构建
├── Application/  消息路由、下载协调、指令处理、Bot 长轮询宿主、入口
└── Texts/        面向用户的中文提示文案

tests/TGBot.Tests/   xUnit 单元测试 + 真实 yt-dlp 集成测试
docker/              Dockerfile、docker-entrypoint.sh、compose.yaml、.env.example
scripts/install.sh   一条命令 Docker 部署
third_party/         telegram-bot-api 子项目（submodule，CI 编译）
```

容器内沙盒布局：

```
/opt/tgdl-bot
├── tgdl-bot                 # 主程序（自包含单文件）
├── api/telegram-bot-api     # 本地 Bot API Server（--local :8081）
├── seed-bin/{yt-dlp,ffmpeg} # 镜像内置，首启种子到 bin 卷
├── bin/{yt-dlp,ffmpeg}      # 运行期可写（/update 自更新，tgdl-bin 卷）
├── api-data/                # telegram-bot-api 数据（tgdl-data 卷）
└── config.conf              # 由环境变量生成，或挂载文件
```

模块通过接口解耦（`IDownloader`、`ITelegramClient`、`IUpdater` 等），遵循 SOLID 原则。

## 快速开始（Docker）

### 一条命令（任意 Linux 发行版）

```bash
curl -fsSL https://raw.githubusercontent.com/linkcccp/tgdl-bot/main/scripts/install.sh | sudo bash
```

脚本自动：检测/安装 Docker → 生成 `/opt/tgdl-bot/.env` → 拉取 `ghcr.io/linkcccp/tgdl-bot:latest` 并启动。

### 手动

```bash
git clone --recurse-submodules https://github.com/linkcccp/tgdl-bot.git
cd tgdl-bot/docker
cp .env.example .env
$EDITOR .env        # 填写 TGDL_BOT_TOKEN / TGDL_TARGET_CHANNELS / TGDL_ALLOWED_USERS / TGDL_API_ID / TGDL_API_HASH
docker compose up -d
docker logs -f tgdl-bot
```

## 配置：环境变量（docker/.env）

| 变量 | 必填 | 说明 |
| --- | --- | --- |
| `TGDL_BOT_TOKEN` | 是 | @BotFather 创建 bot 后获得 |
| `TGDL_TARGET_CHANNELS` | 是 | 目标频道/群组 ID，逗号分隔（负数），结果推送对象 + 群组白名单 |
| `TGDL_ALLOWED_USERS` | 是 | 私聊白名单用户 ID，逗号分隔 |
| `TGDL_API_ID` / `TGDL_API_HASH` | 是 | 本地 Bot API Server 凭据（my.telegram.org），只给 tba 用 |
| `TGDL_LOG_LEVEL` | 否 | Trace/Debug/Info/Warn/Error |
| `TGDL_MAX_CONCURRENT` | 否 | 并发下载数，默认 2 |
| `TGDL_DOWNLOAD_RETRIES` / `TGDL_UPLOAD_RETRIES` | 否 | 重试次数 |
| `TGDL_DOWNLOAD_TIMEOUT` | 否 | 单任务超时（秒） |
| `TGDL_EXTRACT_AUDIO` | 否 | 提取为 mp3 音频 |
| `TGDL_SEND_TO_REQUESTER` | 否 | 私聊请求者是否也收媒体 |
| `TGDL_ALLOW_PRIVATE_URLS` | 否 | 允许私网 URL（默认否，SSRF 防护） |
| `TGDL_ALLOW_PLAYLISTS` | 否 | 允许播放列表 |
| `TGDL_MERGE_FORMAT` | 否 | 合并容器（`/` 分隔候选，默认 `mp4/mkv`，封装不了自动退 mkv） |
| `TGDL_MAX_MEDIA_SIZE` | 否 | 可上传最大字节数（默认接近 2GB） |
| `TGDL_UPDATE_YTDLP` / `TGDL_UPDATE_FFMPEG` | 否 | 是否参与 /update |

完整示例见 [`docker/.env.example`](docker/.env.example)。若需完全用文件管理，可挂载
`config.conf` 并设置 `TGDL_CONFIG_FILE=/opt/tgdl-bot/config.conf`，此时无需填以上必填项
（格式参考 [`docker/config.conf.example`](docker/config.conf.example)）。

### 如何获取 user ID 与 channel ID

1. **自己的 user ID**：私聊 [@userinfobot](https://t.me/userinfobot) 直接回复数字。
2. **channel/群组 ID**：把 bot 加为频道/群组管理员 → 发一条消息 →
   `curl https://api.telegram.org/bot<TOKEN>/getUpdates` 查看 `chat.id`（频道形如 `-100...`）。

## 常用运维

```bash
docker ps                                   # 状态
docker logs -f tgdl-bot                     # 日志
cd /opt/tgdl-bot && docker compose up -d    # 改 .env 后重启
cd /opt/tgdl-bot && docker compose pull && docker compose up -d   # 升级镜像
```

私聊 bot 发 `/update` 自动更新 yt-dlp/ffmpeg；`/status` 查看版本与内存。

## 解决站点机器人检测（Bot 内上传 cookies）

某些站点（如 YouTube）会要求登录确认，bot 会**快速失败**并提示需要 cookies（不再空转重试）。
可通过 bot 私聊直接上传该站点的 cookies：

```text
/cookie youtube         → bot 提示「请发送 cookies 文件」
（发送 cookies.txt 文件）→ bot 保存并提示「已保存」
/cookies                → 查看各站点状态
/cookie youtube clear   → 删除该站点 cookies
```

- **按域名自动选用**：上传的 cookie 归到对应站点；下载时按 URL 域名自动挑选该站点 cookie 传给 yt-dlp
- 预置站点：YouTube、X（推特）、Instagram、TikTok、Twitch、Facebook、哔哩哔哩、抖音、小红书、微博、SoundCloud、Vimeo、Dailymotion、Reddit
- 每站一个文件，存储于 `/opt/tgdl-bot/cookies`（`tgdl-cookies` 卷，跨重建保留；文件 0600）
- 获取 cookies.txt：浏览器登录该站点后，用扩展（如 *Get cookies.txt LOCALLY*）导出 Netscape 格式文件

### 免 cookies 的备选（可选）

数据中心 IP 常被 YouTube 拦截，除 cookies 外可尝试：

```bash
# docker/.env 中配置代理，或附加 yt-dlp 参数
TGDL_YTDLP_PROXY=http://<住宅/独享代理>:端口
TGDL_YTDLP_EXTRA_ARGS=--extractor-args youtube:player_client=android,ios
```

### 自动格式兜底

若默认格式选择失败（`Requested format is not available`，常见于 YouTube 多 player_client
返回的格式列表不一致），bot 会**自动后台**：列出可用格式 → 挑**最高画质视频 + 最高音质音频**
→ 用 `-f <视频ID>+<音频ID>` 重新下载并 **ffmpeg 合并**，无需人工干预。
YouTube 默认启用多 player_client（`TGDL_YTDLP_PLAYER_CLIENTS=default,android,ios,web_embedded`，留空可禁用）。

## 本地开发与测试

```bash
dotnet restore && dotnet test
dotnet publish src/TGBot/TGBot.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -o publish/linux-x64
./publish/linux-x64/tgdl-bot --config ./config.conf --smoke-test 8    # 本地自检（不连接网络）
```

### 构建 Docker 镜像

```bash
# 先按 CI 方式准备 docker/dist/ 产物：
#   dist/{amd64,arm64}/tgdl-bot            （dotnet publish -r linux-x64 / linux-arm64）
#   dist/{amd64,arm64}/telegram-bot-api     （来自 fork linkcccp/telegram-bot-api 的预编译 Release）
#   dist/{amd64,arm64}/ffmpeg               （johnvansickle 静态版）
#   dist/yt-dlp                             （官方 latest 静态版，跨架构通用）
# 单架构本地构建：
docker build --build-arg TARGETARCH=amd64 -f docker/Dockerfile -t ghcr.io/linkcccp/tgdl-bot:dev docker/
```

## 发布（GitHub Actions 自动构建并推送多架构镜像）

打 `v*` tag 触发 [`.github/workflows/release.yml`](.github/workflows/release.yml)：
发布 tgdl-bot（x64+arm64）→ 从 **fork `linkcccp/telegram-bot-api` 的最新 Release 下载
预编译 telegram-bot-api**（该 fork 由 `auto-sync` 工作流自动同步上游并构建 x64/arm64）→
下载 yt-dlp/ffmpeg 静态版 → 构建 **多架构**（linux/amd64 + linux/arm64）Docker 镜像并推送
GHCR（`ghcr.io/linkcccp/tgdl-bot:{tag}` 与 `:latest`）→ 创建 Release。

```bash
git tag v2.0.0 && git push origin v2.0.0
```

> **重要**：在仓库 **Settings → Actions → General → Workflow permissions** 需选择
> **Read and write permissions**，否则 Release/GHCR 推送会失败（workflow 已显式声明
> `permissions: contents: write, packages: write`）。

## API 文档

```bash
dotnet tool install --global docfx
./build-docs.sh   # 输出到 docs/，打开 docs/index.html
```

## 已知限制与未验证项

- **架构**：已发布 linux/amd64 + linux/arm64 多架构镜像；arm64 由 CI（QEMU 模拟）验证，本机未实跑
- 超过上传上限（约 2GB）的文件会先完整下载再被拒绝（直链大小不可预知）
- 与无空格中文粘连的 URL 无法可靠切分，需以空格分隔
- telegram-bot-api 预编译二进制依赖 fork（`linkcccp/telegram-bot-api`）的 Release；
  fork 的 `auto-sync` 工作流会定时同步上游并自动构建发布
- **未验证项**：真实的 Telegram Bot API 交互（需真实 Token）无法在本开发机验证；容器全链路
  （tba 就绪 / bot 连接 / 内存）已在本机 Docker 实测通过
- 仓库托管于 https://github.com/linkcccp/tgdl-bot（公开，GitHub 仅保留 main 分支 + tag）
