# scripts/install.sh — 一条命令安装

在 **Debian 12（x86_64）** VPS 上执行以下一行即可从 GitHub Release 下载并安装 tgdl-bot：

```bash
curl -fsSL https://raw.githubusercontent.com/linkcccp/tgdl-bot/main/scripts/install.sh | sudo bash
```

## 前置要求

- 系统：Debian 12（或 Debian 系兼容版本），x86_64 架构
- 需有 root 权限（脚本用 `| sudo bash` 运行）
- 仓库需已发布 **Release**（`git push origin v1.0.0` 触发 CI 构建后自动创建）

## 脚本做了什么

1. **root 检查**：非 root 直接报错退出。
2. **前置检查**：校验 x86_64 与 Debian 系；`apt` 安装 `curl`、`jq`、`xz-utils`。
3. **拉取 Release**：调用 `https://api.github.com/repos/linkcccp/tgdl-bot/releases/latest`
   用 `jq` 解析最新 tag 与 `tgdl-bot-*-linux-x64.tar.gz` 资产；支持 `TGDL_VERSION` 指定版本。
   下载 → 解压到临时目录 → 校验二进制存在且可执行。
4. **可选自动安装 telegram-bot-api**：尝试从
   `https://github.com/tdlib/telegram-bot-api/releases/download/latest/telegram-bot-api-linux-amd64`
   下载官方预编译二进制（校验 ELF 魔数）；失败则提示手动安装（不阻断）。
   > 决策理由：本地 Bot API Server 是 bot 正常工作的必需组件，自动安装可省去手动步骤；
   > 但 tdlib 仓库当前无 GitHub release 资产（需源码构建），故采用“自动尝试 + 优雅回退”。
5. **复用 `deploy/install.sh`**：调用
   `bash deploy/install.sh <解压出的 tgdl-bot>`，由它完成系统用户/目录创建、二进制安装、
   配置模板生成、systemd 单元安装与启动。
6. **验证**：检查两个服务的 `is-active/is-enabled`；检查 `journalctl -u tgdl-bot`
   最近日志有无 `[ERROR]`；运行 `--smoke-test` 自检并读取 RSS（目标 < 100MB）。
7. **提示填写配置**：列出 `config.conf`（BotToken / TargetChannelIds / AllowedUserIds）与
   `api.env`（API_ID / API_HASH）的填写命令与重启命令。

## 可重入性

- `config.conf` / `api.env` 已存在时**不会覆盖**（由 `deploy/install.sh` 保证）。
- 已安装**相同版本**二进制时**跳过覆盖**（`cmp` 逐字节比较）。
- 重新执行脚本 = 升级到最新 Release，配置保留，安全无副作用。

## 环境变量

| 变量 | 默认 | 说明 |
| --- | --- | --- |
| `TGDL_VERSION` | （最新版） | 指定安装版本，如 `TGDL_VERSION=v1.0.0` |
| `TGDL_REPO` | `linkcccp/tgdl-bot` | 指定仓库 |
| `TGDL_API_BASE` | `https://api.github.com` | 指定 GitHub API 地址（镜像/测试用） |

## 卸载

```bash
# 1) 停止并禁用服务
sudo systemctl disable --now tgdl-bot telegram-bot-api

# 2) 删除 systemd 单元
sudo rm -f /etc/systemd/system/tgdl-bot.service /etc/systemd/system/telegram-bot-api.service
sudo systemctl daemon-reload

# 3) 删除数据与日志
sudo rm -rf /opt/tgdl-bot /var/lib/tgdl-bot /var/lib/tgdl-bot-api /var/log/tgdl-bot-api.log

# 4) 删除服务用户
sudo userdel tgdl-bot 2>/dev/null || true
```
