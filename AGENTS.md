# AGENTS.md

.NET 10 Telegram 下载 Bot（`tgdl-bot`）。README 覆盖用户视角；本文件是给 AI 代理的仓库特定注意事项，按需更新（非事无巨细）。

## 命令
- 构建：`dotnet build -c Release` — **0 警告是硬门槛**（`GenerateDocumentationFile` 开启，所有 public 成员须有 XML 注释）。
- 测试：`dotnet test`。`*IntegrationTests.cs`（真实网络/yt-dlp）不可用时**静默跳过**，别误判为挂。
- 文档：`./build-docs.sh`（需 `dotnet tool install --global docfx`），输出 `docs/` 与 `docfx/api/`，须 0 警告。本机 docfx 需要 aspnetcore 运行时：`DOTNET_ROOT=$HOME/dotnet`。
- 发布：`dotnet publish src/TGBot/TGBot.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -o <dir>`

## 架构
- 单一控制台应用 `src/TGBot`（AssemblyName `tgdl-bot`，net10.0，自包含单文件；`ServerGarbageCollection=false` + `TieredCompilation=false` + `InvariantGlobalization=true`）。
- 入口：`Program.cs` → `Application/AppHost` → `BotService`(长轮询) → `MessageRouter` → `DownloadCoordinator`/`CommandHandler`/`CookieService`。
- 模块命名空间：`Config/Logging/Security/Access/Download/Update/Messaging/Application/Cookie/Texts`。
- Telegram.Bot 22.x：方法为**无 `Async` 后缀的扩展方法**（`SendMessage`/`GetFile`/`DownloadFile`），轮询用 `ReceiveAsync`。
- yt-dlp 经 `Process.ArgumentList` 调用（无 shell，杜绝注入）。错误分类快速失败：`AuthRequired`/`FormatUnavailable`；格式不足自动 `-J` probe 挑最高视频+音频（mkv 兜底）。
- 下载模式：`audio`=bestaudio→**flac+mp3@320k 双传**；`video`=合并。DM 内联键盘选择，非交互按 `TgdlDefaultMode`。

## Docker / 发布（关键）
- CI（`.github/workflows/release.yml`）由 `v*` tag 触发：仅构建 **linux/amd64** → 推 GHCR `ghcr.io/linkcccp/tgdl-bot:{ver}`+`latest` → 建 Release（**无二进制资产**）。需仓库 Settings→Actions→General→Workflow permissions = **Read and write**。
- 镜像内置：tgdl-bot、telegram-bot-api（来自 fork `linkcccp/telegram-bot-api` 的 latest Release，**不本地编译**）、yt-dlp、ffmpeg 静态版、python3、**deno**（yt-dlp YouTube 提取必需；缺则报 "Requested format is not available"）。
- `docker/Dockerfile` 用 `ARG TARGETARCH`；dist 布局 `docker/dist/amd64/`（构建上下文 = `docker/`，CI 负责放产物）。
- 部署：`scripts/install.sh`（`curl|sudo bash`）→ 装/用 Docker → pull → 启动 → `docker image prune -f`（清悬空镜像）。
- **cookie 持久化**：默认 `/opt/tgdl-bot/api-data/cookies`（`tgdl-data` 卷内，v2.3.1 起，pull 重建不丢）。**勿改回 `/opt/tgdl-bot/cookies`**（早期安装非卷，会丢）。
- `docker/docker-entrypoint.sh` 由 `TGDL_*` 环境变量生成 `config.conf`（若挂载文件则跳过）。

## 配置新增的完整链路
新增配置键必须同步：`AppConfig` → `ConfigParser`（别名+解析+校验）→ `docker/docker-entrypoint.sh`（TGDL_* 映射）→ `docker/.env.example` → `docker/config.conf.example` → README 配置表。

## Git 约定
- **GitHub 只保留 `main`**；`dev` 仅本地集成分支。`feat/*`/`fix/*` → `--squash` 合并到 main；发版打 `v*` tag 触发 CI（无 tag 不发布镜像）。
- 本机外网阻断 GitHub 22 端口：origin 已用 `ssh://git@ssh.github.com:443/...`。
- 子模块 `third_party/telegram-bot-api` → fork URL（源码参考；CI 不检出/构建）。

## 测试注意
- 共享 fakes 在 `tests/TGBot.Tests/MessageRouterTests.cs`（`FakeTelegramClient`/`FakeDownloader`，含 `ProbeFormatsHandler`/`AudioBundleHandler`）。
- 测试项目同样须 0 警告（xunit 分析器开启；如 `Assert.DoesNotContain`、异步测试禁阻塞）。
