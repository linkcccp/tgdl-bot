# Pull Request

感谢贡献！请填写以下信息并勾选检查清单（详见 [CONTRIBUTING.md](../CONTRIBUTING.md)）。
Thanks for contributing! Fill in the sections below and tick the checklist
(see [CONTRIBUTING.md](../CONTRIBUTING.md)).

## 描述 / Description

<!-- 简要说明本次改动的目的与内容。Briefly describe the purpose and content of this change. -->

## PR 标题类型 / PR Title Type

PR 标题**必须以类型前缀开头**（CI 强制校验，Dependabot 自动更新除外）。
类型决定合并后自动发布的版本（SemVer，auto-version workflow）：

| 前缀 | 版本影响 | 示例 |
| --- | --- | --- |
| `breaking:`（或 `feat!:`) | 大版本 vX.0.0 | `breaking: 移除旧配置键` |
| `feat:` | 小版本 vX.Y.0 | `feat: 支持多线程下载` |
| `fix:` | 修订号 vX.Y.Z+1 | `fix: 修复内存泄漏` |
| `chore:` | 修订号 vX.Y.Z+1 | `chore: 更新依赖` |
| `docs:` | 不发版（无 tag） | `docs: 更新安装说明` |

可带 scope（如 `feat(config):`）。标题不符合格式将导致 **CI 红叉**，无法合并；
规则详见 [CONTRIBUTING.md](../CONTRIBUTING.md) 的"版本与发布"小节。

The PR title **must start with a type prefix** (enforced by CI; Dependabot
updates are exempt). The type determines the next version tag (SemVer,
auto-version workflow) after the PR is merged into `main`:

| Prefix | Version impact | Example |
| --- | --- | --- |
| `breaking:` (or `feat!:`) | major vX.0.0 | `breaking: remove legacy config keys` |
| `feat:` | minor vX.Y.0 | `feat: support multi-threaded downloads` |
| `fix:` | patch vX.Y.Z+1 | `fix: fix memory leak` |
| `chore:` | patch vX.Y.Z+1 | `chore: bump dependencies` |
| `docs:` | no release (no tag) | `docs: update install guide` |

An optional scope is allowed (e.g. `feat(config):`). A non-conforming title
fails **CI** and cannot be merged; see "版本与发布 / Versioning & Releases" in
[CONTRIBUTING.md](../CONTRIBUTING.md).

## 改动类型 / Type of Change

- [ ] 新功能 / feat
- [ ] 缺陷修复 / fix
- [ ] 文档 / docs
- [ ] 测试 / test
- [ ] 维护 / chore

## 检查清单 / Checklist

- [ ] 基于 `main` 创建分支（`feat/*` / `fix/*` / `chore/*`）/ Branch created from `main`
- [ ] `dotnet build -c Release` 通过且 **0 警告** / passes with **0 warnings**
- [ ] `dotnet test` 全部通过（集成测试网络不可用时静默跳过）/ all tests pass (integration tests skip silently when network is unavailable)
- [ ] 新增功能/修复已配套测试（共享 fakes 位于 `tests/TGBot.Tests/MessageRouterTests.cs`）/ new features/fixes covered by tests (shared fakes live in `tests/TGBot.Tests/MessageRouterTests.cs`)
- [ ] 合并采用 **squash** 到 `main`（单条提交，信息符合 `feat:`/`fix:`/`docs:`/`test:`/`chore:` 规范）/ ready to be squash-merged into `main`
- [ ] 行为/配置变更已同步文档：`docker/.env.example`、`docker/config.conf.example`、README（中/英）、CHANGELOG（如需）/ docs synced for behavior/config changes (`.env.example`, `config.conf.example`, README zh/en, CHANGELOG if needed)
- [ ] 目标分支为 `main`（CI 必须全绿 + 至少 1 人审查）/ PR targets `main` (CI green + at least 1 review)

## 相关 Issue / Related Issues

<!-- 如有关联 issue，填写 #编号。Link related issues, e.g. #123. -->

## 测试证据 / Test Evidence

<!-- 简述验证结果：build 输出、测试数量等。Briefly note verification results: build output, test counts, etc. -->