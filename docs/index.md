# 文档导航

## 使用

- [项目首页](../README.md)
- [源码启动与桌面使用指南](usage.md)
- [命令行说明](cli.md)
- [清单、扫描和比较报告格式](cli.md#数据格式)
- [领域术语](../GLOSSARY.md)

## 开发与维护

- [配置与架构维护](configuration-maintenance.md)
- [游戏识别规则](game-identification.md)
- [manifest 识别规则](manifest-identification.md)
- [自动获取规则](automatic-manifest.md)
- [测试入口与临时文件](testing.md)
- [与 AI 协作](development-workflow.md)：面向指导开发的人，不是工具使用前提。
- [第三方声明](../THIRD-PARTY-NOTICES.md)

## 研究与历史

- [Sophon 构建清单研究](research/sophon-manifest.md)
- [预下载／补丁清单研究](research/sophon-pre-download-manifest.md)
- 历史审查：保留特定日期／提交的证据，不代表当前数量、状态或授权；反引号项目路径以仓库根目录为基准。
  - [硬编码盘点与用户意见](history/hardcoded-data-review.md)
  - [两类识别合并](history/identification-merge-review.md)
  - [三分支及文件操作整合](history/worktree-integration-review.md)
  - [预置排除与补丁整合](history/preset-patch-integration-review.md)
  - [自动获取交付](history/automatic-manifest-delivery.md)
  - [结果选择整合](history/result-selection-integration-review.md)
  - [测试必要性与耗时](history/testing-assessment.md)
  - [技能安装验证](history/skill-installation.md)
- [提交与 session 对照](commit-sessions.md)

研究只代表记录时的证据；当前行为以对应指南和代码为准。历史任务保留在 `.scratch/`，不作为当前操作手册或新任务授权。

## Agent 入口

执行规则只放在 [AGENTS.md](../AGENTS.md) 和 `docs/agents/`；技能手册在 `.opencode/skills/`，与人类使用说明分离。
