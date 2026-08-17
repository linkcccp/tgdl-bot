---
description: 将当前会话或最近阶段的工作写入 docs/history/YYYY-MM-DD.md 工作日志。
agent: orchestrator
---

记录工作日志：委派给 `scribe` agent，结合当前会话上下文，向 `docs/history/YYYY-MM-DD.md` 追加记录（时间、执行 agent、改动内容、原因、阶段结果、遗留问题）。若当日文件不存在则创建。完成后向用户确认已记录的内容摘要。
