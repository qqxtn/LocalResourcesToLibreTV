# LibreTV 本地资源站

这个目录已经生成了一个本地 MacCMS / 苹果 CMS 风格资源站，LibreTV 可以像接入采集站一样接入它。

## 目录放法

把视频放到 `media` 下面：

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

电影放 `media/movies`，电视剧放 `media/tv/剧名/`。视频格式支持 `mp4`、`mkv`、`avi`、`mov`、`webm`、`m4v`、`ts`、`flv`、`wmv`。

## 启动

```powershell
npm start
```

启动后 API 地址是：

```text
http://127.0.0.1:9978/api.php/provide/vod
```

如果 LibreTV 跑在 Docker、手机、电视盒子或另一台机器上，不能用 `127.0.0.1`，要换成这台电脑在局域网里的 IP，例如：

```text
http://192.168.1.23:9978/api.php/provide/vod
```

## LibreTV 配置

在 LibreTV 的 `API_SITES` 里加一项：

```js
local: {
    api: 'http://127.0.0.1:9978/api.php/provide/vod',
    name: '本地资源',
    detail: 'http://127.0.0.1:9978',
},
```

如果 LibreTV 不在同一台电脑浏览器里运行，把上面的 `127.0.0.1` 换成你的局域网 IP。

## 检查扫描结果

```powershell
npm run scan
```

这个命令会输出当前能被资源站识别到的电影和电视剧。
