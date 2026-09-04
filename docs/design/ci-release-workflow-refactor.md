# CI + Release Workflow 重构设计

## 1. 背景与目标

### 当前流程
```
分支 push (dev/feat/fix/chore) → CI 构建测试
PR → CI 构建测试 + PR 标题校验
main push → auto-version 自动打 tag → Release 构建发布
tag push (v*) → Release 构建发布
```

### 重构目标
```
所有 push (所有分支 + tag) → CI 构建测试
所有 PR → CI 构建测试 + PR 标题校验
CI 完成 → Release 检查是否为 tag push + CI 成功 → 构建发布
```

### 核心变更
1. CI 监听范围扩大到所有 push + 所有 PR
2. Release 使用 `workflow_run` 事件监听 CI 完成
3. 删除 auto-version.yml（发版由维护者手动打 tag 控制）

## 2. 技术设计

### 2.1 CI Workflow (ci.yml)

#### 触发条件
```yaml
on:
  push:
    # 不限分支 = 所有分支 + 所有 tag
  pull_request:
    # 不限分支 = 所有 PR
```

#### 设计理由
- **全面覆盖**: 任何代码变更（分支 push、tag push、PR）都必须通过 CI 验证
- **简化配置**: 移除分支过滤器，减少维护成本
- **保持现有逻辑**: 构建（0 警告硬门槛）+ 测试 + PR 标题校验不变

#### 权限
```yaml
permissions:
  contents: read  # CI 只读，不产生外部写入
```

### 2.2 Release Workflow (release.yml)

#### 触发条件
```yaml
on:
  workflow_run:
    workflows: ["CI"]
    types: [completed]
```

#### 关键设计: 条件过滤
Release job 需要满足两个条件才执行：
1. CI 执行成功 (`conclusion == 'success'`)
2. CI 由 tag push 触发 (`head_branch` 以 `v` 开头)

```yaml
jobs:
  release:
    if: >-
      github.event.workflow_run.conclusion == 'success' &&
      startsWith(github.event.workflow_run.head_branch, 'v')
    runs-on: ubuntu-latest
    steps:
      # Checkout tag 对应的代码
      - uses: actions/checkout@v7
        with:
          ref: ${{ github.event.workflow_run.head_branch }}
          fetch-depth: 0
```

#### 设计理由
- **精确触发**: 只在 tag push 的 CI 成功后才发布，避免 PR/分支 push 误触发
- **状态验证**: 确保代码质量（CI 成功）后才发布
- **手动控制**: 维护者通过打 tag 控制发版时机，更灵活

#### 权限
```yaml
permissions:
  contents: write   # 创建 Release
  packages: write   # 推送 GHCR 镜像
```

### 2.3 删除 auto-version.yml

#### 原因
- 自动打 tag 的逻辑与新的手动打 tag 流程冲突
- 维护者需要完全控制发版时机
- 简化流程，减少自动化复杂度

#### 影响
- 维护者需手动执行 `git tag vX.Y.Z && git push origin vX.Y.Z`
- PR 标题校验仍然保留（作为代码规范，即使不用于自动版本计算）

## 3. 完整配置

### 3.1 CI (ci.yml)

```yaml
name: CI

on:
  push:
    # 所有分支 + 所有 tag
  pull_request:
    # 所有 PR

permissions:
  contents: read

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - name: Checkout
        uses: actions/checkout@v7

      - name: Setup .NET
        uses: actions/setup-dotnet@v6
        with:
          dotnet-version: '10.0.x'

      - name: Restore
        run: dotnet restore

      - name: Build (Release, 0 warnings gate)
        shell: bash
        run: |
          set -euo pipefail
          dotnet build -c Release 2>&1 | tee build.log
          if grep -E "warning" build.log; then
            echo "::error::构建存在警告（0 警告是硬门槛），请修复后重新推送"
            exit 1
          fi

      - name: Test
        run: dotnet test

  pr-title-check:
    name: PR 标题格式校验（SemVer 发版依据）
    if: github.event_name == 'pull_request' && github.actor != 'dependabot[bot]'
    runs-on: ubuntu-latest
    steps:
      - name: Check PR title prefix
        env:
          TITLE: ${{ github.event.pull_request.title }}
        run: |
          echo "PR 标题: $TITLE"
          if ! echo "$TITLE" | grep -qE '^(feat|fix|breaking|chore|docs)(\([^)]+\))?!?: '; then
            echo "::error::PR 标题必须以类型前缀开头：breaking: / feat: / fix: / chore: / docs:（如 'feat: 支持 xxx'）——自动发版依赖标题类型"
            exit 1
          fi
```

### 3.2 Release (release.yml)

```yaml
name: Release

on:
  workflow_run:
    workflows: ["CI"]
    types: [completed]

permissions:
  contents: write
  packages: write

jobs:
  release:
    name: Release (Docker multi-arch)
    # 仅在 CI 成功且由 tag push 触发时执行
    if: >-
      github.event.workflow_run.conclusion == 'success' &&
      startsWith(github.event.workflow_run.head_branch, 'v')
    runs-on: ubuntu-latest
    steps:
      # Checkout tag 对应的代码（workflow_run 默认 checkout main，需显式指定 tag）
      - name: Checkout
        uses: actions/checkout@v7
        with:
          ref: ${{ github.event.workflow_run.head_branch }}
          fetch-depth: 0

      - name: Setup .NET
        uses: actions/setup-dotnet@v6
        with:
          dotnet-version: '10.0.x'

      - name: Resolve version
        shell: bash
        run: |
          TAG="${{ github.event.workflow_run.head_branch }}"
          echo "VERSION=${TAG#v}" >> "$GITHUB_ENV"

      # ... 后续构建逻辑与现有 release.yml 相同（双架构构建 + manifest 合并）
```

## 4. 流程对比

### 4.1 重构前
```
1. 开发者推送代码到 dev/feat/fix/chore 分支
2. CI 自动运行
3. 创建 PR 到 main/dev
4. CI 运行 + PR 标题校验
5. 合并 PR 到 main
6. auto-version 自动计算版本号并打 tag
7. tag push 触发 Release
8. Release 构建双架构镜像并发布
```

### 4.2 重构后
```
1. 开发者推送代码到任意分支
2. CI 自动运行
3. 创建 PR 到任意分支
4. CI 运行 + PR 标题校验
5. 合并 PR
6. 维护者手动打 tag: git tag vX.Y.Z && git push origin vX.Y.Z
7. CI 运行（tag push 触发）
8. CI 完成后，Release 自动触发
9. Release 构建双架构镜像并发布
```

## 5. 测试验证点

### 5.1 CI 触发验证
- [ ] 推送到任意分支 → CI 运行
- [ ] 推送 tag → CI 运行
- [ ] 创建 PR → CI 运行
- [ ] PR 标题校验正常工作

### 5.2 Release 触发验证
- [ ] Tag push + CI 成功 → Release 运行
- [ ] Tag push + CI 失败 → Release 不运行
- [ ] 分支 push + CI 成功 → Release 不运行
- [ ] PR 合并 + CI 成功 → Release 不运行

### 5.3 构建验证
- [ ] 双架构镜像构建成功
- [ ] Manifest 合并成功
- [ ] GitHub Release 创建成功

## 6. 风险与缓解

### 6.1 风险: workflow_run 事件的局限性
- **问题**: `workflow_run` 事件无法直接获取触发 CI 的具体分支/tag 信息
- **缓解**: 使用 `github.event.workflow_run.head_branch` 获取，tag push 时该值为 tag 名

### 6.2 风险: 并发 workflow_run
- **问题**: 多个 tag 快速推送可能导致 Release 并发执行
- **缓解**: 添加 concurrency 配置，确保 Release 顺序执行

```yaml
concurrency:
  group: release
  cancel-in-progress: false  # 已开始的 Release 必须完成
```

### 6.3 风险: 手动打 tag 的人为错误
- **问题**: 维护者可能打错版本号
- **缓解**: 
  - CI 仍然运行，如果代码有问题会失败
  - Release 只在 CI 成功后才执行
  - 可以删除错误 tag 重新打

## 7. 迁移步骤

1. **准备阶段**
   - 备份现有 workflow 文件
   - 创建特性分支 `feat/ci-release-refactor`

2. **实施阶段**
   - 修改 ci.yml 触发条件
   - 修改 release.yml 使用 workflow_run
   - 删除 auto-version.yml

3. **测试阶段**
   - 推送到测试分支验证 CI
   - 创建测试 tag 验证 Release
   - 验证 PR 流程

4. **发布阶段**
   - 合并到 main
   - 打 tag 验证完整流程

## 8. 回滚方案

如果新流程有问题，可以：
1. 恢复 auto-version.yml
2. 恢复 ci.yml 原有分支过滤
3. 恢复 release.yml 原有 tag 触发

所有改动都在 Git 中，可以快速回滚。
