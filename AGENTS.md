# AGENTS.md

.NET 10 Telegram 下载 Bot（`tgdl-bot`）。README 覆盖用户视角；本文件是给 AI 代理的仓库特定注意事项，按需更新（非事无巨细）。

## 开发流程

大厂 8 环节流程，由 `orchestrator` 主控（详见 `.opencode/agent/orchestrator.md`）：需求澄清 → 设计评审 → 分层开发 → 测试验收 → 代码评审 → 文档 → 部署。

Agent 角色（详见 `.opencode/agent/`，openai 配置 `opencode.json` 默认 `orchestrator`）：

- `orchestrator`：主控（primary），产品经理 + 项目经理，只读协调、结果判定与闭环
- `architect`：设计评审、ADR 产出
- `developer`：`TGBot` 全部功能模块实现（含 yt-dlp 集成、配置链路）
- `qa`：测试与验收
- `code-reviewer`：代码审查，输出 P0/P1/P2 问题清单
- `docs`：docfx 文档生成与一致性核对
- `devops`：docker/CI/发布/install/audit
- `scribe`：工作日志记录

## 命令
- 构建：`dotnet build -c Release` — **0 警告是硬门槛**（`GenerateDocumentationFile` 开启，所有 public 成员须有 XML 注释）。
- 测试：`dotnet test`。`*IntegrationTests.cs`（真实网络/yt-dlp）不可用时**静默跳过**，别误判为挂。
- 文档：`dotnet run --project TGBot.Docfx`（跨平台，替代已删除的 `build-docs.sh`；需 `dotnet tool install --global docfx`），输出 `docs/` 与 `TGBot.Docfx/api/`，须 0 警告（docfx `--warningsAsErrors`）。本机 docfx 需要 aspnetcore 运行时：`DOTNET_ROOT=$HOME/dotnet`（工具会自动清理 dotnet CLI 注入的 `DOTNET_ROOT_<ARCH>` 遮蔽变量，用户显式设置的 `DOTNET_ROOT` 始终生效）。`--help` 查看参数（`--skip-publish`/`--keep-cache`）。
- 发布：`dotnet publish TGBot/TGBot.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -o <dir>`

## 架构
- 单一控制台应用 `TGBot`（AssemblyName `tgdl-bot`，net10.0，自包含单文件；`ServerGarbageCollection=false` + `TieredCompilation=false` + `InvariantGlobalization=true`）。
- 入口：`Program.cs` → `Application/AppHost` → `BotService`(长轮询) → `MessageRouter` → `DownloadCoordinator`/`CommandHandler`/`CookieService`。
- 模块命名空间：`Config/Logging/Security/Access/Download/Update/Messaging/Application/Cookie/Texts`。
- Telegram.Bot 22.x：方法为**无 `Async` 后缀的扩展方法**（`SendMessage`/`GetFile`/`DownloadFile`），轮询用 `ReceiveAsync`。
- yt-dlp 经 `Process.ArgumentList` 调用（无 shell，杜绝注入）。错误分类快速失败：`AuthRequired`/`FormatUnavailable`；格式不足自动 `-J` probe 挑最高视频+音频（mkv 兜底）。
- 下载模式：`audio`=bestaudio→**flac+mp3@320k 双传**；`video`=合并。DM 内联键盘选择，非交互按 `TgdlDefaultMode`。
- **`TGBot.Docfx` 目录**：独立开发工具项目，**不入 `TGBot.slnx`**（不牵连主构建/测试/CI）。`TGBot.Docfx`（文档构建工具，跨平台）按需 `dotnet build TGBot.Docfx/TGBot.Docfx.csproj` 单独编译。**0 警告硬门槛界定**：`GenerateDocumentationFile`+CS1591 的动机是 docfx 从 TGBot 生成 API 文档的质量；tools 工具自身不产文档、不进发布镜像，**编译仍须 0 警告，但不强制 XML 注释**。

## Docker / 发布（关键）
- CI（`.github/workflows/release.yml`）由 `workflow_run` 监听 CI 完成后触发：matrix 双 job 构建 **linux/amd64（即 x64，ubuntu-latest）** 与 **linux/arm64（ubuntu-24.04-arm 免费原生 runner）**，各推 per-arch tag（`{ver}-x64`/`{ver}-arm64`），再由 manifest job 用 `buildx imagetools create` 合并出 `{ver}`+`latest` multi-arch tag 并建 Release（**无二进制资产**）。需仓库 Settings→Actions→General→Workflow permissions = **Read and write**。
- 镜像内置：tgdl-bot、telegram-bot-api（来自 fork `linkcccp/telegram-bot-api` 的 latest Release，**不本地编译**）、yt-dlp、ffmpeg 静态版、python3、**deno**（yt-dlp YouTube 提取必需；缺则报 "Requested format is not available"）。
- `docker/Dockerfile` 用 `ARG TARGETARCH`（buildx 自动注入标准值 amd64/arm64，仅 deno 分支判断）+ `ARG DISTARCH`（自命名 x64/arm64，CI 显式传，驱动 COPY 路径）；dist 布局 `docker/dist/{x64,arm64}/`（构建上下文 = `docker/`，CI 负责放产物；旧布局 `dist/amd64/` 已废弃）。
- 部署：`scripts/install.sh`（`curl|sudo bash`）→ 装/用 Docker → pull → 启动 → `docker image prune -f`（清悬空镜像）。
- **cookie 持久化**：默认 `/opt/tgdl-bot/api-data/cookies`（`tgdl-data` 卷内，v2.3.1 起，pull 重建不丢）。**勿改回 `/opt/tgdl-bot/cookies`**（早期安装非卷，会丢）。
- `docker/docker-entrypoint.sh` 由 `TGDL_*` 环境变量生成 `config.conf`（若挂载文件则跳过）。

## 配置新增的完整链路
新增配置键必须同步：`AppConfig` → `ConfigParser`（别名+解析+校验）→ `docker/docker-entrypoint.sh`（TGDL_* 映射）→ `docker/.env.example` → `docker/config.conf.example` → README 配置表。

## Git 约定（GitHub Flow：main 唯一远程分支）
- **`main`**：唯一远程分支，常驻 GitHub，随时可发布。main 分支改动本地直接 push（不走 PR），发布语义靠 `v*` tag + CHANGELOG 保证（如 `v2.4.1`，tag 与版本一一对应；push tag 触发 CI 发布镜像，无 tag 不发布）。
- **`dev`**：本地开发草稿分支（**仅本地，不 push 远程**，不是远程协作渠道）。内部多步开发在 dev 上积累；发版时由用户指示，将 dev 内容以**单一大版本提交 squash 合并进 main**（本地操作），然后 push main + 打 `v*` tag。
- **`feat/*`、`fix/*`、`chore/*` 等**：内部开发一律基于 dev 创建（**不要**基于 main 创建内部分支），开发完成并通过验证后 squash 合并回本地 dev（先拉最新 dev，`git merge --squash <分支>` 再提交），合并后删除分支。
- **外部贡献者 / Dependabot**：从 main fork/拉取（干净稳定基线）→ PR 到 **main**；Dependabot 配置保持 `base: main`（默认）。
- **push origin / dev→main 合并必须由用户主动指示**，agent 无权自行执行。
- 提交约定：每个任务/阶段完成并通过验证后自动 `git add` + `git commit`（中文，`feat:`/`fix:`/`docs:`/`test:`/`chore:` 风格）；提交前检查只暂存本次改动文件；code-reviewer 只读，禁止 git 写操作。
- **版本策略（手动发版）**：维护者手动打 `v*` tag 并 push，CI 检测通过后自动构建并发布 Docker 镜像（workflow_run 监听 CI 完成）。CHANGELOG 由维护者手动维护。
- 发布回滚由 git 管理（回退到上一 tag/提交）。
- 本机外网阻断 GitHub 22 端口：origin 已用 `ssh://git@ssh.github.com:443/...`。
- 子模块 `third_party/telegram-bot-api` → fork URL（源码参考；CI 不检出/构建）。

## 记录约定

为保证可追溯性，项目保留两类记录并提交 Git：

- **工作日志**（`docs/history/YYYY-MM-DD.md`）：由 `scribe` 在每个阶段结束后自动追加（时间、执行 agent、改动内容、原因、阶段结果、遗留问题）；格式见 `.opencode/agent/scribe.md`，未记录视为阶段未完成。
- **架构决策记录**（`docs/adr/NNNN-标题.md`）：由 `architect` 在每次架构/接口/配置契约决策时产出，编号从 `0001` 递增，记录背景、方案对比、决策与后果；模板见 `docs/adr/README.md`。决策变更不修改旧 ADR，新增并标注"已撤销"。

> 注：`docs/` 是 docfx 输出目录，但 `docs/adr/` 与 `docs/history/` 是非生成文件，docfx build 不会清理，可安全存放记录。

## 测试注意
- 共享 fakes 在 `TGBot.Tests/MessageRouterTests.cs`（`FakeTelegramClient`/`FakeDownloader`，含 `ProbeFormatsHandler`/`AudioBundleHandler`）。
- 测试项目同样须 0 警告（xunit 分析器开启；如 `Assert.DoesNotContain`、异步测试禁阻塞）。
