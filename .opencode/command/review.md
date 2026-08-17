---
description: 对工作区当前改动执行代码审查（git diff），输出 P0/P1/P2 问题清单。
agent: orchestrator
---

执行代码审查：委派给 `code-reviewer` agent 审查 `git diff`（含未暂存与已暂存改动），对照 AGENTS.md 编码规范（标准注释/0 警告/SOLID/设计模式/安全/性能），输出 P0/P1/P2 问题清单与整体结论。向用户汇报审查结果；如有 P0 问题，安排 `developer` agent 修复后复审。
