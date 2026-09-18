# Boss 清单（BossChecklist）

TerrariaModder 模组：右侧的 Boss 清单侧栏，外加一本可翻阅的 Boss 日志本，界面复刻 tModLoader 版 Boss Checklist。

仓库按语言分成两套，源码与成品分开放：

```
boss-checklist\
├─ en-US\   英文版源码：Mod.cs、LocalizationData.cs、csproj、manifest.json、icon.png、README.md
│  └─ bin\  英文版成品：BossChecklist.dll、manifest.json、icon.png、README.md（直接复制进 TerrariaModder\mods\boss-checklist\）
├─ zh-CN\   中文版源码（同上）
│  └─ bin\  中文版成品
├─ assets\*.rawimg                 贴图（编进 DLL，两个语言版共用）
├─ Content\*.cs、GameRefs.cs、UI\*.cs   与语言无关的源码（两个版本共用）
├─ Directory.Build.props / .targets      本机路径配置、安装到 TerrariaModder 的逻辑
└─ README.md
```

带界面文字的源码（Mod.cs、LocalizationData.cs）在 `en-US\` 和 `zh-CN\` 里各有一份，其余源码与贴图共用同一份。

## 编译

```powershell
dotnet build zh-CN\BossChecklist.zh-CN.csproj -c Release                     # 中文版，成品在 zh-CN\bin\
dotnet build en-US\BossChecklist.en-US.csproj -c Release                     # 英文版，成品在 en-US\bin\
dotnet build zh-CN\BossChecklist.zh-CN.csproj -c Release -p:DeployMod=true   # 编译并装进 TerrariaModder\mods\
```

需要 Windows、[.NET SDK](https://dotnet.microsoft.com/download)、Terraria 1.4.5、已安装的 TerrariaModder，另外还需要：

- tModLoader 的 `Libraries/ReLogic/1.0.0/ReLogic.dll`
- XNA 4.0 可再发行组件（`Microsoft.Xna.Framework*.dll` 在系统 GAC 里）

路径在 `Directory.Build.props` 里改，或者建一个不入库的 `local.props` 覆盖。

## 说明

- 所有「是否已击败」的判定都取原版字段（`NPC.downedXxx`、`Main.hardMode`、`DD2Event.DownedInvasionT3` 之类），没有硬编码的存档数值；原版没记的几个事件标记（血月、霜月、南瓜月、日食等）由本模组补记
- 掉落表来自原版 `Main.ItemDropsDB.GetRulesForNPCID`，腐化 / 猩红专属、专家限定的掉落会按当前世界和难度自动区分；醉酒世界里两种邪恶地形同时存在，两边的掉落都算可获取
- 「拿到过哪些掉落」在物品拾取钩子里记录，捡起来之后放进箱子、卖掉、丢掉都不会丢，另外每隔几帧扫一次背包与装备栏兜底
- 界面布局、配色和贴图照着 tModLoader 版 Boss Checklist 复刻，`assets/*.rawimg` 取自原模组，公开分发前建议先确认原模组的授权方式
- 两个语言版模组 id 相同（`boss-checklist`），属于同一模组的两个语言包，不能同时安装
- 数据（已击败标记、隐藏 / 标记的条目、纪录、已获得过的掉落）存在 `mods\boss-checklist\data\boss-checklist.txt`