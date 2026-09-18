# Boss 清单（BossChecklist）

TerrariaModder 模组：右侧的 Boss 清单侧栏，外加一本可翻阅的 Boss 日志本

仓库按语言分成两套，源码与成品分开放：

```
boss-checklist\
├─ en-US\   英文版源码：Mod.cs、LocalizationData.cs、csproj、manifest.json、icon.png、README.md
│  └─ bin\  英文版成品：BossChecklist.dll、manifest.json、icon.png、README.md
├─ zh-CN\   中文版源码（同上）
│  └─ bin\  中文版成品
├─ assets\*.rawimg               
├─ Content\*.cs、GameRefs.cs、UI\*.cs   
├─ Directory.Build.props / .targets      
└─ README.md
```
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


- 本模组是 tModLoader 版 Boss Checklist 的移植版（原模组：https://github.com/JavidPack/BossChecklist ），
