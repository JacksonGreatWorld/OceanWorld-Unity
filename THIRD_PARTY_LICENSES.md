# 第三方软件与素材许可

此公开源码导出包含不同许可证的软件、字体、数据和美术。**整个工程不是统一 CC0、CC BY 或 MIT 资源包。** 本文不增加任何上游授权；项目原创代码的许可声明不替代各项第三方许可。公开下载也不代表供应商认可本项目。

## 本仓库包含的素材

| 文件范围 | 来源 / 作者 | 许可证及分发要求 |
| --- | --- | --- |
| `Assets/ArtSources/RealisticReef/`、`Assets/ArtBaked/ReefAstc/`及同名 `Resources/RealisticReef` prefab | Smithsonian / The Hydrous 珊瑚扫描；Poly Haven Coast Rocks 02 / 05 | 公共领域 / CC0；[逐项来源](ASSET_SOURCES_REALISTIC.md)。保留来源，不暗示机构背书。 |
| `Assets/ArtSources/MarketReef/`、`Assets/ArtBaked/MarketReef/`及 `market_*` prefab | Mountainside：Dario Barresi / Rico Cilliers；Coastal Cliff 02：Rob Tuytel | CC0；[来源和改制](Sources/MarketReef/SOURCE.md)。 |
| 上述目录中的 `coral_platygyra*`、`fan_coral*` | Brain Coral (platygyra)：QQman；Fan Coral Cluster Low：Valery.Li | CC BY 4.0；保留作者、源链接、许可及改制说明；[运行时署名](Assets/StreamingAssets/ThirdParty/MarketReef.txt)。 |
| `Assets/Resources/Fish/`、`Assets/Resources/SchoolFish/` | Polyy.AI，3D Fish Pack；群游低模为本项目衍生 | 原作者页面标注 CC0 1.0 Universal，同时说明不得把未经改制的原文件包装为自己的模型商品出售。此次为免费整游戏工程分发，不出售独立原始模型包；两项说明均保留，见[来源记录](Sources/SchoolFish/README.md)。AI-assisted graphics 是作者披露，不是生物扫描。 |
| `Assets/ArtSources/MarineExpansion/mackerel/`、`Assets/ArtBaked/MarineExpansion/`及对应 prefab / geometry | Atlantic Mackerel：Ishxxn (@1shxxn)；Butterfly Fish：slleggott | CC BY 4.0；[来源](Sources/MarineExpansion/SOURCE.md)、[署名及改制](Assets/StreamingAssets/MarineExpansion-CREDITS.txt)。蝶鱼原件无动画，游动为游戏程序效果。 |
| `Assets/ArtSources/MarineCompanions/`、`Assets/ArtBaked/MarineCompanions/`及对应 prefab / geometry | Sea Turtle：Eloi (@Eloiart)；Game-Ready dolphin (Swimming)：Allie2k | CC BY 4.0；[来源与改制](Sources/MarineExpansion/COMPANIONS.md)、[运行时署名](Assets/StreamingAssets/OceanMarineCompanions-credits.txt)。 |
| `Assets/Resources/Textures/Sand/Ground101_*`、`Textures/Rock/Rock019_*` | ambientCG，Ground101 / Rock019 | CC0；https://ambientcg.com/a/Ground101 、https://ambientcg.com/a/Rock019 、https://docs.ambientcg.com/license/ 。 |
| `Assets/Resources/Bubbles/bubble_ring_glowCircle_512.png` | oglsdl，OpenGameArt Glow Circle | CC0；https://opengameart.org/content/glow-circle 。 |
| `Assets/Resources/Audio/underwater_ambient.wav` | cynicmusic，Aquaria / TylerSong3_Normal.wav | CC0；https://opengameart.org/content/aquaria 。 |
| Loading / Treasure / Leaderboard 字体 | Noto Sans CJK SC，Adobe / Noto 项目 | SIL OFL 1.1；保留原版权、完整 OFL 和衍生名称；[加载字体记录](Assets/ThirdPartyNotices/OceanLoadingFont-SOURCE.md)、[Treasure](Assets/Resources/Treasure/FONT-SOURCE.txt)、[Leaderboard](Assets/Resources/Leaderboard/SOURCE.md)。 |
| `Assets/Resources/Loading/OceanTitleFont.ttf` | Ma Shan Zheng / MaShanZheng Project Authors；Ocean Title 衍生 | SIL OFL 1.1；[来源及完整许可](Assets/ThirdPartyNotices/OceanTitleFont-SOURCE.md)。 |
| `Assets/Resources/Leaderboard/PinyinCandidates.txt` | mozillazg/pinyin-data | MIT；保留[版权和许可](Assets/Resources/Leaderboard/Pinyin-LICENSE.txt)及[来源](Assets/Resources/Leaderboard/Pinyin-SOURCES.md)。 |

CC0 原文：https://creativecommons.org/publicdomain/zero/1.0/ 。CC BY 4.0 原文：https://creativecommons.org/licenses/by/4.0/ 。CC BY 资源在本工程经过坐标/统一尺度转换、LOD、材质、纹理压缩或动画适配；具体差异见对应来源及运行时 credits。不得将作者署名替换成项目作者，也不得暗示作者或机构代言。

项目内贝壳、渔枪、金币、教学图形、水流，以及航行/遗迹/任务道具的几何为项目程序生成；幻想巨鲲使用已列出的 Polyy.AI 蝠鲼衍生，并非独立真实物种扫描。品牌图标由项目提供。未包含未采购的候选素材包或供应商网页预览图。

## 单独获取的依赖，不在公开仓库分发

- **PICO XR SDK 6.1.1 / PICO MCP Extensions**：供应商保留全部权利；现有通知限制未经书面许可的传播与复制。此仓库不含 SDK、插件副本或其调试资源。请从官方渠道取得适用授权，见[依赖安装](docs/DEPENDENCIES.md)。使用公开 API 的项目适配代码不等于再次发布 SDK 源码。
- **Unity XR Hands HandVisualizer 样例手模型**：受 [Unity Package Distribution License](https://unity.com/legal/licenses/unity-package-distribution-license) 管辖，不是 CC0。该许可允许按其条件随项目分发集成二进制；此源码导出不含两只 FBX。仅保留项目自己的 `.meta` GUID 与[来源/许可通知](Sources/DiverHands/SOURCE.md)，由使用者从已获许可的本地 XR Hands 包恢复。
- Unity Editor 和 registry 包继续遵循各自条款；这里只保留包版本引用，不发布 PackageCache 或 Unity 安装程序。

## 导出差异

来源不足或当前未使用的旧 Poly / MiniPoly 资源、旧备用岩石、未用源模型和历史烘焙样本均不包含。泛来源旧透明气泡图也不包含；本导出代码使用上表有明确来源的 ring 贴图。完整目录与视觉差异说明见 [ASSETS.md](docs/ASSETS.md)。
