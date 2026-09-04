# CI + Release Workflow 重构实施指南

## 概述

本指南提供 CI 和 release workflow 重构的完整实施步骤，供 developer agent 执行。

## 重构目标

1. CI 监听所有 push（所有分支 + tag）+ 所有 PR
2. Release 使用 `workflow_run` 监听 CI 完成
3. 只有 CI 成功时才触发 Release
4. 删除 auto-version.yml

## 文件变更清单

| 文件 | 操作 | 说明 |
|------|------|------|
| `.github/workflows/ci.yml` | 修改 | 扩大触发范围到所有 push + 所有 PR |
| `.github/workflows/release.yml` | 修改 | 使用 workflow_run 监听 CI 完成 |
| `.github/workflows/auto-version.yml` | 删除 | 发版由维护者手动打 tag 控制 |

## 实施步骤

### 步骤 1：创建特性分支

```bash
git checkout -b feat/ci-release-refactor dev
```

### 步骤 2：修改 CI Workflow

**文件**: `.github/workflows/ci.yml`

**变更内容**:
1. 移除 push 的分支过滤器（监听所有分支 + tag）
2. 移除 pull_request 的分支过滤器（监听所有 PR）
3. 更新注释说明

**完整配置**: 参见 `docs/design/ci.yml.refactored`

### 步骤 3：修改 Release Workflow

**文件**: `.github/workflows/release.yml`

**变更内容**:
1. 修改触发条件为 `workflow_run`
2. 添加条件检查 job（check）
3. 添加并发控制
4. 更新构建和 manifest job 的依赖关系
5. 更新版本号解析逻辑

**完整配置**: 参见 `docs/design/release.yml.refactored`

### 步骤 4：删除 auto-version.yml

**文件**: `.github/workflows/auto-version.yml`

**操作**: 直接删除

### 步骤 5：提交更改

```bash
git add .github/workflows/
git commit -m "feat: 重构 CI + Release workflow

- CI 监听所有 push（所有分支 + tag）+ 所有 PR
- Release 使用 workflow_run 监听 CI 完成
- 只有 CI 成功且由 tag push 触发时才执行 Release
- 删除 auto-version.yml，发版由维护者手动打 tag 控制"
```

### 步骤 6：验证

#### 6.1 验证 CI 触发

```bash
# 推送到测试分支
git push origin feat/ci-release-refactor

# 检查 CI 是否运行
# 访问 GitHub Actions 页面，确认 CI workflow 已触发
```

#### 6.2 验证 PR 标题校验

```bash
# 创建 PR（标题不带类型前缀）
git push origin feat/ci-release-refactor

# 访问 GitHub，创建 PR，标题为 "test: 验证 CI"
# 确认 PR 标题校验通过
```

#### 6.3 验证 Release 触发

```bash
# 打 tag 并推送
git tag v0.0.1-test
git push origin v0.0.1-test

# 检查 CI 和 Release 是否运行
# 访问 GitHub Actions 页面，确认：
# 1. CI workflow 已触发
# 2. CI 完成后，Release workflow 已触发
# 3. Release 构建成功
```

#### 6.4 验证 Release 不误触发

```bash
# 推送到任意分支（非 tag）
git push origin feat/ci-release-refactor

# 检查 Release 是否运行
# 访问 GitHub Actions 页面，确认 Release workflow 未触发
```

## 关键配置说明

### CI 触发条件

```yaml
on:
  push:
    # 不限分支 = 所有分支 + 所有 tag
  pull_request:
    # 不限分支 = 所有 PR
```

**说明**: 移除分支过滤器后，任何 push（包括 tag push）都会触发 CI。

### Release 触发条件

```yaml
on:
  workflow_run:
    workflows: ["CI"]
    types: [completed]
```

**说明**: 监听 CI workflow 完成事件。

### Release 执行条件

```yaml
jobs:
  check:
    steps:
      - name: Check if CI succeeded and triggered by tag push
        run: |
          # 检查 CI 是否成功
          if [ "${{ github.event.workflow_run.conclusion }}" != "success" ]; then
            echo "should_release=false" >> "$GITHUB_OUTPUT"
            exit 0
          fi

          # 检查是否由 tag push 触发（head_branch 以 v 开头）
          HEAD_BRANCH="${{ github.event.workflow_run.head_branch }}"
          if [[ ! "$HEAD_BRANCH" =~ ^v ]]; then
            echo "should_release=false" >> "$GITHUB_OUTPUT"
            exit 0
          fi

          echo "should_release=true" >> "$GITHUB_OUTPUT"
          echo "version=${HEAD_BRANCH#v}" >> "$GITHUB_OUTPUT"
          echo "tag=$HEAD_BRANCH" >> "$GITHUB_OUTPUT"
```

**说明**: 使用 check job 验证两个条件：
1. CI 成功 (`conclusion == 'success'`)
2. 由 tag push 触发 (`head_branch` 以 `v` 开头)

### 并发控制

```yaml
concurrency:
  group: release
  cancel-in-progress: false
```

**说明**: 防止多个 tag 快速推送导致 Release 并发执行。

## 风险与缓解

### 风险 1: workflow_run 事件的局限性

**问题**: `workflow_run` 事件无法直接获取触发 CI 的具体分支/tag 信息。

**缓解**: 使用 `github.event.workflow_run.head_branch` 获取，tag push 时该值为 tag 名。

### 风险 2: 并发 workflow_run

**问题**: 多个 tag 快速推送可能导致 Release 并发执行。

**缓解**: 添加 concurrency 配置，确保 Release 顺序执行。

### 风险 3: 手动打 tag 的人为错误

**问题**: 维护者可能打错版本号。

**缓解**:
- CI 仍然运行，如果代码有问题会失败
- Release 只在 CI 成功后才执行
- 可以删除错误 tag 重新打

## 回滚方案

如果新流程有问题，可以：

1. **恢复 auto-version.yml**:
   ```bash
   git checkout dev -- .github/workflows/auto-version.yml
   ```

2. **恢复 ci.yml**:
   ```bash
   git checkout dev -- .github/workflows/ci.yml
   ```

3. **恢复 release.yml**:
   ```bash
   git checkout dev -- .github/workflows/release.yml
   ```

4. **提交回滚**:
   ```bash
   git add .github/workflows/
   git commit -m "revert: 回滚 CI + Release workflow 重构"
   ```

## 验证检查清单

- [ ] CI 监听所有 push（所有分支 + tag）
- [ ] CI 监听所有 PR
- [ ] PR 标题校验正常工作
- [ ] Release 使用 workflow_run 监听 CI 完成
- [ ] Release 只在 CI 成功且由 tag push 触发时执行
- [ ] auto-version.yml 已删除
- [ ] 双架构镜像构建成功
- [ ] Manifest 合并成功
- [ ] GitHub Release 创建成功
- [ ] 并发控制正常工作
