# 安全策略 / Security Policy

## 受支持版本 / Supported Versions

仅维护**最新发布版本**（`v*` tag 对应的 Release，含 `:latest` Docker 镜像）。
旧版本不提供安全修复，请及时升级。

| 版本 | 支持状态 |
| --- | --- |
| 最新 Release（v\*） | ✅ 支持 |
| 更早版本 | ❌ 不维护，请升级 |

## 上报渠道 / Reporting a Vulnerability

**优先（Recommended）**：通过 GitHub Security Advisory 上报
（仓库 → **Security** → **Report a vulnerability**），
可提供私密讨论空间，避免提前公开漏洞细节。

**后备（Fallback）**：发送邮件至 `linkzengyaoxiang@outlook.com`，
主题请以 `[SECURITY]` 开头，并在正文包含：

- 受影响版本
- 漏洞类型与严重程度评估
- 复现步骤（尽可能精简）
- 建议的缓解/修复方案（可选）

## 响应承诺 / Response Commitment

- 上报后 **5 个工作日内**确认收到并初步评估；
- 确认有效的漏洞，**90 天内**修复并发布新版本（修复方案视复杂度而定）；
- 修复发布前，我们会与上报者协商披露时间，避免 0-day 提前公开。

## 不在受理范围 / Out of Scope

以下内容**不**属于本安全策略受理范围，请勿通过上述渠道上报：

- **Bot 滥用举报**（如他人用 bot 下载内容）：请走仓库 Issue 流程或
  [法律与合规声明](README.md) 中描述的方式；
- **第三方站点版权投诉**：本项目不托管内容，请直接联系目标站点；
- 用户自身部署/配置问题（环境变量、Docker 等）：请先查阅 README。

## 安全设计要点（参考）

- 容器内以非 root 用户运行（降权）；URL/命令/路径全面校验净化（SSRF 防护）；
- 私聊/群组双重白名单访问控制；临时目录 0700；日志脱敏（不记录 Bot Token）；
- 配置通过环境变量注入，不进镜像层；cookies 存于独立卷（0600）。