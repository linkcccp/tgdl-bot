# 贡献指南 / Contributing Guide

欢迎参与 **tgdl-bot** 的开发！tgdl-bot 是一款基于 .NET 10 的 Telegram 下载 bot，
以 Docker 沙盒形式分发（内置 telegram-bot-api / yt-dlp / ffmpeg），详见
[README.md](README.md)。本项目以 GPL-3.0 开源，所有贡献均视为接受该许可条款。

本指南覆盖：开发环境、本地验证、分支与提交规范、测试要求、提 PR 流程、
报告问题与行为准则。

Welcome to **tgdl-bot**! It is a .NET 10 Telegram download bot distributed as a
Docker sandbox (bundling telegram-bot-api / yt-dlp / ffmpeg); see
[README.en.md](README.en.md). The project is licensed under GPL-3.0; by
contributing you agree to its terms.

## 开发环境 / Development Environment

- **.NET 10 SDK**（构建与测试；`dotnet --version` 应为 10.x）
- 克隆仓库（含 `third_party/telegram-bot-api` 子模块，源码参考用途）：

  ```bash
  git clone --recurse-submodules https://github.com/linkcccp/tgdl-bot.git
  ```

  > 提示：大陆网络环境访问 GitHub 可能受限（SSH 22 端口常被阻断），可改用 HTTPS
  > 克隆，或为 SSH 配置 443 端口（`ssh.github.com`）代理。
  >
  > Note: If GitHub port 22 is blocked in your network, use HTTPS clone or route
  > SSH through port 443 (`ssh.github.com`).

## 本地验证 / Local Verification

提交前请在本地完整验证（**硬门槛**）：

| 命令 | 要求 |
| --- | --- |
| `dotnet build -c Release` | **0 警告**（`GenerateDocumentationFile` 开启，所有 public 成员须有 XML 注释） |
| `dotnet test` | 全部通过；`*IntegrationTests.cs`（真实网络/yt-dlp）在环境不可用时**静默跳过**，不算失败 |
| `dotnet run --project tools/TgdlDocBuilder` | 文档改动后执行（docfx，需 `dotnet tool install --global docfx`；macOS/Linux 自定义 dotnet 安装需 `export DOTNET_ROOT=$HOME/dotnet`），须 0 警告 |
| `--smoke-test` | 可选：`dotnet publish` 后运行 `tgdl-bot --config ./config.conf --smoke-test 8` 本地自检（不联网） |

## 分支与提交规范 / Branching & Commit Guidelines

```
main  ←── PR（CI 必须全绿 + 至少 1 人审查；外部贡献者与 Dependabot 均 PR 到 main）
dev   ←── 本地草稿分支（仅本地，不 push 远程）：内部 feat/fix/chore squash 合并汇聚
feat/*、fix/*、chore/*  ←── 内部开发一律基于 dev 创建；外部贡献者基于 main 创建
```

- **`main`**：**唯一远程分支**，常驻 GitHub、随时可发布。所有改动（含小修复/文档）
  均以 **PR** 进入（CI 必须全绿 + 至少 1 人审查）；版本语义靠 `v*` tag + CHANGELOG
  保证（如 `v2.4.1`），push tag 触发 GitHub Actions 构建镜像并发布。
- **`dev`**：**本地开发草稿分支**（仅本地，不是远程协作渠道）。内部多步开发在 dev 上
  积累，发版时由维护者将 dev 内容以**单一大版本提交 squash 合并进 main**（本地操作），
  然后 push main + 打 `v*` tag。
- **外部贡献者**：从 `main` fork/拉取（干净稳定基线）→ 基于 `main` 创建分支 →
  PR 到 **`main`**；Dependabot 同样 PR 到 `main`。
- **禁止**：直接 push 到 `main`（必须走 PR）；`dev` → `main` 合并由维护者主动指示。
- **提交信息**：中文，采用 `feat:` / `fix:` / `docs:` / `test:` / `chore:` 前缀
  （如 `feat: 支持 xxx`、`fix: 修复 xxx`）；提交前检查只暂存本次改动文件。

## 测试要求 / Testing Requirements

- 新增功能/修复**必须配套测试**（xUnit），测试项目同样须 **0 警告**。
- 共享 fakes 位于 `tests/TGBot.Tests/MessageRouterTests.cs`
  （`FakeTelegramClient` / `FakeDownloader`，含 `ProbeFormatsHandler` / `AudioBundleHandler`），
  优先复用。
- 异步测试禁止阻塞（禁用 `.Result` / `.Wait()`）。
- 集成测试（`*IntegrationTests.cs`）需要真实网络/yt-dlp，环境不可用时静默跳过——
  不要误判为挂。

## 提交 Pull Request / Submitting a Pull Request

1. fork 仓库（外部贡献者），从最新 `main` 创建分支：
   `git checkout main && git pull && git checkout -b feat/xxx`；
2. 开发并完成本地验证（build 0 警告 + test 全绿 + 必要文档）；
3. push 分支并开 PR，目标分支为 **`main`**；PR 描述请填写
   [PULL_REQUEST_TEMPLATE.md](.github/PULL_REQUEST_TEMPLATE.md) 中的检查清单；
4. 合并采用 **squash**；CI（`.github/workflows/ci.yml`）必须全绿，且需至少
   1 名维护者审查通过。

> 说明：CI 在 PR（base: main）与 push 时自动运行（build 0 警告 + test 全绿），
> 合并前必须通过；分支保护规则（require PR + CI、禁 force push/删除）由维护者
> 在 GitHub 仓库设置中配置。
>
> Note: CI runs automatically on PRs (base: main) and pushes (0-warning build +
> all-green tests) and must pass before merge; branch protection (require PR + CI,
> no force-push/deletion) is configured by maintainers in the repository settings.

## 报告问题 / Reporting Issues

- **缺陷**：使用 [缺陷报告模板](.github/ISSUE_TEMPLATE/bug_report.yml)
  （需附环境、复现步骤、日志；**切勿粘贴 Bot Token / API ID / HASH / cookies 等敏感信息**）。
- **功能建议**：使用 [功能建议模板](.github/ISSUE_TEMPLATE/feature_request.yml)。
- **安全问题**：请走 [SECURITY.md](SECURITY.md) 渠道（Security Advisory 或邮件），
  **不要**在 issue 中公开漏洞细节。

## 行为准则 / Code of Conduct

参与本项目即视为同意遵守 [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md)
（Contributor Covenant 2.1）。任何骚扰、歧视或不当行为均可向
`linkzengyaoxiang@outlook.com` 举报。