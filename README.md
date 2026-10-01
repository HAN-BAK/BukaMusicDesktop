<h1 align="center">BukaMusic 控制台</h1>

<p align="center">
  <img src="Assets/logo.png" alt="BukaMusic Logo" width="200" />
</p>

<p align="center">
  <b>BukaMusic Desktop</b> — Windows 桌面控制台，用来自动发现并控制同一局域网内安装了 <b>BukaMusic</b> 的安卓设备。
</p>

---

## 这是什么

安卓端的 **BukaMusic**（[仓库在这里](https://github.com/HAN-BAK/BukaMusic)）跑在手机、平板、电视盒子上，播放界面在设备屏幕或电视上。
本控制台把整套操作搬到电脑上：不用拿起手机，在电脑上就能选曲、调音、传歌、看歌词、管多房间同步。

程序启动后会用 UDP 广播自动发现设备，每个设备显示为一张卡片，点卡片进入控制台；电脑上改的设置会直接写到设备上。

## 下载与安装

到 [Releases](https://github.com/HAN-BAK/BukaMusicDesktop/releases) 下载最新版本，两种形式任选：

| 文件 | 说明 |
| --- | --- |
| `BukaMusicDesktop-<版本>-x64.msi` | **安装包**（推荐）：双击安装，装到 `Program Files\BukaMusic 控制台`，自动创建开始菜单和桌面快捷方式，可从“应用和功能”卸载 |
| `BukaMusicDesktop-<版本>-portable-x64.zip` | **便携版**：解压到任意目录，运行 `BukaMusicDesktop.exe`，不写注册表 |

两种形式都已内置 .NET 与 Windows App SDK，**无需另外安装运行库**。仅支持 64 位 Windows 10 1809 及以上。

> 安装包未做代码签名，首次安装时 Windows 可能提示“未知发布者”，选择“更多信息 → 仍要运行”即可。

## 功能

**设备**

- UDP 广播自动发现局域网内的设备，支持手动添加 `IP:端口`；设备离线会从列表移除，短暂丢包不会误判
- 播放控制：播放 / 暂停 / 上一曲 / 下一曲 / 进度拖动 / 系统音量
- 曲库：全部歌曲列表，或**按专辑 / 按歌手**分类浏览（带封面），支持搜索、排序、上传音乐、批量删除文件
- 曲库交互：**左键点击歌曲直接在电脑上播放**（音频从设备流式读取，可拖动进度）；**右键**弹出菜单，可选「在 <设备名称> 上播放」让安卓设备播放，或「电脑播放」
- 播放控制页下方新增**电脑播放**分组：当前曲目、进度、上一曲 / 播放暂停 / 下一曲，与上面的设备控制互不影响
- **管理**模式：点「管理」后歌曲行与专辑封面出现勾选框，可多选歌曲或整个专辑 / 歌手，一键批量删除文件
- 远程切换设备屏幕：播放控制页的「歌词界面」按钮让设备打开歌词画面；设备显示歌词界面时按钮自动变成「播放界面」，再点一下切回主播放界面
- 设备音量以**百分比**显示（按设备自身音量档位换算），进度条两侧时间与音量数值和滑块对齐
- 设置：设备名称、音乐文件夹、播放方式（顺序 / 顺序循环 / 随机循环 / 单曲循环）、启动自动播放、左右声道平衡、背景模糊、在线歌词开关、**10 段均衡器**（含预设的保存 / 加载 / 删除 / 导出 / 导入 / 恢复默认）
- 多房间同步：勾选要把音乐推送到哪些设备；如果本机是接收端，则只显示"断开多房间连接"
- 歌词预览：把安卓端的商籁（sonnet）歌词画面移植到 Windows（Win2D + 预编译像素着色器），支持逐词高亮、间奏三点、转场特效
- 运行日志：设备连接、曲库、多房间、歌词等所有动作都有记录，方便排查问题

**界面**

- 四语言切换：中文 / English / 日本語 / 한국어，右下角随时切换，选择会被记住
- 深色 + 天空蓝配色，参考 BetterGI 的卡片式布局
- 文字放不下时自动左右滚动，按钮、输入框提示、计数文字都适用
- 列表各列（歌手 / 专辑 / 时长 / 大小）与表头严格对齐，缺少信息时显示「暂无」，悬停底色为圆角
- 窗口默认 900×600 打开并居中（可在窗口里自由调整大小）

## 环境要求

| 项目 | 版本 |
| --- | --- |
| 系统 | Windows 10 1809（17763）及以上，x64 |
| 运行时 | .NET 8（Windows App SDK 以自包含方式随程序发布，无需另外安装） |
| 开发 | .NET 8 SDK、Windows App SDK 1.8、Win2D 1.4（均由 NuGet 还原） |

安卓端需要 **BukaMusic 2.70 或更高版本**（控制台依赖它提供的 HTTP 接口）。

## 构建

```powershell
cd BukaMusicDesktop
dotnet build -c Release -p:Platform=x64
```

产物在：

```
bin\x64\Release\net8.0-windows10.0.26100.0\win-x64\BukaMusicDesktop.exe
```

打包安装包（需要一次 `dotnet tool install --global wix`）：

```powershell
powershell -ExecutionPolicy Bypass -File installer\build.ps1
```

会在 `dist` 下生成自包含的 MSI 安装包（安装脚本分两步：先 `dotnet publish` 出自包含产物，
再交给 WiX 打成 MSI，开始菜单与桌面快捷方式、控制面板图标、卸载入口都由安装包负责）。

## 使用

1. 确认安卓设备上的 BukaMusic 正在运行（通知栏有常驻播放状态），并且与本机在同一局域网
2. 打开 `BukaMusicDesktop.exe`，等待设备卡片出现
3. 点设备卡片进入控制台：左侧是播放控制 / 歌词预览 / 多房间同步 / 曲库与文件 / 设置 / 运行日志 / 关于

调试用的命令行参数（一般用不到）：

```
BukaMusicDesktop.exe --lang en                     # 指定界面语言：zh / en / ja / ko
BukaMusicDesktop.exe --page library 192.168.0.10   # 直接打开某台设备的指定页面
BukaMusicDesktop.exe --navtest settings 192.168.0.10   # 打开设备后自动点一次侧栏
```

## 通信方式

控制台不依赖安卓端的界面，全部通过局域网接口工作：

| 接口 | 用途 |
| --- | --- |
| `GET /api/info` | 设备信息（也是 UDP 自动发现的应答内容） |
| `GET /api/state` | 播放状态（歌曲、进度、音量、模式） |
| `GET/POST /api/settings` | 读取 / 修改设置 |
| `GET /api/library` | 曲库列表 |
| `POST /api/control` | 播放控制（play / pause / next / previous / seek / volume / mode / rescan / playTrack / 均衡器预设） |
| `POST /api/delete` | 删除曲库中的文件 |
| `GET /api/cover` | 当前封面图片 |
| `GET/POST /api/multicast` | 多房间同步状态与设置 |
| `GET /api/lyrics` | 歌词（含词级时间戳与译文） |
| `GET /api/file?path=` | 流式读取歌曲文件（支持 Range），电脑播放用 |
| `POST /upload` | 上传音乐（网页端与控制台共用） |

`POST /api/control` 支持的动作：`play`、`pause`、`toggle`、`next`、`previous`、`seek`、
`volume`、`mode`、`rescan`、`playTrack`、`saveEqPreset`、`deleteEqPreset`、
`exportEqPresets`、`importEqPresets`、`disconnectMultiRoomReceiver`、
`openLyrics`（设备打开歌词界面）、`openMain`（设备切回播放界面）。

`GET /api/state` 中的 `screen` 字段表示设备当前显示的是 `lyrics` 还是 `main`。

发现端口 `UDP 47101`，控制端口为设备上的 `8080`（被占用时自动顺延，控制台会按扫码到的端口连接）。

## 其它

- 配置文件与日志：`%APPDATA%\BukaMusicDesktop\settings.json`、`%APPDATA%\BukaMusicDesktop\log.txt`
- 界面文案表在 `Core/Loc.cs`，新增文案直接加一行即可支持四语言
- 像素着色器源码在 `Shaders/sonnet_final.hlsl`，预编译结果 `Shaders/sonnet_final.bin` 随程序内置（`Shaders/compile.ps1` 用来重新编译）

## 许可

GPL-3.0，与安卓端一致。
