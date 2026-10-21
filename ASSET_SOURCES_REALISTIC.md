# 扫描珊瑚与海岸岩石来源

以下仅说明本公开库包含的素材来源、许可和改制，不授予 Unity/PICO 软件的额外授权，不表示机构认可本游戏。

## Smithsonian / The Hydrous 珊瑚

| 运行时名称 | 原始机构对象 | 身份 |
| --- | --- | --- |
| coral_cuneata | https://3d.si.edu/object/3d/madrepora-cuneata%3A8b475c34-00db-4046-b020-c5c7bfab0f00 | Madrepora cuneata，USNM 334 |
| coral_spicifera | https://www.si.edu/object/3d/madrepora-spicifera%3Adebebbb8-f7ee-4a6d-941c-12ea01dec71a | Madrepora spicifera，USNM 244 |
| coral_brain | https://3d.si.edu/object/3d/diploria-labyrinthiformis%3A87738412-3acd-45d1-bff4-3ab67093470e | Diploria labyrinthiformis，USNM 74947 |
| coral_palmata | https://3d.si.edu/object/3d/acropora-palmata%3A6fd30c79-3345-4d06-b27c-99d5b320ce5f | Acropora palmata，USNM 3968 |

机构/制作署名：Smithsonian Institution，National Museum of Natural History / Digitization Program Office，与 [The Hydrous 珊瑚数字化协作](https://3d.si.edu/corals)。原媒体公开领域、metadata 标注 CC0；[Smithsonian Open Access 政策](https://www.si.edu/openaccess/faq)允许使用、改制、分享及商业用途。脑珊瑚[机构镜像](https://sketchfab.com/3d-models/diploria-labyrinthiformis-cbbf64f211f84a7cb62887fec11f54fe)和鹿角珊瑚[机构镜像](https://sketchfab.com/3d-models/acropora-palmata-8cb986313c824ac38c373c0fabd0805f)另标 CC0。

这是干燥馆藏标本扫描，不是活体水下原色或科学海域复原。游戏做减面、保 UV/法线处理、底座修剪、居中接地、LOD、材质及压缩适配。被修剪的底部须嵌入地形，不作为闭合物理模型。

## Poly Haven 海岸岩石

| 名称 | 作者 | 原作者来源 |
| --- | --- | --- |
| coast_rocks_02 | Rob Tuytel（摄影/处理）、Rico Cilliers（清理） | https://polyhaven.com/a/coast_rocks_02 |
| coast_rocks_05 | Rob Tuytel | https://polyhaven.com/a/coast_rocks_05 |

许可：[Poly Haven CC0](https://polyhaven.com/license)，允许下载资产的改制和再分发；不包含其网站品牌或预览图片。游戏做减面、LOD、纹理适配、压缩和尺度/位置适配。原岩石扫描有开放边界，不冒称闭合碰撞体。

## 当前文件对应

- `Assets/ArtSources/RealisticReef/`：导入 GLB 网格，须保留 `.meta`。
- `Assets/ArtBaked/ReefAstc/`：对应材质、法线/底色等压缩纹理。
- `Assets/Resources/RealisticReef/`：`coral_*`、`rock_coast_*`各 LOD prefab。

原始大文件、完整 API 回执和内部加工/测试证据不随公开源码提供。本文件不是一键从原始扫描重建当前 prefab/ASTC 的完整管线。其他新岩壁、脑珊瑚和海扇的 CC BY / CC0 来源另见 [MarketReef](Sources/MarketReef/SOURCE.md)。
