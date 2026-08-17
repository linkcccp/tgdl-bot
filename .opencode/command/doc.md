---
description: 生成/更新 API 文档（dotnet run --project tools/TgdlDocBuilder 用 docfx 生成）并核对与源码的一致性。
agent: orchestrator
---

生成文档：先确认源码 public 成员均有标准 XML 注释（缺失会导致 docfx 警告），再委派 `docs` agent 运行 `dotnet run --project tools/TgdlDocBuilder`（需 `DOTNET_ROOT=$HOME/dotnet`）生成 `docs/` 与 `docfx/api/`，核对与 AGENTS.md、ADR 的一致性。若发现缺失注释，报告缺失位置供 `developer` 补齐后重新生成。完成后委派 `scribe` 记录到当日日志，并向用户汇报覆盖范围。
