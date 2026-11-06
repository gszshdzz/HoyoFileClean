# 项目协作

## 交付节奏

按模块交付。每个模块完成后，说明改动、提供验证结果和用户可操作的验收方法；等待用户明确验收通过，再开始下一个模块。

当前实施范围为模块 0：本地 Sophon manifest 转 JSON 文件列表；验收通过前不开始扫描、比对、移动或界面模块。自动获取清单优先级极低，留到核心逻辑完成之后，届时参考 Collapse Launcher。`refs/` 中的旧脚本仅作流程参考，不作为正式实现或安全规则的依据；用户提供的 manifest 与 `main.json` 用于真实转换验收。

## Agent skills

### Issue tracker

方案与任务使用本地 Markdown，约定见 `docs/agents/issue-tracker.md`。

### Domain docs

使用根目录 `GLOSSARY.md` 和 `docs/adr/`；探索项目前按 `docs/agents/domain.md` 阅读相关领域文档。
