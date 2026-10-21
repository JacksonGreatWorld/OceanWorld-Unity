# 海龟与海豚来源

| 模型 | 作者 | 原作者来源 | 许可 |
| --- | --- | --- | --- |
| Sea Turtle | Eloi (@Eloiart) | https://sketchfab.com/3d-models/sea-turtle-23dcb315dea44f5082b020b04710bd31 | [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) |
| Game-Ready dolphin (Swimming) | Allie2k | https://sketchfab.com/3d-models/game-ready-dolphin-swimming-b621ec8894734bc79c04338edff3cd0d | [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) |

保留原始 FBX，另建游戏材质和循环动画：海龟选取重复游泳区间，仅将已识别的非 root 身体 Y 位移曲线固定为区间起点以实现原地游动；海豚选择重复区间。两者使用静态解剖朝向/统一尺度 wrapper、独立导航根、ASTC 贴图和法线通道转换；海龟粗糙度转 smoothness，海豚缺少原始粗糙度贴图，使用显式材质标量。没有叠加旧鱼类侧摆 Shader。

模型和原始贴图在 `Assets/ArtSources/MarineCompanions/`，运行时动画/材质在 `Assets/ArtBaked/MarineCompanions/`，prefab 和导航几何在 `Assets/Resources/MarineExpansion/`。原始下载 ZIP、API 信息和内部验收记录不公开。作者不为项目背书。

[运行时署名](../../Assets/StreamingAssets/OceanMarineCompanions-credits.txt)必须随模型衍生分发。海龟、海豚不应描述为鱼类。
