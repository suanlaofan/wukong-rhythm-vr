# 悟空节奏 0.2.0 整改实施记录

> 此文件保留早期验收快照。当前 1.1.0 的谱面、UI、模拟器和图标结果见 [Release-1.1.0.md](Release-1.1.0.md)，旧的“未在模拟器测试”不代表当前状态。

日期：2026-09-07。原工程：`/Users/liuzhenjia/Documents/Codex/2026-08-04/wo/outputs/WukongRhythmVR`。分支：`codex/wukong-rhythm-remediation-20260907`。起点：`69190a1e799dd12fcd6d8554e0fb60d72cc0f5ea`。

本轮完成节奏核心、曲谱时间修正、已识别的运行开销优化及发布检查。Swan 真机启动、原约 45 FPS 的瓶颈归因与整改后帧率，仍待设备验收。未向其他会话发送消息。

## 已实施

- 判定使用统一音乐 DSP 时间：Perfect ±70 ms、Good ±140 ms。过早接触不计分，过晚或漏击记 Miss；一次挥棒最多消费一个音符，重复回调不重复计分。
- 对棒端和棒身中心进行相对扫掠，插值估计帧间接触时间；先处理本帧有效接触再结算超时。这里是采样间插值，仍需真实控制器快速动作验收。
- 石块按音乐时间前进并穿过打击点。固定目标圈与收缩圈在目标时间重合；仅展示临近音符的光圈，减少重叠。
- 计分准确度改为节奏准确度：`(Perfect + 0.65 × Good) / 已结算音符数`；结算页使用整局音符数。Good 显示偏早或偏晚。
- 增加暂停、四拍恢复准备、摘戴／失焦／音频配置变化处理。重新排程音乐并清除输入历史，降低恢复瞬间误击；追踪丢失时停止判定。
- 暂停页支持左右拨杆／方向键以 10 ms 步长调整输入补偿，范围 ±250 ms，保存在本机。正值表示把输入映射到更晚的音乐时间。
- 21 首、4,199 个音符新增独立秒数修正。4,158 个音符匹配到附近自动检测的音频起音；其余保留原时间。8 首自动相位一致性较高的曲目细化 BPM；其余保留旧 BPM。全部标记为**未听感验收**，并锁定以防自动生成覆盖。详细结果见 `chart-audit.json`。自动检测可拾取音效或非主拍，不能替代逐首听音审谱。
- 预热 48 个石块和预警圈，复用共享材质；16 组粒子池替代逐次生成的刚体碎片、灯光和粒子对象。粒子使用轻量网格；HUD 数值变化时才更新文字。关闭 Mobile HDR，保留原 0.8 渲染比例、Multiview 和其他 XR 设置。
- 构建器不再强制重置模板包名；新增验证／发布两种入口。正式发布会检查登记包名与已有自定义签名配置。输出 SHA-256 和构建元数据，验证包关闭 Development／Script Debugging／自动连接 Profiler。
- 修复 PICO 包内 `CompositionLayers.meta` 已存在的合并冲突标记，保留 Unity 当前使用的 GUID。

## 已验证

- Unity 6000.5.4f1：8,465 项断言通过，覆盖判定边界、快速扫掠、重复消费、45/72/90 Hz 下相同接触时间的判定一致性，以及全曲库时间范围／排序／池容量。**这是规则测试，不是设备帧率测试。**
- 实际 SampleScene Play Mode 的 9 项检查通过：音乐启动、音乐与谱面时间、暂停冻结、恢复准备与重新同步、100 次特效调用对象总量不增加、音频重配暂停、返回选曲清理。
- 该次 Play Mode 内部时钟差：启动约 21.3 ms，恢复小于 1 ms；此数值比较 Unity 内部时钟，**不包含声卡输出、头显显示或用户感知延迟**。
- 特效压力检查后：16 个粒子系统、0 个刚体，灯光总数保持 19。不能据此宣称 45 FPS 已提升至 72/90 FPS。
- 已查看最终 Mobile 画质的实际选曲、游戏／暂停画面，检查场景 missingScripts=0、粒子模式 Mesh；打包后 196 个相关源码／资源文件哈希未变。桌面 Game View 的右侧原 HUD 在当前比例下仍有边缘裁切，需头显双眼视场进一步验收；未将其判为完整设备 UI 通过。
- 临时 Unity MCP 使用本任务独立本机端口进行结构化操作；交付前已从项目移出临时工具副本，避免把本机调试连接带入源码依赖。

## 签名和设备边界

旧 APK 的 Android 签名验证通过，但它采用 Android Debug 证书、模板包名且可调试。这些是发布配置问题，**尚不能证明它们就是 Swan “非法签名”的具体拒绝原因**。当前 PICO 启动授权检查关闭，未擅自启用模拟授权或绕过校验。

本机仅发现 `emulator-5554`，没有 Swan 真机。未安装或启动本轮 APK 到该模拟器，也没有模拟器性能结论。正式包名、PICO 后台登记证书、受测 APK 哈希及 Swan 错误阶段／系统日志尚缺。

本轮验证包继续保留原包名，使用当前本机验证签名，**不是提交商店或平台验收的正式发布包**。需确认已有登记身份后，使用对应证书构建正式包；不得随意新建证书替换已发布应用身份。

## 复验入口

Unity 菜单 `Wukong Rhythm/Validation/Run Timing Regressions` 运行规则和曲库检查。Play Mode 流程验收可在实际 SampleScene 进入运行后调用 `WukongPlayModeValidation.Run()`；该代码以 `UNITY_EDITOR` 排除在 Player 外。

本地构建：Unity `-batchmode -quit -buildTarget Android -executeMethod WukongReleaseBuilder.BuildDiagnostic -projectPath <项目>`。可使用 `WUKONG_BUILD_DIR` 指定输出目录、`WUKONG_AUDIT_DIR` 指定验证报告目录。正式包使用 `BuildRelease`，先在本机 Unity 中配置登记包名及现有 keystore；密码不要写入仓库或报告。

APK 用 Android 官方 `apksigner verify --verbose --print-certs` 校验，并核对 `aapt2 dump badging` 的包名、版本、ABI 和 debuggable。Android 验签通过仍需与 PICO 后台的登记证书逐项比对，再做 Swan 安装／覆盖升级／冷启动与日志验收。

参考：[Unity DSP 排程](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource.PlayScheduled.html)、[Android apksigner](https://developer.android.com/tools/apksigner)、[Android 应用签名](https://developer.android.com/studio/publish/app-signing)。

## 下一步验收

1. 在 Swan 核对型号／固件、包名、APK 哈希、登记证书和拒绝发生阶段，采集系统日志。完成合法正式签名包的安装、升级和冷启动。
2. 用真实手柄检查早击／正拍／晚击、快挥、投掷、停留不挥、失追踪和暂停恢复，并通过有线或设备扬声器进行听感补偿校准。
3. 逐首听音审谱，优先验收一首样板和开头／中段／结尾；审核后才设置 auditoryReviewed。
4. 采集真机 CPU/GPU 帧时间及掉帧数据，确认约 45 FPS 的原因。以实际刷新率预算验证 30 分钟稳定运行，之后再决定分辨率、FFR、灯光和场景批次的进一步调整。

## 本轮 APK 结果

- 文件：`WukongRhythmVR-0.2.0-validation.apk`，185,892,501 字节（约 177.28 MiB）。
- Unity `Build Finished, Result: Success.`；版本 0.2.0，versionCode 2，ARM64，debuggable=false。
- Android 数字签名验证通过，16 KB ZIP 对齐检查通过；ZIP 对齐不等同于所有 ELF 或设备兼容性验证。
- SHA-256：`0a1e0ac4344e02ba96ed5eaffab0d9ccdc8dabcd30562ecccf805fca3db21c76`。
- 签名证书 SHA-256：`1002e7ec219e2f238e4cf33b65f71188b02564d7893fd39785e26a78d495946e`，与历史测试包一致，仍为 Android Debug 证书。
- 保留原模板包名 `com.UnityTechnologies.com.unity.template.urpblank`。正式发布检查会拦截该身份；本轮不以验证包代替正式发布包。
- 详情见 apk-verification.json、apk-signature.txt、apk-manifest.txt 和 apk-alignment.txt。
