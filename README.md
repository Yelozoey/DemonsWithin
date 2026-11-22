# 心魔 / Demons Within

第三人称一对一对战原型。工程包含地空连段、弹反与识破、训练机器人、可视化连招树和动作时间轴。心魔通过行为树执行战斗决策，可选接入 LLM 调整策略。

## 打开工程

1. 用 Unity Hub 安装 Unity **6000.6.8f1**，然后添加仓库根目录。
2. 在 Unity 中通过 **Assets → Import Package → Custom Package** 导入自己有授权的 **Lucy 1.0** 和 **Animancer Pro v8 8.4.0**。保留原始导入路径 `Assets/Lucy` 和 `Packages/com.kybernetik.animancer`。这两份商业资源需自行获取。[Lucy](https://assetstore.unity.com/packages/3d/animations/lucy-238874) · [Animancer Pro v8](https://assetstore.unity.com/packages/tools/animation/animancer-pro-v8-293522)
3. 导入后在仓库根目录运行 `python Tools/restore_lucy_loops.py`，恢复工程对 83 个 Lucy 循环片段的 **Loop Time** 设置。切回 Unity 等待资源重新导入。
4. 打开 `Assets/DemonsWithin/Scenes/PhaseOneArena.unity` 开始对战，或打开 `TrainingArena.unity` 练习连招。

- [战斗操作与编辑器](Docs/CombatAuthoring.md)
- [开发文档](Docs/Development.md)
- [LLM 策略配置](Docs/LlmTactics.md)
