# Psychic Island · Windows 灵动岛

Windows 桌面上的轻量灵动岛：默认显示本地时间，悬停展开系统媒体控制。

## 下载和使用

1. 在 [Releases](https://github.com/tourio114/psychic/releases/latest) 下载 `PsychicIsland-win-x64.zip`。ARM Windows 设备使用 `win-arm64` 版本。
2. 解压到任意目录，运行 `PsychicIsland.exe`，无需另外安装 .NET。
3. 灵动岛默认位于主屏幕工作区顶部中央，显示时、分、秒；悬停展开日期、曲目、歌手、封面、进度和播放按钮。
4. 拖动顶部移动位置，位置会自动保存。右键可切换 12/24 小时制、重置位置、隐藏或退出；隐藏后双击系统托盘图标恢复。

要求 Windows 10 2004（19041）及以上或 Windows 11。首次启动单文件程序会将运行库解压到系统临时目录。

## 媒体支持

通过 Windows 官方 [GlobalSystemMediaTransportControlsSessionManager](https://learn.microsoft.com/en-us/uwp/api/windows.media.control.globalsystemmediatransportcontrolssessionmanager) 接入当前系统媒体会话。上一首、播放/暂停、下一首是否可用由播放器报告。进度条仅用于显示。

播放器必须向 Windows 注册系统媒体会话；没有会话时继续显示时钟。若有多个播放器，控制 Windows 选择的当前会话。歌曲、封面和进度每秒刷新；不支持此接口的播放器无法读取歌曲信息或控制。应用不收集或上传媒体信息。

## 本地构建

安装 .NET 10 SDK，在项目目录执行：

```powershell
dotnet build src/PsychicIsland/PsychicIsland.csproj -c Release
dotnet publish src/PsychicIsland/PsychicIsland.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
./scripts/Smoke-Test.ps1 -Exe ./publish/PsychicIsland.exe
```

`main` 分支推送和手动 Actions 运行会构建 x64 / ARM64 程序，产物在对应运行页面的 Artifacts 中。推送 `v*` 标签会自动发布 Release。x64 构建还执行启动、悬停展开、媒体按钮可访问性与正常退出检查，并保存桌面截图；ARM64 进行交叉编译。真实媒体播放与多显示器/DPI 行为需在 Windows 设备上验证。

设置文件位于 `%LOCALAPPDATA%\PsychicIsland\settings.json`。删除它可恢复默认设置。应用以普通用户权限运行，不修改系统播放器设置。
