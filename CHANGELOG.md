# 更新日志 / Changelog

本项目遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/) 格式
（中文为主、英文并存，与 README 策略一致）。
**版本号与 git tag 一一对应**：每个发布版本对应一个 `v*` tag
（如 `v2.4.0`），tag 触发 CI 构建镜像并创建 GitHub Release。

This project follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)
format (Chinese-primary with English, same policy as the README).
**Every version maps to a git tag** (e.g. `v2.4.0`); pushing the tag triggers CI
to build the Docker image and create a GitHub Release.

## [v2.4.0] - Unreleased（未发布）

### 新增 / Added

- **中英双语界面（i18n）**：默认跟随用户的 Telegram 语言设置（`auto`），
  首次私聊弹窗选择语言，可用 `/language` 随时切换；新增 `TGDL_LANGUAGE`
  配置键设置全局默认（`auto` / `en` / `zh`）。
- **bot 内配置管理**：`/config` 查看/修改配置（凭据脱敏显示、连接类键可改但带
  警告、重启生效），`/access` 管理白名单成员；改配置后自动重启并发送待通知。
- **交互式安装向导**：`install.sh` 引导选择语言并逐项输入必填项（超 5 次跳过、
  无终端环境自动降级为模板模式）。
- **开源合规**：新增 GPL-3.0 `LICENSE`、`NOTICE`（第三方组件声明）、
  `SECURITY.md`（安全策略）、`README.en.md`（英文版 README）；全源码加 SPDX 头。
- **状态目录持久化（StateDir）**：`languages.json` / `config-overlay.json` /
  `access-overlay.json` / `pending-notify.json` 存于 `tgdl-data` 卷，
  **跨镜像重建/升级不丢失**；新增 `TGDL_STATE_DIR` 配置键。
- **arm64 镜像支持**：CI 双架构（x64/arm64）matrix 原生构建（arm64 用 GitHub 免费
  ARM runner），发布 `linux/amd64`（即 x64）+ `linux/arm64` multi-arch manifest 镜像，
  `docker pull` 自动按宿主架构拉取；arm64 构建侧已验证，真实运行待用户 ARM 设备实测。

- **Bilingual UI (i18n)**: follows the user's Telegram language setting by
  default (`auto`), with a language picker on first private chat and a
  `/language` command; new `TGDL_LANGUAGE` key for a global default
  (`auto` / `en` / `zh`).
- **In-bot configuration**: `/config` to view/modify settings (credentials
  masked, connection-related keys editable with a warning, effective after a
  restart), `/access` to manage allowlist members; auto-restart with pending
  notifications after config changes.
- **Interactive install wizard**: `install.sh` guides language selection and
  required fields (skips after 5 attempts, degrades to template mode without a
  TTY).
- **Open-source compliance**: GPL-3.0 `LICENSE`, `NOTICE` (third-party notices),
  `SECURITY.md` (security policy), `README.en.md` (English README); SPDX
  headers added across the source tree.
- **Persistent state directory (StateDir)**: `languages.json`,
  `config-overlay.json`, `access-overlay.json` and `pending-notify.json` live
  in the `tgdl-data` volume and **survive image recreation/upgrades**; new
  `TGDL_STATE_DIR` config key.
- **arm64 image support**: dual-arch (x64/arm64) matrix native CI builds (arm64 on
  GitHub's free ARM runner) publish a `linux/amd64` (i.e. x64) + `linux/arm64`
  multi-arch manifest image; `docker pull` automatically fetches the matching
  architecture. Build side verified; real-device runtime testing on ARM pending.

### 修复 / Fixed

- 配置引号值统一归一化（如 `LocalApiBaseUrl`，校验与落盘一致，P1-1）。
- 状态文件权限收紧为 0600；配置中凭据（token 等）展示脱敏。
- 更新失败提示改走 i18n（不再中文硬编码直发）。
- 配置变更重启增加 60 秒节流，防止崩溃循环。
- `/language` 重复弹窗去重；`/config` 同值去重并入生效值。
- entrypoint 补写 `StateDir` / `TgdlLanguage`（修复重建丢状态）；bool 解析
  大小写不敏感；Dockerfile 删除冗余 ENV；install.sh 增加容器 running/镜像
  匹配兜底校验。
- `NOTICE` 许可证声明修正（telegram-bot-api 实为 Boost Software License 1.0，
  ffmpeg 明确 GPL）。

- Normalized quoted config values (e.g. `LocalApiBaseUrl`, consistent between
  validation and on-disk form).
- Tightened state file permissions to 0600; masked credentials (tokens etc.) in
  config display.
- Update-failure messages now go through i18n instead of hardcoded Chinese.
- Added a 60-second restart throttle after config changes.
- Deduplicated `/language` picker popups and `/config` no-op changes.
- entrypoint now writes `StateDir` / `TgdlLanguage` (fixes state loss on image
  recreation); case-insensitive bool parsing; removed redundant Dockerfile ENVs;
  install.sh fallback check for running container/image match.
- Corrected license declarations in `NOTICE` (telegram-bot-api is Boost Software
  License 1.0; ffmpeg explicitly GPL).

### 变更 / Changed

- Git 工作流调整：`dev` 为汇聚分支，`main` 仅接受 `dev` 合并的大版本发布；
  开发分支 squash 合并回 `dev`。
- 建立 agent 调度体系（`.opencode/`，8 环节流程）与 ADR / 工作日志记录约定；
  docfx API 文档接入 `./build-docs.sh`。

- Git workflow: `dev` is the integration branch; `main` only accepts
  major-version merges from `dev`; feature branches are squash-merged back.
- Introduced an agent orchestration setup (`.opencode/`, 8-stage workflow) and
  ADR / work-log conventions; docfx API docs wired into `./build-docs.sh`.

## 历史版本 / Previous Releases

较早版本（v2.3.1 及更早）无独立更新日志，变更内容见对应 git tag 与
[GitHub Releases](https://github.com/linkcccp/tgdl-bot/releases)。

Earlier releases (v2.3.1 and before) do not have a dedicated changelog; see the
corresponding git tags and [GitHub Releases](https://github.com/linkcccp/tgdl-bot/releases).

[Unreleased]: https://github.com/linkcccp/tgdl-bot/compare/v2.3.1...dev
[v2.4.0]: https://github.com/linkcccp/tgdl-bot/releases/tag/v2.4.0