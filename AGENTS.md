# 项目协作

## 交付节奏

按模块交付。每个模块完成后，说明改动、提供验证结果和用户可操作的验收方法；等待用户明确验收通过，再开始下一个模块。

模块 0 已验收，模块 1 已完成并提交。当前用户指定模块 2：多输入、对称清单比较，方案与状态见 `.scratch/file-comparison/spec.md` 及其任务记录；两侧之间的差异正常输出，同侧矛盾才报错。坚持最小实现，复用现有库和共用校验，不单独交付清单组合模块。移动和界面仍未开始，下一模块需用户明确指定。多线程和自动获取清单均为低优先级，后者留到核心逻辑完成后参考 Collapse Launcher。`refs/` 旧脚本仅作流程参考，不作为正式实现或安全规则的依据；用户提供的 manifest 与 `main.json` 用于真实转换回归。

## Agent skills

### Issue tracker

方案与任务使用本地 Markdown，约定见 `docs/agents/issue-tracker.md`。

### Domain docs

使用根目录 `GLOSSARY.md` 和 `docs/adr/`；探索项目前按 `docs/agents/domain.md` 阅读相关领域文档。
