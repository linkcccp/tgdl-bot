# Pull Request

感谢贡献！请填写以下信息并勾选检查清单（详见 [CONTRIBUTING.md](../CONTRIBUTING.md)）。
Thanks for contributing! Fill in the sections below and tick the checklist
(see [CONTRIBUTING.md](../CONTRIBUTING.md)).

## 描述 / Description

<!-- 简要说明本次改动的目的与内容。Briefly describe the purpose and content of this change. -->

## 改动类型 / Type of Change

- [ ] 新功能 / feat
- [ ] 缺陷修复 / fix
- [ ] 文档 / docs
- [ ] 测试 / test
- [ ] 维护 / chore

## 检查清单 / Checklist

- [ ] 基于 `dev` 分支创建（`feat/*` / `fix/*` / `chore/*`）/ Branch created from `dev`
- [ ] `dotnet build -c Release` 通过且 **0 警告** / passes with **0 warnings**
- [ ] `dotnet test` 全部通过（集成测试网络不可用时静默跳过）/ all tests pass (integration tests skip silently when network is unavailable)
- [ ] 新增功能/修复已配套测试（共享 fakes 位于 `tests/TGBot.Tests/MessageRouterTests.cs`）/ new features/fixes covered by tests (shared fakes live in `tests/TGBot.Tests/MessageRouterTests.cs`)
- [ ] 已准备 **squash** 合并回 `dev`（单条提交，信息符合 `feat:`/`fix:`/`docs:`/`test:`/`chore:` 规范）/ ready to be squash-merged into `dev`
- [ ] 行为/配置变更已同步文档：`docker/.env.example`、`docker/config.conf.example`、README（中/英）、CHANGELOG（如需）/ docs synced for behavior/config changes (`.env.example`, `config.conf.example`, README zh/en, CHANGELOG if needed)
- [ ] 未向 `main` 推送（`main` 仅接受 `dev` 合并的大版本）/ no pushes to `main` (only merges from `dev` for major versions)

## 相关 Issue / Related Issues

<!-- 如有关联 issue，填写 #编号。Link related issues, e.g. #123. -->

## 测试证据 / Test Evidence

<!-- 简述验证结果：build 输出、测试数量等。Briefly note verification results: build output, test counts, etc. -->