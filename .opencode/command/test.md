---
description: 运行测试套件并报告结果（dotnet test）。
agent: orchestrator
---

执行测试：委派给 `qa` agent 运行 `dotnet test`，检查测试通过/失败/跳过情况（`*IntegrationTests.cs` 需真实网络/yt-dlp，不可用时静默跳过，别误判为挂），必要时修复测试或记录缺陷。完成后委派 `scribe` 记录到当日日志。向用户汇报测试结果摘要（通过用例、失败用例、跳过用例、覆盖率、发现的问题）。
