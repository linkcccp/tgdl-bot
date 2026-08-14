#!/usr/bin/env bash
# 构建 DocFX API 文档。
# 前置条件：dotnet tool install --global docfx
set -euo pipefail

cd "$(dirname "$0")"

export PATH="$PATH:$HOME/.dotnet/tools"

if ! command -v docfx >/dev/null 2>&1; then
    echo "错误：未安装 docfx。请先执行：dotnet tool install --global docfx" >&2
    exit 1
fi

# 先发布一次以生成最新的 XML 文档
dotnet publish src/TGBot/TGBot.csproj -c Release -o /tmp/tgdl-docfx-publish --no-restore >/dev/null

echo ">>> docfx metadata/build ..."
docfx build docfx/docfx.json --warningsAsErrors

echo ">>> 文档已生成到 docs/，打开 docs/index.html 查看。"
