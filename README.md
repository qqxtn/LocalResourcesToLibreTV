# LibreTV Local Resource API / LibreTV 本地资源接口

[中文](#中文) | [English](#english)

## 中文

这是一个给 LibreTV 使用的本地视频资源接口。它会扫描 `media` 目录里的电影和剧集文件，并提供一个兼容 MacCMS / 苹果 CMS 采集格式的 API。

适合这些场景：

- 把电脑里的本地视频接入 LibreTV。
- 在局域网内给手机、电视盒子或其他设备播放本机视频。
- 不想搭建完整 CMS，只需要一个轻量的本地资源站。

## 环境要求

- Node.js 18 或更高版本
- 本项目不需要安装第三方依赖
- 如果只运行已打包的 Windows exe，不需要安装 Node.js
- 如果需要重新生成 exe，需要 .NET 7 SDK 或更高版本

## 目录结构

把视频放到 `media` 目录下：

```text
media/
  movies/
    流浪地球2.mp4
    让子弹飞.mkv
  tv/
    三体/
      第01集.mp4
      第02集.mp4
    漫长的季节/
      S01E01.mp4
      S01E02.mp4
```

推荐规则：

- 电影放在 `media/movies/`。
- 剧集放在 `media/tv/剧名/`。
- 支持的视频格式：`mp4`、`mkv`、`avi`、`mov`、`webm`、`m4v`、`ts`、`flv`、`wmv`、`rmvb`。

## Windows 托盘版 exe

已打包好的程序在：

```text
dist/LocalLibreTV.exe
```

运行后不会弹出 cmd 窗口，程序会出现在 Windows 右下角系统托盘。右键托盘图标可以打开 API、打开 `media` 文件夹或退出程序。

exe 是单文件自包含程序，图标已经嵌入 exe，不需要随附 DLL 或其他运行文件。首次运行时会在 exe 同目录下自动创建 `media/movies` 和 `media/tv` 目录；视频文件仍需要放在这个 `media` 目录中。

默认 API 地址：

```text
http://127.0.0.1:9978/api.php/provide/vod
```

重新生成 exe：

```powershell
npm run build:exe
```

## 启动服务

如果不使用 exe，也可以直接用 Node.js 启动服务：

```powershell
npm start
```

默认 API 地址：

```text
http://127.0.0.1:9978/api.php/provide/vod
```

如果 LibreTV 和这个服务运行在同一台电脑的同一个浏览器环境里，可以直接使用 `127.0.0.1`。

如果 LibreTV 运行在 Docker、手机、电视盒子或另一台设备上，请把 `127.0.0.1` 换成这台电脑在局域网里的 IP，例如：

```text
http://192.168.1.23:9978/api.php/provide/vod
```

## LibreTV 配置示例

在 LibreTV 的 `API_SITES` 中加入一项：

```js
local: {
    api: 'http://127.0.0.1:9978/api.php/provide/vod',
    name: '本地资源',
    detail: 'http://127.0.0.1:9978',
},
```

如果 LibreTV 不在同一台电脑上运行，请把上面的 `127.0.0.1` 替换为本机局域网 IP。

项目里也提供了一个配置片段文件：

```text
libretv-api-site.local.js
```

## 扫描检查

可以用下面的命令查看当前能被识别到的资源：

```powershell
npm run scan
```

这个命令会输出 MacCMS 风格的 JSON，方便检查电影、剧集、播放地址和分类是否正确。

## 自定义端口和监听地址

默认监听：

```text
HOST=0.0.0.0
PORT=9978
```

PowerShell 示例：

```powershell
$env:PORT=9980
npm start
```

启动后 API 地址会变为：

```text
http://127.0.0.1:9980/api.php/provide/vod
```

## API 路径

- 资源列表：`/api.php/provide/vod`
- 首页同样返回资源列表：`/`
- 视频文件：`/media/...`

接口支持常见查询参数：

- `t` 或 `type`：按分类筛选，`1` 为电影，`2` 为剧集
- `wd`：按名称搜索
- `ids`：按资源 ID 查询，多个 ID 用逗号分隔
- `pg`：页码
- `limit`：每页数量

## 常见问题

### LibreTV 里看不到资源

先运行：

```powershell
npm run scan
```

如果扫描结果为空，请检查视频是否放在 `media/movies` 或 `media/tv/剧名` 下面，并确认文件后缀在支持列表中。

### 手机或电视盒子打不开视频

请不要使用 `127.0.0.1`。在其他设备上，`127.0.0.1` 指的是那台设备自己，不是运行本服务的电脑。请换成电脑的局域网 IP。

### 防火墙提示拦截

如果局域网设备无法访问，请确认 Windows 防火墙允许 Node.js 或当前端口通过。

---

## English

[中文](#中文) | [English](#english)

This project provides a lightweight local video resource API for LibreTV. It scans videos under the `media` directory and exposes a MacCMS / Apple CMS compatible API.

Use it when you want to:

- Add local videos from your computer to LibreTV.
- Stream local videos to phones, TV boxes, or other devices on the same LAN.
- Run a simple local resource endpoint without setting up a full CMS.

## Requirements

- Node.js 18 or later
- No third-party dependencies are required
- The packaged Windows exe does not require Node.js to run
- Rebuilding the exe requires .NET 7 SDK or later

## Folder Layout

Put your videos under `media`:

```text
media/
  movies/
    The Wandering Earth 2.mp4
    Let The Bullets Fly.mkv
  tv/
    Three Body/
      Episode 01.mp4
      Episode 02.mp4
    The Long Season/
      S01E01.mp4
      S01E02.mp4
```

Recommended layout:

- Put movies in `media/movies/`.
- Put TV shows in `media/tv/show-name/`.
- Supported video formats: `mp4`, `mkv`, `avi`, `mov`, `webm`, `m4v`, `ts`, `flv`, `wmv`, `rmvb`.

## Windows Tray exe

The packaged app is:

```text
dist/LocalLibreTV.exe
```

When started, it does not open a cmd window. It runs from the Windows system tray. Right-click the tray icon to open the API, open the `media` folder, or quit the app.

The exe is a self-contained single-file app with the icon embedded. It does not need side-by-side DLLs or runtime files. On first run, it creates `media/movies` and `media/tv` next to the exe; video files still need to be placed in that `media` folder.

Default API URL:

```text
http://127.0.0.1:9978/api.php/provide/vod
```

Rebuild the exe:

```powershell
npm run build:exe
```

## Start The Server

If you do not use the exe, you can still start the Node.js server directly:

```powershell
npm start
```

Default API URL:

```text
http://127.0.0.1:9978/api.php/provide/vod
```

If LibreTV runs in the same browser environment on the same computer, `127.0.0.1` is fine.

If LibreTV runs in Docker, on a phone, on a TV box, or on another device, replace `127.0.0.1` with this computer's LAN IP, for example:

```text
http://192.168.1.23:9978/api.php/provide/vod
```

## LibreTV Configuration Example

Add an entry to LibreTV's `API_SITES`:

```js
local: {
    api: 'http://127.0.0.1:9978/api.php/provide/vod',
    name: 'Local Resources',
    detail: 'http://127.0.0.1:9978',
},
```

If LibreTV is not running on the same computer, replace `127.0.0.1` with your computer's LAN IP.

A ready-to-copy config snippet is also included:

```text
libretv-api-site.local.js
```

## Check The Scan Result

Run:

```powershell
npm run scan
```

This prints the MacCMS-style JSON result so you can verify detected movies, shows, playback URLs, and categories.

## Custom Port And Host

Default values:

```text
HOST=0.0.0.0
PORT=9978
```

PowerShell example:

```powershell
$env:PORT=9980
npm start
```

The API URL then becomes:

```text
http://127.0.0.1:9980/api.php/provide/vod
```

## API Paths

- Resource list: `/api.php/provide/vod`
- The root path also returns the resource list: `/`
- Video files: `/media/...`

Supported query parameters:

- `t` or `type`: filter by category, `1` for movies and `2` for TV shows
- `wd`: search by title
- `ids`: query by resource IDs, separated by commas
- `pg`: page number
- `limit`: items per page

## Troubleshooting

### LibreTV shows no resources

Run:

```powershell
npm run scan
```

If the result is empty, check that videos are placed under `media/movies` or `media/tv/show-name`, and make sure their file extensions are supported.

### Phones or TV boxes cannot open videos

Do not use `127.0.0.1` from another device. On another device, `127.0.0.1` points to that device itself, not to the computer running this service. Use the computer's LAN IP instead.

### Windows Firewall blocks access

If LAN devices cannot connect, allow Node.js or the configured port through Windows Firewall.
