# scripts/install.sh — Docker 一键部署

在任何**能运行 Docker 的 Linux 发行版**（Debian / Ubuntu / CentOS / RHEL / Arch 等）上执行一行即可：

```bash
curl -fsSL https://raw.githubusercontent.com/linkcccp/tgdl-bot/main/scripts/install.sh | sudo bash
```

脚本会：
1. **root 检查**：非 root 直接报错退出。
2. **Docker 检测/安装**：已装且可用则直接复用；未装则自动安装
   （优先官方 `get.docker.com` 脚本，失败退回 `apt docker.io`），并 `systemctl enable --now docker`。
3. **生成配置**：把 `docker/compose.yaml` 与 `.env` 模板放到 `/opt/tgdl-bot/`（`.env` 已存在则**不覆盖**）。
4. **拉取镜像并启动**：`docker pull ghcr.io/linkcccp/tgdl-bot:latest` → `docker compose up -d`
   （无 compose 时自动退回 `docker run`）。
5. **提示填写 `.env`** 与常用运维命令。

## 首次配置（必做）

```bash
sudo nano /opt/tgdl-bot/.env
# 必填：
#   TGDL_BOT_TOKEN        @BotFather 创建 bot 后获得
#   TGDL_TARGET_CHANNELS  目标频道/群组 ID，逗号分隔（负数）
#   TGDL_ALLOWED_USERS    允许使用的用户 ID，逗号分隔
#   TGDL_API_ID / TGDL_API_HASH  https://my.telegram.org
cd /opt/tgdl-bot && sudo docker compose up -d
```

## 常用命令

```bash
docker ps                                   # 查看容器状态
docker logs -f tgdl-bot                     # 查看日志
cd /opt/tgdl-bot && sudo docker compose up -d   # 修改 .env 后重启
cd /opt/tgdl-bot && sudo docker compose pull && sudo docker compose up -d   # 升级到最新镜像
```

## 容器内自更新

私聊 bot 发 `/update` 仍可更新 yt-dlp / ffmpeg（写入 `tgdl-bin` 卷，跨容器重建保留）。
bot 二进制与 telegram-bot-api 的更新 = 拉取新镜像重建容器。

## 卸载

```bash
cd /opt/tgdl-bot
docker compose down          # 无 compose 则：docker rm -f tgdl-bot
docker volume rm tgdl-data tgdl-tmp tgdl-bin
sudo rm -rf /opt/tgdl-bot
# 如需一并移除 Docker：卸载方法见 Docker 官方文档（可选）
```
