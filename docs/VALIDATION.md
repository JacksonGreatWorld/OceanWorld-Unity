# 2.9.20 / code313 公开源码同步记录

2026-10-28。本次同步前20关的玩法、场景表现、生物运动与中英文 UI。公开导出沿用素材许可裁剪、本机依赖恢复和关闭排行榜的约束。原有验证记录保留在下方，历史检查数不当作本轮结果。

## 本轮输入与验证边界

- 原开发输入已完成真实 Unity IL2CPP / ARM64 与 Gradle release 构建；0错误、18警告。15份原生库与 metadata 配对、CRC、签名延续、权限不增、ZIP对齐及13项打包排除核验通过。
- 下方支线说明和性能面板已在真实 Unity UI 复现旧问题：修复后同一608项检查通过。独立主任务条矩阵63,381项检查通过，未为它修改生产代码。完整构建前回归及3,251项语言检查通过；计数含状态、字符与布局断言，不是独立用户动作次数。
- 原开发313体验包已保留数据覆盖安装，外部可读取的11份旧文件在首次启动前逐字节保留；内部受保护目录未完成字节核验。用户后续反馈“基本ok”。这不是每关完整验收、双目舒适性或持续性能达标证明。
- **本公开副本具有独立差异**：未附带来源不足的旧 Environment / CoralColored 资源和透明气泡纹理，保留公开气泡替代；未附带 SDK 和手模型原件；配置与历史网络端点仍保持脱敏。原开发APK与本公开源码不能声称字节一致。
- 本公开副本已在独立工程完成Unity编译及完整Editor回归，实际入口返回0并记录 `OCEAN_REGRESSION_CHECKS_PASS`；13项内容排除、语言规则及公开配置门禁通过。三个生物公开投影对应的87份输出保持原始SHA，既有模型、动画和纹理约束全部通过；不重跑未附带的原始下载归档验证。验证前按[依赖说明](DEPENDENCIES.md)恢复本机依赖，所有日志与测试原件保存在仓库外。
- 本次不更新 VibeLab 2.9.13 / code306 的固定 Gradle 提审输入，不改变该次审核状态，不安装或操作其他设备。

公开副本的实际PlayMode补测也已通过：同一608项任务文字与性能面板检查通过（外层报告610项，不能叠加）；已查看中文HUD与英文完成态说明截图。微粒生命周期/固定世界预算1,154项，以及六个封存报告/输出文件篡改拒绝用例通过；负例只在隔离副本执行并逐字节恢复。截图读数为Editor值，本轮没有为公开变体生成新的APK或进行头显验收。

公开副本验证过程保留了三次前置失败：隔离复制漏掉SDK的Validation子目录、公开仓缺少可移植准入清单，以及将PlayMode微粒生命周期检查误放在EditMode。分别恢复完整本机依赖、增加脱敏封存清单、将生命周期检查放回PlayMode；未删除失败证据、修改原动画检查或放宽失败阈值。

公开复查入口：`SwanUnderwater.Editor.CodexBuild.RunRegressionChecks`。运行前确认排行榜关闭、无 `PrivateRelease` 配置、Git LFS 已完整还原。内容策略为13项排除；公开导出的旧归档采用 provenance-only，不把缺失的历史原件当作已随仓库提供。

Editor截图和面板读数不等于PICO双眼画质或帧时。ZIP对齐不等于全部供应商原生库支持16KiB页。真实手势、双目文字、连续游动与不同系统Locale仍需在设备上继续验证。

---

# 公开源码验证记录

## 历史源码候选：2.9.11 / code304

本公开源码副本已完成Unity编译及完整Editor回归。`public-regression-r1.log` 记录 `OCEAN_REGRESSION_CHECKS_PASS` 与成功退出；本次回归进程退出码为0。独立Android Gradle打包工程随后完成code304本地基线构建（Gradle退出码0）。**最终预编译APK仍在核验；没有将该APK字节绑定到此公开源码提交，安装与真实佩戴也未完成验收。**

| 当前检查 | 已核实结果与边界 |
| --- | --- |
| Unity源码编译、完整Editor回归 | 通过；入口 `SwanUnderwater.Editor.CodexBuild.RunRegressionChecks`。 |
| 排行榜关闭检查 | `OceanLeaderboardDisabledChecks.RunAll()` 通过1,212项，使用独立临时合成历史验证菜单、旧动作、无身份/请求任务、本地满旧队列计分、去重与成就。 |
| 公开发布配置与Android设置 | `OceanPublicExportChecks.RunAll()` 通过10项，实际执行关闭版Guard：无配置时输入检查通过，空私有目录或有效地址配置均被拒绝，关闭时不解析注入URL。 |
| 舒适设置与菜单UI | `OceanComfortVisualTests.RunAll()` 通过4,244项，覆盖实际UGUI、字体边界与生命周期；不代表头显观感或帧率。 |
| 独立Android Gradle基线 | code304本地构建成功；它是独立打包工程的结果，不等于此公开Git提交与APK逐字节对应。 |
| 最终预编译APK、安装、双目与佩戴 | APK仍在核验，其余尚未完成，不标记通过。 |

上述两个排行榜相关入口均已由本次完整回归真实调用，无需将历史联网/改名测试强行运行于关闭模式。原始日志保存在仓库外，不作为公开源码或APK资源上传。

- 合入排行榜关闭策略：菜单与昵称入口不再提供，客户端不创建身份任务、签名任务或网络请求；保留旧本地历史供收藏与成就使用。
- 新的本地收集继续记录累计事实与去重，不增加待上传事件，旧队列满也不应阻塞本地玩法。
- 保留公开版原有配置解析和身份冲突保护；关闭模式不解析地址，Android构建必须不含 `PrivateRelease` 配置。历史联网测试在此模式不运行，改为 `OceanLeaderboardDisabledChecks.RunAll()` 与公开发布检查。
- 近鱼运动、海草受影及性能面板直接入口取自已经提交的主线；沙地、礁石、鱼材质及场景美术不包含正在试验的改动。现有图像均保留原版本标签。
- 复查入口：`SwanUnderwater.Editor.CodexBuild.RunRegressionChecks`，以及菜单 `OceanWorld/Validation/Disabled leaderboard release`、`OceanWorld/Validation/Public leaderboard release`。实际场景可在入场、拾取和图鉴交互后调用 `OceanLeaderboardDisabledChecks.CheckInScene`，这些检查仍不代替真实手追、双目或持续性能验收。

以下为2026-10-21历史联网实现的记录，**不适用于当前关闭排行榜的2.9.11发行行为**。保留记录用于说明旧代码与迁移防护的来历，不能将旧检查数、服务连通结果或装机记录当成本候选的新结果。

---

# 公开快照验证记录

日期：2026-10-21。范围：PICO Unity前20关公开源码的联网排行榜修订。当前开放1–20关，21–100关保留永久身份和入口，但未开放。

## 当前联网修订

| 检查 | 实际结果 |
| --- | --- |
| Unity6000.0.84f1独立导入与C#编译 | batchmode/nographics正常退出，C#编译错误0 |
| `OceanPublicExportChecks.RunAll` | 首轮完整缺配置检查**50项通过**，`missingConfigurationChecks=True`；后续新增Android类型/宏与PICO能力门禁，在独立Android导出源中实际**51项通过**，`missingConfigurationChecks=False`（私有配置存在，4项缺配置子检查未重复执行） |
| Android实际编译与项目回归 | 匹配的Android模块预检退出0，C#编译无错误；正式导出前的`OCEAN_REGRESSION_CHECKS_PASS`已出现，素材来源门禁保留 |
| 原领域回归 | **228项通过**，含账本、ACK、历史、拼音与签名；独立C#环境，不代替头显运行 |
| 私有配置脚本 | **21项通过**，含默认预检、显式写入、0600权限、幂等、拒绝覆盖、符号链接与危险URL负例 |
| 私有发布内容禁入 | **6项通过**，PrivateRelease及其元数据拒绝进入公开目录，合法说明/源码仍允许 |
| 独立代码审查 | 已修复评分或发现单独历史的身份保护，以及迁移/离线状态提示优先级；无剩余代码审查阻塞 |

完整缺配置检查前，仅将本轮新建的私有部署JSON暂存到仓库外；检查后按原SHA256与权限恢复，未改写其字节，也未读取或改写玩家存档。配置文件与本机SDK、手模型、Library缓存和验证原件均不进入公开Git。

测试包含：有效积分1/2/10、重复拾取防重、中文昵称待服务端确认、缓存游标冷启动、两个持久队列不伪造ACK、缺配置可离线记录、同一签名身份冷重启、旧随机身份冲突保留全部原件、仅有旧发现档时不提前创建评分档、未配置/无效配置阻止正式Android构建。Editor测试身份在创建实际请求前拒绝连接非loopback服务。

后续Android检查修复了旧脱敏误替换的三项设置：多线程渲染与IL2CPP保留正确数值类型，PICO宏恢复；公开包名占位值保留。另恢复不含账号或凭据的应用`PXR_ProjectSetting.asset`和原GUID，以保留手追与入场透视。平台账号/entitlement模拟设置继续忽略并禁止公开。此前仅在Mac编译通过并未覆盖这些Android配置问题。

## 只读服务连通检查

使用已有可信CA并保持证书链与目标校验，能力接口返回**HTTP200**；系统curl默认TLS也返回HTTP200。首次Python检查因本机CA集合为空而失败，保留失败原件后重新核对，未绕过验证或修改服务器。

本轮没有向正式服务提交昵称、积分、发现或任何测试事件。能力接口可达不能替代真实设备签名上传、中文重名冲突、多页排行及断网重试的端到端验收；这些仍待重建后在设备上验证。

## 内容与已有基线

- 原内容检查`OceanPlayerContentBuildChecks.RunAll`已通过1,143项断言；本轮未改美术、模型、场景布局或前20关资源清单。
- 12条旧资源身份（路径/GUID/SHA256）仅作来源记录，原始归档在公开快照省略；构建仍拒绝SourceOnly或已排除旧Resources依赖进入APK。
- 当前图鉴开放30类可观察生物，保留31个历史永久身份，不是30种鱼。服务端历史目录分母与当前开放口径分别处理。
- 依赖恢复脚本已有10项正反例，实际预检核对过SDK与手模型共1,850个文件；供应商载荷与手模型原FBX不公开分发。
- 首轮公开快照的强离线合同（16项独立检查、26项Unity检查）属于历史实现，已被本次联网合同取代，不能作为现在网络能力的证据。
- 原导入曾因缓存盘空间不足失败、外置盘解包过慢主动中止；失败原件保留。后续独立缓存导入成功。编译仍有供应商过时API等警告，未修改供应商代码或屏蔽警告。

## 构建与体验边界

本公开联网副本已进入独立Android Gradle正式导出，**此记录尚未确认生成新APK**。匹配的Android模块位于Unity.app同级的`PlaybackEngines/AndroidPlayer`，此前“未安装Android Build Support”的路径判断不准确。原20关code301的Player缓存虽与旧APK指纹相符，但不是本次正式导出或新源码构建，不能替代IL2CPP强制预编译与最终Gradle终验；仓库不附带它。

VibeLab云端不直接构建此Unity源码仓库：须在本机导出独立的Android Gradle工程，完成IL2CPP预编译与`:launcher:assembleRelease`终验，再使用单独仓库提交。源码公开、Editor检查和表单素材保存均不能代替该流程；以[PICO官方提交要求](https://developer-cn.picoxr.com/document/distribute/pico-vibelab-requirements/#7bfed0c3)为准。

本轮未做PICO/Quest真机佩戴、GPU/双目特效、持续帧时或新包真实联网同步验收，不承诺固定帧率。联网发行还需要在私有构建环境恢复服务配置；VibeLab草稿保存不等于构建、审核或发布完成。

## 本地复查

安装依赖后，可运行Unity菜单 `OceanWorld/Validation/Public leaderboard release` 与 `OceanWorld/Validation/First twenty player content`。若项目已配置服务，缺配置子检查会明确标记未执行，不能照搬本次完整50项结果。

公开检查器应扫描干净的待提交内容树；报告写到仓库以外。私有配置、供应商载荷、缓存和玩家原件都不应加入该树：

```sh
python3 Scripts/check-public-release.py --help
```

完整联网配置与当前未验边界见[联网排行榜说明](LEADERBOARD.md)。
