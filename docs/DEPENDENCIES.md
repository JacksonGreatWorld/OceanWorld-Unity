# 本地依赖与恢复

需要 Unity **6000.0.84f1** 及匹配的 Android 构建支持。registry 包按 `Packages/manifest.json` 的版本恢复；Unity、PICO SDK 和样例各有自己的许可证。本仓库不会下载依赖、自动接受协议或声称授予供应商授权。

## PICO XR SDK

从 [PICO 官方开发者文档](https://developer.picoxr.com/document/unity/)取得已获授权的 **PICO XR SDK 6.1.1**。恢复脚本接受包含 `package.json` 的 XR 包目录，或其含 `XR/` 的上一级目录；要求名称为 `com.bytedance.pico.xr`、版本恰为 `6.1.1` 且包含 `LICENSE.md`。这些字段检查不替代下载来源与适用授权核验。

本地目标：`Packages/Local/PICO-Unity-SDK-main/XR/`。此目录必须被 Git 忽略。不要把 SDK、SDK 压缩包或 MCP Extensions 推送到公开仓库。

`Assets/Resources/PXR_ProjectSetting.asset`是本应用的非敏感能力配置，随源码保留，以启用手部追踪和入场透视；它通过脚本GUID引用恢复后的SDK，不包含SDK代码或平台账号。不要删掉它后依赖SDK自动生成默认值：默认会关闭手追和透视。另一个`PXR_PlatformSetting.asset`可能包含平台账号、设备与entitlement模拟信息，只供本地使用，必须被Git忽略，不应加入公开快照。

## Unity XR Hands 源模型

从自己已获许可的 `com.unity.xr.hands` 包取得 `Samples~/HandVisualizer/Models/LeftHand.fbx` 与 `RightHand.fbx`。项目来源记录对应 XR Hands **1.7.3** 的 HandVisualizer；已核对该包中两只 FBX 与项目原件完全一致，较早 1.4.1 样例也是相同字节。当前运行时包版本仍为 **1.7.3**。

脚本接受样例包版本 1.4.1 或 1.7.3，但无论版本都要求模型字节严格符合以下已记录原件。其他版本或不同模型会停止，不会悄悄替换：

| 模型 | SHA256 |
| --- | --- |
| LeftHand.fbx | `828996c1e01f710222a90caa3451a2099ec82f0651242a9b4847091a508ec521` |
| RightHand.fbx | `215bd9c9782a74c95b47e08e802bd2e37b9bd0e2205428543dbdef9da7990db3` |

目标：`Assets/Resources/DiverHands/`。仓库仅保留项目 `.fbx.meta` GUID；恢复时不能复制样例自己的 `.meta`。两个 `.fbx` 必须被 Git 忽略，`.fbx.meta` 必须保留。详见[Unity 来源记录](../Sources/DiverHands/SOURCE.md)。

## 显式本地恢复

先关闭 Unity，确保源目录来自可信且有权使用的供应商包。下面的占位目录请换成自己的本地路径：

```sh
python3 Scripts/restore-local-dependencies.py   --pico-sdk '/path/to/authorized/pico-sdk/XR'   --xr-hands '/path/to/com.unity.xr.hands'
```

默认只做完整预检与哈希比对。核对计划后，显式追加 `--apply` 才会复制：

```sh
python3 Scripts/restore-local-dependencies.py   --pico-sdk '/path/to/authorized/pico-sdk/XR'   --xr-hands '/path/to/com.unity.xr.hands'   --apply
```

脚本只写入上述既定本地依赖路径，不改 manifest、不联网、不运行 Unity、不启动应用，也不接受任何协议。显式选择的输入根目录先解析为真实目录，便于使用挂载磁盘上的包缓存；包内部及目标路径的符号链接会被拒绝。现有文件必须逐字节相同；遇到未知文件、版本不符、手模型哈希或项目 GUID 不符时停止，不覆盖。中途因磁盘等原因失败可能留下已完成文件，修复原因后重跑只补齐缺项；不要手工覆盖不明文件。

恢复后再打开 Unity，确认 package resolution、两只手模型、PICO loader 与构建目标；按项目构建入口编译。通过源文件检查不表示构建或实机已验收。

## 已封存素材与重新制作

公开快照包含当前可再分发的运行素材、作者/许可说明及封存的公开准入凭据。构建入口验证公开凭据的固定SHA、输出名单、实际模型/材质/动画和署名；317的鲭鱼体积派生另保留18项原始输入的逐SHA检查，仅通过已封存的native公开投影桥接原始native receipt身份。

恢复SDK与手模型不会替代上述验证，也不要删除校验或伪造receipt。直接使用本快照的封存输出无需携带私有原始ZIP或本机路径记录。若要从原始下载包重新烘焙、换模型或改输出，应从素材来源说明取得适用授权，重新执行完整来源/几何/动画准入；不能把修改后的输出当成原封存结果。

当前验证范围见[验证说明](VALIDATION.md)。恢复依赖、Editor回归、Android构建、设备安装和真人佩戴是分别记录的状态。
