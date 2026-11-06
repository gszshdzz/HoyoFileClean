# Issue tracker: Local Markdown

方案与任务保存在 `.scratch/<feature>/`。

- `spec.md` 保存方案。
- `issues/<NN>-<slug>.md` 每个任务一份文件，从 `01` 编号。
- 相互依赖通过 `Blocked by: NN` 记录。
- 发布到任务平台即写入上述本地文件，无需联网。
