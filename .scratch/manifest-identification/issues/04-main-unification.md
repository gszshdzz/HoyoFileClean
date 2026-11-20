# 04 主线验收纠正与实质统一

编号：`8,9->7.5`。基点：`faedaee`。用户指出初次合并没有充分统一配置、风格和策略，并确认游戏列“有 biz 用 biz，没有用 game”；两种格式都应从文件字段取得线索，共用能复用的逻辑。

状态：修正、自动验证及两轴复审完成，用户已授权提交。提交标题：`统一识别规则结构与结果处理并修正清单详情交互`；编号 `8,9->7.5`。

## 修正

- 移除 `GameName_*` 手写中文映射，游戏列优先原始 biz、否则原始 game；biz 冲突不能被 game 掩盖。
- 详情默认焦点在关闭按钮，文字不默认选中；文件表格不保留选择，但保持可用和滚动。
- 两份规则文件统一为 `GameIdentificationRules.cs`／`ManifestIdentificationRules.cs`，同一 `Create()`、`IdentificationConfiguration<TDetection>`、`Detections`／`Results` 结构。配置只定义数据，不创建引擎；移除旧 manifest 扁平配置与转换路径。
- 共用 `IdentificationResultRule`／`IdentificationResultDefinition` 和 `IdentificationEvaluator`：真实线索→条件映射→值去重→证据归并→必要项／单值／多值／冲突保留约束；格式读取仍各自负责安全读取、正则或清单字段选择。
- 保留原始扫描记录和默认 manifest JSON 契约；显式多值规则记录可选 `multiple: true`，默认省略。

## 审查中修复

- Standards 首轮：文案 ID 投影丢失为 null；manifest 再次把合法多值当冲突。
- Spec 首轮：`Source` 取得命中占位符而非字段原值；文案 ID 丢失。
- 已改为 manifest 明确拒绝非原始值输出、保存实际字段线索、传递多值策略且复用同一冲突判定。纯存在性规则首命中即停，不收集全部路径。

## 验证

先增加用户症状回归，在旧实现运行桌面测试得到 7 项失败：6 项原始标识断言失败，1 项上方文字默认选中全部 268 字符。修正后这些测试通过，另覆盖主动选单元格／全选不残留选择。

共用逻辑测试覆盖两种实际读取路径使用相同规则、同值去重、证据 ID、严格冲突、保留冲突、合法多值、动态插件线索、实际 Source 字段、多值不被重新判冲突、配置快照隔离、文案 ID 拒绝，以及十万条匹配／不匹配路径的分配量上限。测试不删除或跳过原有安全回归。

最终验证数字与人工验收见 `docs/history/identification-merge-review.md`。

本轮最终输出：核心总计 416，409 执行通过、7 显式未运行；桌面总计 156，155 执行通过、1 显式未运行；错误、失败、跳过均为 0。CLI／桌面构建均为 0 警告、0 错误。Standards／Spec 复审剩余问题均为 0。
