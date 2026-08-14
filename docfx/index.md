# TGBot API 文档

`tgdl-bot` 是一个基于 .NET 10 的 Telegram 下载 Bot 控制台程序，支持 yt-dlp 下载与本地 Bot API Server（--local 模式）上传，内存占用 < 100MB。

## 模块概览

| 命名空间 | 职责 |
| --- | --- |
| `TGBot.Config` | config.conf 解析与校验（中文错误提示） |
| `TGBot.Logging` | 分级日志（控制台 + 可选文件） |
| `TGBot.Security` | URL/SSRF 校验、路径净化、临时目录、磁盘检查 |
| `TGBot.Access` | 双重白名单访问控制 |
| `TGBot.Download` | yt-dlp 下载器抽象、并发闸门、任务注册表 |
| `TGBot.Update` | ffmpeg/yt-dlp 原子自更新 |
| `TGBot.Messaging` | Telegram 客户端抽象、上传服务、文案构建 |
| `TGBot.Application` | 消息路由、下载协调、指令处理、Bot 宿主 |
| `TGBot.Texts` | 面向用户的中文提示文案 |

## 构建文档

```bash
dotnet tool install --global docfx
docfx build docfx/docfx.json
# 输出到 docs/ 目录，用浏览器打开 docs/index.html
```

本文档由 DocFX 从 XML 文档注释自动生成。
