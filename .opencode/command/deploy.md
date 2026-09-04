---
description: 构建并发布（dotnet publish 单文件 + docker 构建，推送 GHCR 由用户打 v* tag 触发 CI）。
agent: orchestrator
---

执行发布流程：委派给 `devops` agent 完成 `dotnet build -c Release`（0 警告）+ `dotnet test` + `dotnet publish TGBot/TGBot.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true` 与 docker 构建验证。**推送镜像走 CI（`.github/workflows/release.yml`，`v*` tag 触发），push/push tag 必须由用户主动指示，agent 不自动执行**。部署/巡检可参考 `scripts/install.sh`。完成后委派 `scribe` 记录到当日日志，向用户汇报发布产物与验证结果。
