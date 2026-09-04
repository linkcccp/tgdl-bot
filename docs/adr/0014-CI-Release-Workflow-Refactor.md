# ADR 0014: CI + Release Workflow 重构

## 状态
提议

## 背景

当前 CI 和 release workflow 存在以下问题：

1. **CI 触发范围有限**：只监听 dev/feat/fix/chore 分支的 push，其他分支的变更不会触发 CI
2. **Release 触发机制**：依赖 tag push，但 auto-version.yml 自动打 tag 的逻辑与手动控制冲突
3. **流程复杂度**：auto-version.yml 增加了自动化复杂度，且维护者无法完全控制发版时机

## 决策驱动因素

1. **全面覆盖**：任何代码变更都应通过 CI 验证
2. **质量门禁**：只有 CI 成功后才允许发布
3. **手动控制**：维护者应完全控制发版时机
4. **简化流程**：减少自动化复杂度，降低维护成本

## 方案对比

### 方案 A：保持现有流程（拒绝）

**优点**：
- 无需改动
- auto-version 自动化程度高

**缺点**：
- CI 覆盖不全面
- 维护者无法完全控制发版时机
- 自动化逻辑复杂，难以调试

### 方案 B：CI + workflow_run（采用）

**优点**：
- CI 覆盖所有代码变更
- Release 只在 CI 成功且由 tag push 触发时执行
- 维护者通过打 tag 完全控制发版时机
- 流程清晰，易于理解

**缺点**：
- 需要手动打 tag
- workflow_run 事件有一定局限性

### 方案 C：CI + workflow_call（拒绝）

**优点**：
- 更强的流程控制
- 可以传递复杂参数

**缺点**：
- 需要重构 CI 为可复用 workflow
- 增加配置复杂度
- 不符合需求（需求是 workflow_run，不是 workflow_call）

## 决策

采用**方案 B：CI + workflow_run**。

## 详细设计

### CI Workflow (ci.yml)

```yaml
on:
  push:
    # 不限分支 = 所有分支 + 所有 tag
  pull_request:
    # 不限分支 = 所有 PR
```

**设计理由**：
- 全面覆盖任何代码变更
- 简化配置，减少维护成本
- 保持现有构建/测试逻辑不变

### Release Workflow (release.yml)

```yaml
on:
  workflow_run:
    workflows: ["CI"]
    types: [completed]

jobs:
  check:
    # 检查 CI 成功且由 tag push 触发
    outputs:
      should_release: ${{ steps.check.outputs.should_release }}
      version: ${{ steps.check.outputs.version }}
      tag: ${{ steps.check.outputs.tag }}
  
  build:
    needs: check
    if: needs.check.outputs.should_release == 'true'
    # 双架构构建逻辑（与现有相同）
  
  manifest:
    needs: [check, build]
    if: needs.check.outputs.should_release == 'true'
    # Manifest 合并 + Release 创建（与现有相同）
```

**关键设计**：
1. **条件过滤**：使用 check job 验证 CI 成功且由 tag push 触发
2. **参数传递**：通过 outputs 传递版本号和 tag 名
3. **并发控制**：添加 concurrency 配置避免 Release 并发执行

### 删除 auto-version.yml

**原因**：
- 自动打 tag 的逻辑与新的手动打 tag 流程冲突
- 维护者需要完全控制发版时机
- 简化流程，减少自动化复杂度

## 后果

### 正面

1. **全面 CI 覆盖**：任何代码变更都会触发 CI
2. **质量门禁**：只有 CI 成功后才发布
3. **手动控制**：维护者通过打 tag 完全控制发版时机
4. **流程清晰**：易于理解和维护

### 负面

1. **手动操作**：维护者需要手动打 tag
2. **学习成本**：团队需要适应新的发版流程

### 风险缓解

1. **workflow_run 局限性**：使用 `github.event.workflow_run.head_branch` 获取 tag 名，tag push 时该值为 tag 名
2. **并发 Release**：添加 concurrency 配置，确保 Release 顺序执行
3. **人为错误**：CI 仍然运行，如果代码有问题会失败；Release 只在 CI 成功后才执行

## 验证方式

1. **CI 触发验证**：
   - 推送到任意分支 → CI 运行
   - 推送 tag → CI 运行
   - 创建 PR → CI 运行

2. **Release 触发验证**：
   - Tag push + CI 成功 → Release 运行
   - Tag push + CI 失败 → Release 不运行
   - 分支 push + CI 成功 → Release 不运行
   - PR 合并 + CI 成功 → Release 不运行

3. **构建验证**：
   - 双架构镜像构建成功
   - Manifest 合并成功
   - GitHub Release 创建成功

## 迁移步骤

1. **准备阶段**：备份现有 workflow 文件，创建特性分支
2. **实施阶段**：修改 ci.yml、修改 release.yml、删除 auto-version.yml
3. **测试阶段**：推送到测试分支验证 CI，创建测试 tag 验证 Release
4. **发布阶段**：合并到 main，打 tag 验证完整流程

## 回滚方案

如果新流程有问题，可以：
1. 恢复 auto-version.yml
2. 恢复 ci.yml 原有分支过滤
3. 恢复 release.yml 原有 tag 触发

所有改动都在 Git 中，可以快速回滚。
