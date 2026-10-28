# 公开版资源范围

本仓库是 OceanWorld Unity 的公开源码导出。当前发行内容只开放第 1–20 关；第 21–100 关保留永久目录和旧存档身份，点击显示“未开放，敬请期待”。素材许可与关卡开放范围是不同事项。

## 保留内容

- 当前扫描礁体、自然岩壁、脑珊瑚、海扇及其 LOD、近景材质、压缩纹理和独立碰撞几何。
- 当前 Polyy.AI 鱼、群游衍生，以及已集成的鲭鱼、蝶鱼、海龟和海豚；保留逐项来源、署名与改制说明。
- 有明确来源的海底纹理、环形气泡、背景音频、OFL 字体和 MIT 拼音字表。
- 原创玩法、场景布局、Shader 和程序化道具代码。

逐项许可证见 [THIRD_PARTY_LICENSES.md](../THIRD_PARTY_LICENSES.md)，不能将全部美术或软件统一视为 CC0。

## 不包含的内容及原因

| 路径 / 内容 | 原因与影响 |
| --- | --- |
| `Packages/Local/` 下的 PICO SDK 与 MCP Extensions | 供应商组件不随本公开源码重分发；使用者须自行取得适用授权并在本地接入。 |
| `Assets/Resources/DiverHands/LeftHand.fbx`、`RightHand.fbx` | Unity 样例源模型不公开复制。项目 `.meta` GUID 保留；关闭 Unity 后按[依赖说明](DEPENDENCIES.md)恢复，构建前仍必须检查两只手。 |
| `Assets/Resources/Environment/`、`Assets/Resources/CoralColored/`及目录 `.meta` | 旧备用资源来源记录不完整，且当前礁体构建入口不使用。相关旧 private 方法保留作源码历史，不用于当前 1–20 关环境；本导出不恢复该旧资源路径。 |
| `Assets/SourceOnly/`、`Assets/ArtBaked/ReefAstcSamples/` | 未用模型、历史样本不进入此公开导出。内容清单的 archive rows 仅记录历史归档政策，不表示这些原件随公开库提供。 |
| `Assets/Resources/Textures/Bubbles/bubble_transparent.png` | 旧文件来源不足；公开导出改用 `Bubbles/bubble_ring_glowCircle_512`。局部微气泡仍可表现，但纹理外观有差异，需要导出版运行时复核。 |
| 原始下载 ZIP、大型研究原件、完整 API/下载回执、内部测试记录与存档 | 正常运行不依赖这些内部证据；这里只保留精简的公开来源/许可说明。 |

`BuildProfiles/first-20-player-content.json` 的 requiredResources 已移除不发布的旧 Environment / CoralColored / 透明气泡条目，仍要求安装后核实 DiverHands。该公开清单不替换原工程清单，也不声称独立于依赖即可直接构建。

公开源码导出的资源检查与原发行 APK 的构建、头显运行、真实佩戴验收分别报告；不得把原 APK 的验收结果直接当成改用气泡纹理或恢复本地依赖之后的验证结果。

## 已烘焙生物的可移植校验

`Sources/MarineExpansion/*-native-admission.json` 是已完成资源准入记录的公开投影，保留87份实际输出的原始SHA256、模型、纹理、姿态和动画测量；本机目录、保护文件清单、下载原件清单与截图路径不公开。它们不是新的采样结果，也不包含原始下载归档。公开构建入口使用 `ValidatePublishedAssets()` 核对封存投影、完整输出清单和既有动画/纹理约束；重跑原始导入的开发入口仍要求自行准备原件，不用公开投影冒充完整下载验证。
