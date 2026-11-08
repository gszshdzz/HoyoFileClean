# 项目协作

## 交付节奏

按模块交付。每个模块完成后，说明改动、提供验证结果和用户可操作的验收方法；等待用户明确验收通过，再开始下一个模块。

模块 0 已验收通过。当前实施范围为模块 1：默认轻扫只读路径和大小，可选重扫复用列表计算哈希，支持自定义排除目录和文件。本次用户已授权自动验证和 code-review 通过后提交，不再要求人工运行；比对、移动和界面仍未开始，下一模块需用户明确指定。多线程和自动获取清单均为低优先级，后者留到核心逻辑完成后参考 Collapse Launcher。`refs/` 旧脚本仅作流程参考，不作为正式实现或安全规则的依据；用户提供的 manifest 与 `main.json` 用于真实转换回归。

## Agent skills

### Issue tracker

方案与任务使用本地 Markdown，约定见 `docs/agents/issue-tracker.md`。

### Domain docs

使用根目录 `GLOSSARY.md` 和 `docs/adr/`；探索项目前按 `docs/agents/domain.md` 阅读相关领域文档。
