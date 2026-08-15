# tgdl-bot

基于 **.NET 10** 的 Telegram 下载 Bot：用户把视频/音乐链接发给 Bot（私聊或白名单频道/群组），Bot 调用 **yt-dlp** 下载，并通过 **本地 Telegram Bot API Server（--local 模式）** 推送到目标频道/群组，支持最大 **2GB** 媒体上传，常驻内存 **< 100MB**。

具备 ffmpeg / yt-dlp **自更新**能力（白名单用户发 `/update` 即可，无需 SSH），零信任安全设计，全部配置集中在 `config.conf`。

## 功能特性

- 私聊 + 频道/群组双场景触发下载，推送到配置的目标频道/群组
- 双重白名单访问控制：私聊仅限白名单用户，频道/群组仅限白名单会话
- 基于 Telegram **本地 Bot API Server**（`--local`）绕过 50MB 限制，支持 ~2GB 大文件
- 内存占用低：工作站 GC + 关闭分层编译 + InvariantGlobalization，实测 RSS ≈ 40MB
- **自更新** ffmpeg 与 yt-dlp：原子替换、失败回滚、版本对比、互斥排队
- 并发下载队列、失败重试、进度通知、磁盘空间与超大文件检测
- 零信任：SSRF 防护（拒绝私网地址）、URL/命令/路径全面校验净化、临时目录 0700、日志脱敏
- 全部配置在 `config.conf`，缺失或格式错误给出中文提示并退出

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
deploy/              systemd 单元、install.sh、config.conf.example
docfx/               DocFX 文档源（输出到 docs/）
```

模块通过接口解耦（`IDownloader`、`ITelegramClient`、`IUpdater`、`IProcessRunner`、`IHostResolver` 等），可替换、可单测，遵循 SOLID 原则。

## 环境依赖

### 开发环境（Arch Linux / WSL）

```bash
sudo pacman -Syu dotnet-sdk ffmpeg  # dotnet-sdk 需为 10.x
# yt-dlp（二选一）
sudo pacman -S yt-dlp                # 或官方静态版
# 本地 Bot API Server（可选，用于联调）
# 下载 https://github.com/tdlib/telegram-bot-api/releases 的 linux amd64 二进制
```

### 部署环境（Debian 12）

```bash
sudo apt-get update
sudo apt-get install -y curl xz-utils ffmpeg
# .NET 运行时已内嵌于自包含发布，无需单独安装
# yt-dlp 与 ffmpeg 由 Bot 的 /update 命令自动安装到 /opt/tgdl-bot/bin
```

## 构建、测试与发布

```bash
# 还原 + 测试
dotnet restore
dotnet test

# 自包含发布（linux-x64，单文件）
dotnet publish src/TGBot/TGBot.csproj -c Release -r linux-x64 \
    --self-contained true -p:PublishSingleFile=true -o publish/linux-x64

# 本地自检（验证配置与模块装配，不连接网络；输出 RSS 占用）
./publish/linux-x64/tgdl-bot --config ./config.conf --smoke-test 8
```

### 内存配置说明

`TGBot.csproj` 已配置 `ServerGarbageCollection=false`（工作站 GC）、`TieredCompilation=false`（关闭分层编译）、`InvariantGlobalization=true`（减少运行时开销）。实测 idle RSS ≈ 40MB，远低于 100MB 目标。

## 配置：config.conf

位于 **tgdl-bot 二进制同目录**（也可用 `--config <路径>` 或环境变量 `TGDL_CONFIG` 指定）。

| 配置项 | 必填 | 说明 |
| --- | --- | --- |
| `BotToken` | 是 | @BotFather 创建 bot 后获得 |
| `LocalApiBaseUrl` | 是 | 本地 Bot API Server 地址，如 `http://127.0.0.1:8081` |
| `TelegramApiId` / `TelegramApiHash` | 是 | 本地 Bot API Server 启动凭据（my.telegram.org） |
| `TargetChannelIds` | 是 | 目标频道/群组 ID（逗号分隔，负数），结果推送对象 + 群组白名单 |
| `AllowedUserIds` | 是 | 私聊白名单用户 ID（逗号分隔） |
| `DownloadTempDir` | 是 | 下载临时目录 |
| `YtDlpPath` / `FfmpegPath` | 否 | yt-dlp/ffmpeg 路径，自更新目标（需可写） |
| `MaxConcurrentDownloads` | 否 | 并发下载数，默认 2 |
| `LogLevel` / `LogFile` | 否 | 日志级别 / 可选日志文件 |
| `DownloadRetries` / `UploadRetries` | 否 | 重试次数 |
| `DownloadTimeoutSeconds` | 否 | 单任务超时 |
| `ExtractAudio` | 否 | 提取为 mp3 音频 |
| `AlsoSendMediaToRequester` | 否 | 私聊请求者是否也收媒体 |
| `AllowPrivateUrls` | 否 | 允许私网 URL（默认否，SSRF 防护） |
| `AllowPlaylists` | 否 | 允许播放列表 |
| `MergeFormat` | 否 | 合并容器，默认 mp4 |
| `UpdateYtDlp` / `UpdateFfmpeg` | 否 | 是否参与 /update |

完整示例见 [`deploy/config.conf.example`](deploy/config.conf.example)。

### 如何获取 user ID 与 channel ID

1. **自己的 user ID**：私聊 [@userinfobot](https://t.me/userinfobot)，它直接回复你的数字 ID；或运行 `curl https://api.telegram.org/bot<TOKEN>/getUpdates` 后查看 `message.from.id`。
2. **channel ID**：把 Bot 加为频道管理员 → 频道里发一条消息 → `getUpdates` 返回的 `message.chat.id`（形如 `-1001234567890`）。
3. **群组 ID**：同样方式查看 `chat.id`（负数）。

## 部署到 Debian 12（分步）

### 0) 一条命令安装（推荐）

从 GitHub Release 自动下载并安装最新版本（含 systemd 服务、config 模板）：

```bash
curl -fsSL https://raw.githubusercontent.com/linkcccp/tgdl-bot/main/scripts/install.sh | sudo bash
```

脚本详情见 [`scripts/README.md`](scripts/README.md)。

### 1) 发布并上传

```bash
# 本地构建自包含发布
dotnet publish src/TGBot/TGBot.csproj -c Release -r linux-x64 \
    --self-contained true -p:PublishSingleFile=true -o publish/linux-x64

# 上传二进制与部署文件到 VPS
scp publish/linux-x64/tgdl-bot root@<VPS_IP>:/tmp/
scp -r deploy root@<VPS_IP>:/tmp/
```

### 2) 运行安装脚本

```bash
ssh root@<VPS_IP>
cd /tmp
sudo bash deploy/install.sh /tmp/tgdl-bot
```

脚本会：安装依赖 → 创建 `tgdl-bot` 系统用户与目录 → 放置二进制与配置 → 安装并启用两个 systemd 服务。

### 3) 填写配置

```bash
sudo nano /opt/tgdl-bot/config.conf    # Token、TargetChannelIds、AllowedUserIds 等
sudo nano /opt/tgdl-bot/api.env        # API_ID、API_HASH
sudo systemctl restart telegram-bot-api tgdl-bot
sudo systemctl status tgdl-bot
journalctl -u tgdl-bot -f
```

### 4) 本地 Bot API Server

**无需手动安装**：`telegram-bot-api` 以**子项目（`third_party/telegram-bot-api` submodule）**
形式维护，由 GitHub Actions 在 Release 时于 CI runner 上编译好并打进
`tgdl-bot-*-linux-x64.tar.gz`；一条命令安装脚本会直接从压缩包安装二进制（VPS 不本地构建）。

若压缩包内没有该二进制（旧 Release），可手动构建后放置：

```bash
cd /opt/tgdl-bot/api
# 参考官方构建说明：https://tdlib.github.io/telegram-bot-api/build.html
# 将构建得到的 telegram-bot-api 放到 /opt/tgdl-bot/api/ 并：
sudo chmod +x /opt/tgdl-bot/api/telegram-bot-api
sudo systemctl restart telegram-bot-api
```

服务单元已配置 `--local --dir=/var/lib/tgdl-bot-api --http-port=8081`。

### 5) 首次自更新

私聊中向 Bot 发送 `/update`（仅白名单用户），自动下载并原子替换最新 **yt-dlp** 与 **ffmpeg**（官方静态版，安装到 `/opt/tgdl-bot/bin`，无需 root）。

## 发布（GitHub Actions 自动构建 Release）

打 `v*` tag 即触发 [`.github/workflows/release.yml`](.github/workflows/release.yml)：
自动编译 **telegram-bot-api 子项目**（`third_party/telegram-bot-api` submodule）与 tgdl-bot
单文件二进制 → 打包 `tgdl-bot-<tag>-linux-x64.tar.gz`（含两个二进制、`deploy/` 与 README）→
创建 GitHub Release 并上传产物。

```bash
git tag v1.0.0
git push origin v1.0.0     # 触发 CI 构建并创建 Release
```

> **重要**：在仓库 **Settings → Actions → General → Workflow permissions** 中需选择
> **Read and write permissions**，否则 `softprops/action-gh-release` 创建 Release 会因
> 权限不足而失败（workflow 内已显式声明 `permissions: contents: write`，此为双保险）。

## systemd 单元

`deploy/tgdl-bot.service`（Bot，`MemoryMax=220M` 软限制）与 `deploy/telegram-bot-api.service`（本地 API Server）均已含内存/权限加固（`NoNewPrivileges`、`ProtectSystem` 等）。

## 自更新说明

`/update` 命令（仅私聊白名单用户）：
- **yt-dlp**：官方 GitHub `latest/download` 静态版
- **ffmpeg**：johnvansickle.com 官方静态构建（理由：无需 root 即可安装到非 root 目录、支持原子替换与回滚、版本领先 Debian apt 仓库；apt 需要 root 且 Debian 12 自带版本较旧）
- 流程：版本对比（无新版本则提示）→ 下载到目标同目录临时文件 → 校验可运行 → 备份旧版 → 原子替换 → 校验 → 失败自动回滚
- 与下载任务互斥（更新期间暂停新下载、等待进行中的下载完成）

## API 文档

```bash
dotnet tool install --global docfx
./build-docs.sh
# 输出到 docs/，浏览器打开 docs/index.html
```

源码所有公开类型与成员均含 XML 文档注释（`csproj` 已开启 `GenerateDocumentationFile=true`），DocFX 生成零警告。

## 已知限制与未验证项

- 上传格式依赖 yt-dlp 输出：视频默认合并为 mp4（`MergeFormat` 可改），个别冷门容器会以文档形式发送
- 与无空格中文粘连的 URL（如 `看这个https://x.com/v，赞`）无法可靠切分，需以空格分隔
- 超过上传上限（约 2GB）的文件会先完整下载再被拒绝（yt-dlp 下载前无法预知部分直链大小），建议用 `MaxMediaSizeBytes` 配合磁盘空间检查控制
- **未验证项**：真实的 Telegram Bot API 交互（本地 Bot API Server + 真实 Token）无法在本开发机验证；下载器与更新器已通过真实网络集成测试（raw.githubusercontent / GitHub / johnvansickle），部署到 VPS 后按 README 步骤即可启用
- GitHub Actions 工作流无法在本机实跑，已逐行自查并在 `.github/workflows/release.yml` 注释与下文标注风险点；首条 `v*` tag 推送后请确认 Release 创建成功
- 仓库托管于 https://github.com/linkcccp/tgdl-bot（公开）
