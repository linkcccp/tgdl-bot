# 更新日志 / Changelog

本项目遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/) 格式
（中文为主、英文并存，与 README 策略一致）。
**版本号与 git tag 一一对应**：每个发布版本对应一个 `v*` tag
（如 `v2.4.0`），tag 触发 CI 构建镜像并创建 GitHub Release。

This project follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)
format (Chinese-primary with English, same policy as the README).
**Every version maps to a git tag** (e.g. `v2.4.0`); pushing the tag triggers CI
to build the Docker image and create a GitHub Release.

## [v2.5.0] - 2026-09-05

- feat: 移除 git-cliff，改用纯 git log 生成 changelog
- docs: update CHANGELOG.md for 2.5.0
- fix: 限制 changelog 仅显示当前版本变更
- docs: update CHANGELOG.md for 2.5.0
- fix: limit changelog to current version only
- docs: update CHANGELOG.md for 2.5.0
- fix: use releases[1].version for Full Changelog link
- docs: update CHANGELOG.md for 2.5.0
- fix: 简化 cliff.toml 模板语法，修复 npm 版 git-cliff 兼容性
- fix: 修复 release.yml 中 git-cliff 命令语法
- chore: 优化 docfx 文档构建，添加 GitHub Pages 自动部署
- chore: 重命名 TgdlDocBuilder 为 TGBot.Docfx，优化 docfx 文档构建流程
- feat: CI/Release workflow 重构 + Changelog 自动生成

## 历史版本 / Previous Releases

较早版本（v2.3.1 及更早）无独立更新日志，变更内容见对应 git tag 与
[GitHub Releases](https://github.com/linkcccp/tgdl-bot/releases)。

Earlier releases (v2.3.1 and before) do not have a dedicated changelog; see the
corresponding git tags and [GitHub Releases](https://github.com/linkcccp/tgdl-bot/releases)。
