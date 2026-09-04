#!/bin/bash
# 安装 git-cliff 的脚本
# 用于 CI 环境或本地开发环境

set -euo pipefail

# 检查是否已安装 git-cliff
if command -v git-cliff &> /dev/null; then
    echo "✅ git-cliff 已安装"
    git-cliff --version
    exit 0
fi

echo "📦 安装 git-cliff..."

# 检查 npm 是否可用
if ! command -v npm &> /dev/null; then
    echo "❌ npm 未安装，请先安装 Node.js"
    exit 1
fi

# 安装 git-cliff
npm install -g git-cliff

# 验证安装
if command -v git-cliff &> /dev/null; then
    echo "✅ git-cliff 安装成功"
    git-cliff --version
else
    echo "❌ git-cliff 安装失败"
    exit 1
fi
