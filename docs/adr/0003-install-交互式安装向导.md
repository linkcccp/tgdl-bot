# 0003-install-交互式安装向导

- **状态**：已采纳
- **日期**：2026-08-17
- **决策者**：architect

## 背景

现有 `scripts/install.sh` 为非交互：下载 `.env.example` 模板后提示用户手动编辑。需求：纯交互式向导——语言选择 → 必填项逐项输入（TGDL_BOT_TOKEN / TGDL_TARGET_CHANNELS / TGDL_ALLOWED_USERS / TGDL_API_ID / TGDL_API_HASH）→ 逐项校验（失败原地重问，单项超 5 次跳过）→ 自动写 .env 并启动；保留 `curl|sudo bash` 一键能力；无 TTY 降级。

## 方案对比

| 方案 | 优点 | 缺点 |
| ---- | ---- | ---- |
| A：`read` 直接读 stdin | 实现最简单 | `curl … \| sudo bash` 时 bash 的 stdin 是管道（curl 输出），`read` 会误读脚本内容，向导必然损坏 |
| B：`read -r … < /dev/tty` 专用 TTY 输入 | 与管道 stdin 完全隔离，`curl\|sudo bash` 下工作正常；无 TTY 时可检测降级 | 需处理 /dev/tty 不可读场景（CI/无终端） |
| C：分离交互脚本（install.sh 非交互 + install-interactive.sh 交互） | 职责分离 | 两个脚本的下载入口/维护成本；用户易混淆 |

校验与跳过语义对比：A 单项失败直接退出（用户重跑，丢已填项）vs B 失败原地重问、超 5 次跳过并汇总（其余项继续，跳过项最后提示手动补填）。B 更贴近"交互向导"体验且可自动完成大部分部署。

## 决策

- **输入源选 B**：所有交互 `read -r … < /dev/tty`；`[[ -r /dev/tty ]]` 失败 → 降级为现有非交互路径（下载模板 + 提示手动填写 + 明确"检测到非交互环境"提示）。保留 `curl|sudo bash` 一键能力。
- **第一步语言选择**：菜单（1. 中文 / 2. English）决定向导提示语言（`WIZ_LANG`），并**同时写入 `TGDL_LANGUAGE`**（向导语言 = 部署者语言 → bot 全局默认语言，语义一致；后续可用 /language 或改 .env 调整）。默认 `auto` 不写该键（保持现有 .env 语义）。
- **校验规则（就地重问，单项 ≤ 5 次）**：
  - `TGDL_BOT_TOKEN`：正则 `^\d+:[A-Za-z0-9_-]+$` 且长度 ≤ 200
  - `TGDL_TARGET_CHANNELS`：逗号分隔整数（允许负数）、至少 1 个、去重
  - `TGDL_ALLOWED_USERS`：逗号分隔正整数、至少 1 个、去重
  - `TGDL_API_ID`：正整数（int 范围）
  - `TGDL_API_HASH`：正则 `^[0-9a-fA-F]{32}$`
  - 失败重问回显当前值 `${VAR:-(未填写)}`；连续 5 次失败 → 记入 `SKIPPED` 继续下一项；全部结束后汇总提示"以下项未填写，请稍后手动编辑 /opt/tgdl-bot/.env 后 docker compose up -d"。
- **写 .env 与启动**：交互路径基于下载的 `.env.example` 模板 `sed` 替换 5 个必填项占位值（注意转义）→ 追加 `TGDL_LANGUAGE=<WIZ_LANG>` → `chmod 600`；随后沿用现有启动链路（compose up -d → docker run 兜底 → image prune）。幂等：.env 已存在且必填项非空 → 跳过向导直接启动。
- **技术约束**：脚本保持 `set -euo pipefail`；校验函数集中定义（`validate_token` 等），便于单测与 shellcheck。

## 后果

- 正面：`curl|sudo bash` 下交互可用（/dev/tty 隔离）；无效输入不打断流程（重问/跳过）；安装即完成 bot 默认语言设定；无 TTY 场景行为可预期（降级非交互）。
- 负面：/dev/tty 在部分受限环境（systemd-run、远程 SSH 无分配 TTY）不可用 → 走降级路径（可接受）；交互路径依赖模板下载成功（失败时提示手动创建 .env）；TGDL_LANGUAGE 随向导语言固定后，后续用户改 .env 需手动编辑（README 说明）。
- 实现注意：`sed` 替换值含 `/` 或 `&` 时需转义（token/hash 无此字符，channel 负数 ID 无此字符，安全；但实现仍用 `sed "s|…|…|"` 分隔符规避）；跳过项汇总在启动后仍输出；降级路径必须保留现有全部行为（非交互用户不受影响）。