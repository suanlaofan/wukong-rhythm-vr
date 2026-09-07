# 悟空节奏 · Wukong Rhythm VR

<img src="Assets/WukongRhythmGame/UI/AppIcon/WukongAppIcon.png" width="144" alt="悟空节奏图标">

使用金箍棒随音乐击打石球的 Unity / PICO VR 游戏。当前版本 **1.1.0**。

[下载 APK 与查看发布记录](https://github.com/suanlaofan/wukong-rhythm-vr/releases/latest)

- 音乐 DSP 时间统一驱动石球、预警圈和节拍评分。
- 支持挥棒、投掷、连击、暂停、四拍恢复及节奏补偿校准。
- 21 首曲目；右侧面板正对玩家，顶部按当前状态显示简短操作提示。
- APK 使用游戏已有的金色节奏徽章图标，包含传统、圆形及自适应图标。

## 操作

| 功能 | PICO 实体手柄 | PICO 0.13 模拟器默认映射 | Unity 桌面 |
| --- | --- | --- | --- |
| 选曲 | 右摇杆上下 | I / K | ↑ / ↓ |
| 开始、继续、重玩 | 右 A | 空格 | Enter（结算也可 R） |
| 击打 | 挥动右手 | 鼠标移动虚拟右手 | 鼠标移动，左键 / 空格击打 |
| 投掷 | 右 A | 空格 | T |
| 暂停 | 右 B | Delete（Mac 为 Fn + Delete） | Esc |
| 暂停后返回选曲 | 长按右 B | 长按 Delete | 长按 Esc |
| 中英切换 | 左 X | X | L |
| 暂停时校准 | 右摇杆左右 | J / L | ← / → |

模拟器底部选择 **右手柄模式**，左侧开启 **手柄按键模式**。表格中的模拟器键是默认映射；若修改过模拟器设置，以其“手柄设置”为准。游戏中的提示文字不是可点击按钮。

## 构建与验证

Unity **6000.5.4f1**；Android ARM64 / IL2CPP / Vulkan，PICO XR SDK。

- 场景：`Assets/Scenes/SampleScene.unity`
- 菜单：`Wukong Rhythm > Validation > Run Timing Regressions`
- 本地 APK：`Wukong Rhythm > Build > Build Diagnostic APK`
- 命令行入口：`WukongReleaseBuilder.BuildDiagnostic`
- 输出目录环境变量：`WUKONG_BUILD_DIR`；规则报告目录：`WUKONG_AUDIT_DIR`

本次规则检查 7,586 项通过；已在 PICO 0.13 模拟器实测开始、暂停、继续、返回选曲以及音乐输出。此前 Unity Play Mode 已覆盖投掷、扫掠碰撞、鼠标跟随和命中音效。

GitHub APK 沿用现有测试签名及应用标识，以支持覆盖升级。**Swan 真机的平台签名验收和真实性能仍待实测**；模拟器通过不能替代真机验收。全部曲谱尚未逐首完成人工听感审核。

[本次修改与验证说明](Documentation/Remediation/Release-1.1.0.md)
