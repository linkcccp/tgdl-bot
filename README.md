# tgdl-bot

[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](LICENSE)

[English](README.en.md)

**tgdl-bot** 是一款基于 **.NET 10** 的 Telegram 下载 bot，以 **Docker 沙盒**形式分发：
单个镜像打包全部依赖（tgdl-bot + 本地 Telegram Bot API Server + yt-dlp + ffmpeg），
**零宿主依赖**、完全**自托管**，任何能运行 Docker 的 Linux 发行版均可一键部署。

用户将视频/音乐链接发给 bot（私聊或白名单频道/群组），bot 调用 **yt-dlp** 下载，
通过 **本地 Bot API Server（--local 模式）**推送到目标频道/群组，支持最大 **2GB** 上传。

bot 界面支持**中英双语**：默认跟随用户的 Telegram 语言设置，也可通过 `/language` 命令随时切换。

> **法律与合规声明**：本项目是下载工具，仅限**个人合法用途**。各国版权法差异巨大，
> 使用者须自行确认其司法辖区内的合法性，并**遵守目标站点服务条款（ToS）**与 robots 要求；
> 侵权风险自负。本项目不存储、检索或分发任何媒体内容。
> 详见文末[法律与合规声明](#法律与合规声明)。

## 功能特性

- 私聊与频道/群组双场景触发下载，结果推送至配置的目标频道/群组
- 双重白名单访问控制：私聊仅限白名单用户，频道/群组仅限白名单会话
- 基于本地 Bot API Server（`--local`）绕过 50MB 限制，支持 ~2GB 大文件上传
- **Docker 沙盒**：镜像内集成 telegram-bot-api / yt-dlp / ffmpeg，零宿主依赖，兼容所有 Linux 发行版
- 低内存占用：bot 进程常驻 RSS ≈ 40MB（目标 < 100MB）
- `/update` 自更新 yt-dlp 与 ffmpeg（写入 `tgdl-bin` 卷，跨容器重建保留）
- 并发下载队列、失败重试、进度通知、磁盘空间与超大文件检测
- 零信任安全：SSRF 防护、URL/命令/路径全面校验与净化、临时目录 0700、日志脱敏、容器内非 root 运行
- 12-factor 配置：以环境变量为主，亦支持直接挂载 config.conf
- **交互式安装向导**：一条命令完成部署，引导选择语言并逐项输入、校验必填项（无终端环境时自动降级为模板模式）
- **中英双语界面**：默认跟随用户 Telegram 语言设置，可用 `/language` 命令切换
- **bot 内配置管理**：`/config` 查看与修改配置（重启生效），`/access` 管理白名单成员

## 架构概览

```
src/TGBot/
├── Config/       config.conf 解析与校验（中英双语错误提示）
├── Logging/      分级日志（控制台 + 可选文件）
├── Security/     URL/SSRF 校验、路径净化、临时目录管理、磁盘检查
├── Access/       双重白名单访问控制
├── Download/     IDownloader 抽象、YtDlpDownloader（进程调用）、并发闸门、任务注册表、输出解析
├── Update/       IUpdater 抽象、版本对比、原子替换、远程版本源（yt-dlp GitHub / ffmpeg johnvansickle）
├── Messaging/    ITelegramClient 抽象（Telegram.Bot 实现）、上传服务、文案构建
├── Application/  消息路由、下载协调、指令处理、bot 长轮询宿主、入口
└── Texts/        面向用户的双语文案（i18n：en/zh）

tests/TGBot.Tests/   xUnit 单元测试 + 真实 yt-dlp 集成测试
docker/              Dockerfile、docker-entrypoint.sh、compose.yaml、.env.example
scripts/install.sh   一条命令 Docker 部署
third_party/         telegram-bot-api 子项目（submodule，源码参考；CI 不检出/构建）
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

脚本自动：检测/安装 Docker → **交互式安装向导**（语言选择 + 必填项逐项输入与校验，
无终端环境时自动降级为模板模式）→ 生成 `/opt/tgdl-bot/.env` → 拉取
`ghcr.io/linkcccp/tgdl-bot:latest` 并启动。

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
| `TGDL_LANGUAGE` | 否 | bot 全局默认语言：`auto`（跟随用户 language_code，默认）/ `en` / `zh`；交互式安装向导自动写入 |
| `TGDL_STATE_DIR` | 否 | 运行时状态目录，默认 `/opt/tgdl-bot/api-data`（tgdl-data 卷内持久） |

完整示例见 [`docker/.env.example`](docker/.env.example)。若需完全用文件管理，可挂载
`config.conf` 并设置 `TGDL_CONFIG_FILE=/opt/tgdl-bot/config.conf`，此时无需填以上必填项
（格式参考 [`docker/config.conf.example`](docker/config.conf.example)）。

### 状态文件（StateDir，卷内持久）

以下运行期状态存于 `StateDir`（容器内默认 `/opt/tgdl-bot/api-data`，即 `tgdl-data` 卷内，
**跨镜像重建/升级不丢失**，pull 重建后仍保留）：

| 文件 | 内容 |
| --- | --- |
| `languages.json` | 各用户显式选择的语言（`/language`） |
| `config-overlay.json` | `/config` 修改的配置覆盖（重启后生效） |
| `access-overlay.json` | `/access` 追加的白名单成员 |
| `pending-notify.json` | 配置变更后的待发送重启通知（发送成功即删除） |

### 如何获取 user ID 与 channel ID

- **user ID / chat ID / channel ID**：私聊 [@userinfobot](https://t.me/userinfobot)，**点击它下方提供的按钮选项**即可获得自己的 ID、群组/频道 ID 等（最方便的方式）。
- 也可：把 bot 加为频道/群组管理员 → 发一条消息 →
  `curl https://api.telegram.org/bot<TOKEN>/getUpdates` 查看 `chat.id`（频道形如 `-100...`）。

## 常用运维

```bash
docker ps                                   # 状态
docker logs -f tgdl-bot                     # 日志
cd /opt/tgdl-bot && docker compose up -d    # 改 .env 后重启
```

私聊 bot 发 `/update` 自动更新 yt-dlp/ffmpeg，`/status` 查看版本与内存，`/language` 切换界面语言；
`/config`、`/access` 可在 bot 内管理配置与白名单。

### 更新镜像（升级到新版本）

```bash
cd /opt/tgdl-bot && sudo docker compose pull && sudo docker compose up -d && sudo docker image prune -f
```

- `pull` 拉取最新镜像 → `up -d` 重建容器 → `image prune -f` 清理旧镜像（防磁盘堆积）
- 只会更新镜像与容器；**`.env`、`tgdl-data`/`tgdl-tmp`/`tgdl-bin` 卷、cookies、下载缓存均保留**
- 或直接重跑一键安装脚本（自动完成拉取/启动/清理）：
  ```bash
  curl -fsSL https://raw.githubusercontent.com/linkcccp/tgdl-bot/main/scripts/install.sh | sudo bash
  ```

## 解决站点机器人检测（bot 内上传 cookies）

某些站点（如 YouTube）会要求登录确认，bot 会**快速失败**并提示需要 cookies（不再空转重试）。
可通过 bot 私聊直接上传该站点的 cookies：

```text
/cookie youtube         → bot 提示「请发送 cookies 文件」
（发送 cookies.txt 文件）→ bot 保存并提示「已保存」
/cookies                → 查看各站点状态
/cookie youtube clear   → 删除该站点 cookies
```

- **按域名自动匹配**：上传的 cookie 归到对应站点；下载时按 URL 域名自动挑选该站点 cookie 传给 yt-dlp
- 预置站点：YouTube、X（推特）、Instagram、TikTok、Twitch、Facebook、哔哩哔哩、抖音、小红书、微博、SoundCloud、Vimeo、Dailymotion、Reddit
- 每站一个文件，存储于 `/opt/tgdl-bot/api-data/cookies`（`tgdl-data` 卷内，跨镜像重建持久；文件 0600）
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
返回的格式列表不一致），bot 会自动在后台兜底：列出可用格式 → 挑**最高画质视频 + 最高音质音频**
→ 用 `-f <视频ID>+<音频ID>` 重新下载并 **ffmpeg 合并**，无需人工干预。
YouTube 默认启用多 player_client（`TGDL_YTDLP_PLAYER_CLIENTS=android,ios,web_embedded,tv`，留空可禁用）。

> **JS 运行时**：yt-dlp 2026.07.04+ 的 YouTube 完整格式提取需要 JavaScript 运行时（deno），
> 缺失时格式列表不完整，会报 "Requested format is not available"。**Docker 镜像已内置 deno**
> （仅存在于容器沙盒，不污染宿主机）；本机直接跑 yt-dlp 时需自行安装 deno 并加入 PATH。

## 下载模式（视频 / 音频）

发送链接后，bot 先探测内容并自动选择下载方式：

- **仅音频**（如歌曲链接）：直接下载该站**最高音质音频**，并输出两份上传到目标频道：
  - `.flac`（无损容器，最高质副本）
  - `.mp3`（320k，Telegram 在线流式播放）
- **含视频**：私聊（白名单用户）内弹出按钮选择：
  - 🎬 **视频+音频**：合并下载（mp4/mkv，含自动格式兜底）
  - 🎵 **仅音频**：同上输出 flac+mp3
  - 2 分钟内未选择，或由频道/群组触发时，按 `TGDL_DEFAULT_MODE`（默认 `video`，可改为 `audio`）

## 本地开发与测试

```bash
dotnet restore && dotnet test
dotnet publish src/TGBot/TGBot.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -o publish/linux-x64
./publish/linux-x64/tgdl-bot --config ./config.conf --smoke-test 8    # 本地自检（不连接网络）
```

### 构建 Docker 镜像

```bash
# 先按 CI 方式准备 docker/dist/ 产物（x64 与 arm64 两套，自命名目录）：
#   dist/x64/tgdl-bot            （dotnet publish -r linux-x64）
#   dist/x64/telegram-bot-api     （fork linkcccp/telegram-bot-api 预编译 Release，官方资产名 telegram-bot-api-linux-x64）
#   dist/x64/ffmpeg               （johnvansickle 静态版，官方 URL 为 amd64 命名，即 x64）
#   dist/x64/yt-dlp               （官方 latest 静态版，官方资产名 yt-dlp，即 x64）
#   dist/arm64/...                （arm64 同构：RID linux-arm64、tba 资产 -arm64、
#                                   ffmpeg arm64-static、yt-dlp_linux_aarch64）
# 本地构建（x64 示例；TARGETARCH 为 Docker 标准注入值，DISTARCH 与 dist 目录名对应）：
docker build --build-arg TARGETARCH=amd64 --build-arg DISTARCH=x64 -f docker/Dockerfile -t ghcr.io/linkcccp/tgdl-bot:dev docker/
```

## 发布（GitHub Actions 自动构建并推送镜像）

推送 `v*` tag 触发 [`.github/workflows/release.yml`](.github/workflows/release.yml)：
matrix 双 job 构建 **x64**（ubuntu-latest）与 **arm64**（ubuntu-24.04-arm 原生 runner）——
发布 tgdl-bot（linux-x64 / linux-arm64）→ 从 **fork `linkcccp/telegram-bot-api` 的最新 Release 下载
预编译 telegram-bot-api**（资产按架构区分）→ 下载对应架构的 yt-dlp/ffmpeg 静态版（原生执行校验）→
构建 **linux/amd64（即 x64）+ linux/arm64** 镜像并推 per-arch tag（`{ver}-x64` / `{ver}-arm64`）→
manifest job 合并出 **multi-arch** `ghcr.io/linkcccp/tgdl-bot:{tag}` 与 `:latest` → 创建 Release。

```bash
git tag v2.0.0 && git push origin v2.0.0
```

> **重要**：在仓库 **Settings → Actions → General → Workflow permissions** 需选择
> **Read and write permissions**，否则 Release/GHCR 推送会失败（workflow 已显式声明
> `permissions: contents: write, packages: write`）。

## API 文档

```bash
dotnet tool install --global docfx
dotnet run --project tools/TgdlDocBuilder   # 输出到 docs/，打开 docs/index.html；--help 查看参数
```

> macOS/Linux 使用自定义 dotnet 安装（如 `$HOME/dotnet`）时，先执行 `export DOTNET_ROOT=$HOME/dotnet`
> （docfx 需要 aspnetcore 运行时）。

## 已知限制与未验证项

- **架构**：支持 linux/amd64（即 x64）与 linux/arm64（multi-arch manifest，`docker pull` 自动按宿主
  架构拉取）；不支持 armv7/32 位。arm64 构建侧已验证（原生 arm64 runner 构建 + 资产原生执行校验），
  **真实运行待 ARM 设备实测**
- 超过上传上限（约 2GB）的文件会先完整下载再被拒绝（直链大小不可预知）
- 紧贴中文且无空格分隔的 URL 无法可靠识别边界，请以空格分隔
- telegram-bot-api 预编译二进制依赖 fork（`linkcccp/telegram-bot-api`）的 Release；
  fork 的 `auto-sync` 工作流会定时同步上游并自动构建发布
- **未验证项**：真实的 Telegram Bot API 交互（需真实 Token）无法在本开发机验证；容器全链路
  （tba 就绪 / bot 连接 / 内存）已在本机 Docker 实测通过
- 仓库托管于 https://github.com/linkcccp/tgdl-bot（公开）。`main` 是唯一远程分支（发布分支）；
  开发分支（`dev`、`feat/*` 等）仅在本地维护；开发流程见 [CONTRIBUTING.md](CONTRIBUTING.md)

## 法律与合规声明

> 本项目仅提供下载工具，**不存储、检索或分发任何媒体内容**，不提供任何内容搜索服务。

- **仅限个人合法用途**：各国/地区版权法差异巨大，使用者须自行确认其司法辖区内的合法性；
  不得用于商业分发、侵权复制等非法目的。
- **遵守目标站点条款**：使用时应遵守目标站点的**服务条款（ToS）**与 robots 要求；
  绕过访问控制（如付费墙）可能违反相关法律与站点条款。
- **侵权风险自负**：下载行为引发的版权、合规风险由使用者自行承担。本项目不托管内容，
  版权方可通过仓库 Issue 或 [SECURITY.md](SECURITY.md) 渠道联系（项目无托管内容可移除，
  声明保留以示流程）。
- 参考：yt-dlp 的合法性说明 <https://github.com/yt-dlp/yt-dlp#legal>。

### Telegram Bot API 条款

- bot 开发者/部署者须遵守 [Telegram Bot API 服务条款](https://telegram.org/tos/bots) 与
  [Telegram 服务条款](https://telegram.org/tos)；本项目仅提供下载工具，bot 的部署与使用
  行为由部署者自行负责。
- **滥用风险提示**：高频下载可能触发 Telegram 风控（账号/机器人封禁）或目标站点 IP 封禁，
  部署者自行承担相关风险。
- `/cookie` 上传的 cookies 仅用于访问**用户已授权**的内容，不得用于规避付费墙或访问受限内容。
