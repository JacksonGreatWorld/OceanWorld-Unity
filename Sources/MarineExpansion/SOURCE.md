# 海洋动物来源

| 模型 | 作者 | 原作者来源 | 许可 |
| --- | --- | --- | --- |
| Atlantic Mackerel (Animated and Game-ready) | Ishxxn (@1shxxn) | https://sketchfab.com/3d-models/atlantic-mackerel-animated-and-game-ready-eca6071c26fc4013b8c94a3f37a434c7 | [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) |
| Butterfly Fish | slleggott (@slleggott) | https://sketchfab.com/3d-models/butterfly-fish-1a5d97ef77004074a6b4277cd7e43200 | [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) |

鲭鱼保留原作者 Generic FBX 骨骼游动曲线，选择 Swim 片段、关闭 root motion，以独立导航根和解剖朝向 wrapper 接入。原始 800×534 图片保留，运行时做边缘填充、UV 补偿、底色 ASTC 与法线通道打包；粗糙度为游戏设定标量，不冒称原始 PBR 贴图。

蝶鱼原件为无骨骼/动画的静态 Collada；本项目做坐标/单位转换、统一尺度、PBR/透明度适配与 ASTC 压缩，游动来自原创 Shader，不是作者提供的游泳动画。

当前导入资源在 `Assets/ArtSources/MarineExpansion/`、`Assets/ArtBaked/MarineExpansion/`和 `Assets/Resources/MarineExpansion/`。完整原 ZIP 与下载回执不随公开库提供。[运行时署名和改制说明](../../Assets/StreamingAssets/MarineExpansion-CREDITS.txt)必须保留；作者不为游戏背书。海龟和海豚见 [COMPANIONS.md](COMPANIONS.md)。
