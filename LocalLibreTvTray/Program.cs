using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace LocalLibreTvTray;

internal static class Program
{
    private const int Port = 9978;
    private static readonly string Root = AppContext.BaseDirectory;
    private static readonly string MediaDir = Path.Combine(Root, "media");
    private static readonly HashSet<string> VideoExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi", ".mov", ".webm", ".m4v", ".ts", ".flv", ".wmv", ".rmvb"
    };
    private static readonly Dictionary<string, string> Mime = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mp4"] = "video/mp4",
        [".m4v"] = "video/mp4",
        [".webm"] = "video/webm",
        [".mov"] = "video/quicktime",
        [".mkv"] = "video/x-matroska",
        [".avi"] = "video/x-msvideo",
        [".ts"] = "video/mp2t",
        [".flv"] = "video/x-flv",
        [".wmv"] = "video/x-ms-wmv",
        [".rmvb"] = "application/vnd.rn-realmedia-vbr"
    };

    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        EnsureMediaDirs();

        using var cts = new CancellationTokenSource();
        var serverTask = Task.Run(() => RunServerAsync(cts.Token));

        using var tray = new NotifyIcon
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application,
            Text = "LibreTV Local API",
            Visible = true,
            ContextMenuStrip = BuildMenu(cts)
        };

        Application.Run();
        cts.Cancel();
        try { serverTask.Wait(TimeSpan.FromSeconds(2)); } catch { }
    }

    private static ContextMenuStrip BuildMenu(CancellationTokenSource cts)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("打开 API", null, (_, _) => OpenUrl($"http://127.0.0.1:{Port}/api.php/provide/vod"));
        menu.Items.Add("打开 media 文件夹", null, (_, _) =>
        {
            EnsureMediaDirs();
            Process.Start(new ProcessStartInfo(MediaDir) { UseShellExecute = true });
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) =>
        {
            cts.Cancel();
            Application.Exit();
        });
        return menu;
    }

    private static void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private static async Task RunServerAsync(CancellationToken token)
    {
        var listener = new TcpListener(IPAddress.Any, Port);
        listener.Start();

        try
        {
            while (!token.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(token);
                _ = Task.Run(() => HandleClientAsync(client, token), token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task HandleClientAsync(TcpClient client, CancellationToken token)
    {
        using var _ = client;
        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, bufferSize: 8192, leaveOpen: true);

        var requestLine = await reader.ReadLineAsync(token);
        if (string.IsNullOrWhiteSpace(requestLine)) return;

        var parts = requestLine.Split(' ', 3);
        if (parts.Length < 2) return;

        var method = parts[0].ToUpperInvariant();
        var target = parts[1];
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        string? line;
        while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(token)))
        {
            var separator = line.IndexOf(':');
            if (separator > 0)
            {
                headers[line[..separator].Trim()] = line[(separator + 1)..].Trim();
            }
        }

        if (!Uri.TryCreate(target, UriKind.RelativeOrAbsolute, out var uri)) return;
        var path = uri.IsAbsoluteUri ? uri.AbsolutePath : target.Split('?', 2)[0];
        var queryText = uri.IsAbsoluteUri ? uri.Query : (target.Contains('?') ? target[(target.IndexOf('?') + 1)..] : string.Empty);
        var query = ParseQuery(queryText);
        var host = headers.TryGetValue("Host", out var hostValue) ? hostValue : $"127.0.0.1:{Port}";

        if (method == "OPTIONS")
        {
            await WriteHeadersAsync(stream, "204 No Content", CommonHeaders(), 0, token);
            return;
        }

        if (path == "/" || path.Equals("/api.php/provide/vod", StringComparison.OrdinalIgnoreCase))
        {
            var json = JsonSerializer.Serialize(VodResponse(query, $"http://{host}"));
            var body = Encoding.UTF8.GetBytes(json);
            var responseHeaders = CommonHeaders();
            responseHeaders["Content-Type"] = "application/json; charset=utf-8";
            await WriteHeadersAsync(stream, "200 OK", responseHeaders, body.Length, token);
            if (method != "HEAD") await stream.WriteAsync(body, token);
            return;
        }

        if (path.StartsWith("/media/", StringComparison.OrdinalIgnoreCase))
        {
            await SendMediaAsync(stream, method, path, headers, token);
            return;
        }

        await WriteTextAsync(stream, "404 Not Found", "Not found", token);
    }

    private static async Task SendMediaAsync(NetworkStream stream, string method, string path, Dictionary<string, string> headers, CancellationToken token)
    {
        var decoded = Uri.UnescapeDataString(path["/media/".Length..].Replace('/', Path.DirectorySeparatorChar));
        var absPath = Path.GetFullPath(Path.Combine(MediaDir, decoded));
        var mediaRoot = Path.GetFullPath(MediaDir);

        if (!absPath.StartsWith(mediaRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(absPath))
        {
            await WriteTextAsync(stream, "404 Not Found", "Not found", token);
            return;
        }

        var info = new FileInfo(absPath);
        var ext = Path.GetExtension(absPath);
        var contentType = Mime.TryGetValue(ext, out var mime) ? mime : "application/octet-stream";
        var responseHeaders = CommonHeaders();
        responseHeaders["Content-Type"] = contentType;
        responseHeaders["Accept-Ranges"] = "bytes";

        var start = 0L;
        var end = info.Length - 1;
        var status = "200 OK";

        if (headers.TryGetValue("Range", out var range))
        {
            var match = Regex.Match(range, @"bytes=(\d*)-(\d*)");
            if (match.Success)
            {
                if (long.TryParse(match.Groups[1].Value, out var parsedStart)) start = parsedStart;
                if (long.TryParse(match.Groups[2].Value, out var parsedEnd)) end = parsedEnd;
            }

            if (start >= info.Length || end >= info.Length || start > end)
            {
                responseHeaders["Content-Range"] = $"bytes */{info.Length}";
                await WriteHeadersAsync(stream, "416 Range Not Satisfiable", responseHeaders, 0, token);
                return;
            }

            status = "206 Partial Content";
            responseHeaders["Content-Range"] = $"bytes {start}-{end}/{info.Length}";
        }

        var length = end - start + 1;
        await WriteHeadersAsync(stream, status, responseHeaders, length, token);
        if (method == "HEAD") return;

        await using var file = File.OpenRead(absPath);
        file.Seek(start, SeekOrigin.Begin);
        var buffer = new byte[1024 * 128];
        var remaining = length;
        while (remaining > 0)
        {
            var read = await file.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), token);
            if (read == 0) break;
            await stream.WriteAsync(buffer.AsMemory(0, read), token);
            remaining -= read;
        }
    }

    private static Dictionary<string, string> CommonHeaders() => new()
    {
        ["Access-Control-Allow-Origin"] = "*",
        ["Access-Control-Allow-Methods"] = "GET, HEAD, OPTIONS",
        ["Access-Control-Allow-Headers"] = "Range, Content-Type",
        ["Access-Control-Expose-Headers"] = "Content-Length, Content-Range, Accept-Ranges"
    };

    private static async Task WriteTextAsync(NetworkStream stream, string status, string text, CancellationToken token)
    {
        var body = Encoding.UTF8.GetBytes(text);
        var headers = CommonHeaders();
        headers["Content-Type"] = "text/plain; charset=utf-8";
        await WriteHeadersAsync(stream, status, headers, body.Length, token);
        await stream.WriteAsync(body, token);
    }

    private static async Task WriteHeadersAsync(NetworkStream stream, string status, Dictionary<string, string> headers, long contentLength, CancellationToken token)
    {
        headers["Content-Length"] = contentLength.ToString();
        var builder = new StringBuilder($"HTTP/1.1 {status}\r\n");
        foreach (var (name, value) in headers)
        {
            builder.Append(name).Append(": ").Append(value).Append("\r\n");
        }
        builder.Append("Connection: close\r\n\r\n");
        var bytes = Encoding.ASCII.GetBytes(builder.ToString());
        await stream.WriteAsync(bytes, token);
    }

    private static ApiResponse VodResponse(Dictionary<string, string> query, string origin)
    {
        var library = ScanLibrary(origin);
        var result = library.AsEnumerable();

        if (query.TryGetValue("ids", out var ids) && !string.IsNullOrWhiteSpace(ids))
        {
            var idSet = ids.Split(',').Select(value => int.TryParse(value.Trim(), out var id) ? id : 0).Where(id => id > 0).ToHashSet();
            result = result.Where(item => idSet.Contains(item.vod_id));
        }

        if ((query.TryGetValue("t", out var t) || query.TryGetValue("type", out t)) && int.TryParse(t, out var typeId) && typeId > 0)
        {
            result = result.Where(item => item.type_id == typeId);
        }

        if (query.TryGetValue("wd", out var wd) && !string.IsNullOrWhiteSpace(wd))
        {
            result = result.Where(item => item.vod_name.Contains(wd.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        var list = result.ToList();
        var page = query.TryGetValue("pg", out var pgValue) && int.TryParse(pgValue, out var pg) ? Math.Max(1, pg) : 1;
        var limit = query.TryGetValue("limit", out var limitValue) && int.TryParse(limitValue, out var parsedLimit) ? Math.Max(1, parsedLimit) : 20;
        var pagecount = Math.Max(1, (int)Math.Ceiling(list.Count / (double)limit));

        return new ApiResponse
        {
            code = 1,
            msg = "数据列表",
            page = page,
            pagecount = pagecount,
            limit = limit.ToString(),
            total = list.Count,
            @class = new[]
            {
                new VodClass(1, 0, "电影"),
                new VodClass(2, 0, "电视剧")
            },
            list = list.Skip((page - 1) * limit).Take(limit).ToArray()
        };
    }

    private static List<VodItem> ScanLibrary(string origin)
    {
        EnsureMediaDirs();
        var files = Directory.EnumerateFiles(MediaDir, "*.*", SearchOption.AllDirectories)
            .Where(file => VideoExts.Contains(Path.GetExtension(file)))
            .ToArray();
        var movies = new Dictionary<string, List<(string Name, string Url)>>();
        var shows = new Dictionary<string, List<(int Number, string Name, string Url)>>();

        foreach (var file in files)
        {
            var relParts = Path.GetRelativePath(MediaDir, file).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var top = relParts.FirstOrDefault()?.ToLowerInvariant() ?? string.Empty;
            var isTv = top is "tv" or "series" or "电视剧" or "shows" || relParts.Length >= 3;

            if (isTv)
            {
                var showName = relParts.Length >= 2 ? CleanTitle(relParts[1]) : CleanTitle(Path.GetFileName(file));
                var episodeNumber = ExtractEpisode(Path.GetFileName(file));
                if (!shows.ContainsKey(showName)) shows[showName] = new();
                var fallback = shows[showName].Count + 1;
                shows[showName].Add((episodeNumber ?? fallback, episodeNumber.HasValue ? $"第{episodeNumber.Value:00}集" : CleanTitle(Path.GetFileName(file)), MakeVideoUrl(file, origin)));
                continue;
            }

            var movieName = InferMovieName(relParts);
            if (!movies.ContainsKey(movieName)) movies[movieName] = new();
            movies[movieName].Add((CleanTitle(Path.GetFileName(file)), MakeVideoUrl(file, origin)));
        }

        var id = 1;
        var list = new List<VodItem>();

        foreach (var movie in movies)
        {
            var playUrl = string.Join("#", movie.Value.Select((file, index) => $"{(movie.Value.Count > 1 ? file.Name : movie.Key)}${file.Url}"));
            list.Add(MakeVod(id++, 1, "电影", movie.Key, playUrl, movie.Value.Count > 1 ? $"{movie.Value.Count}个视频" : "本地"));
        }

        foreach (var show in shows)
        {
            var episodes = show.Value.OrderBy(ep => ep.Number).ThenBy(ep => ep.Name, StringComparer.Create(new System.Globalization.CultureInfo("zh-Hans-CN"), false));
            var playUrl = string.Join("#", episodes.Select(ep => $"{ep.Name}${ep.Url}"));
            list.Add(MakeVod(id++, 2, "电视剧", show.Key, playUrl, $"共{show.Value.Count}集"));
        }

        return list;
    }

    private static VodItem MakeVod(int id, int typeId, string typeName, string name, string playUrl, string remarks) => new()
    {
        vod_id = id,
        vod_name = name,
        type_id = typeId,
        type_name = typeName,
        vod_en = "",
        vod_time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
        vod_remarks = remarks,
        vod_play_from = "local",
        vod_play_url = playUrl,
        vod_pic = "",
        vod_year = "",
        vod_area = "本地",
        vod_lang = "",
        vod_actor = "",
        vod_director = "",
        vod_content = $"{name} - 本地视频"
    };

    private static string MakeVideoUrl(string absPath, string origin)
    {
        var rel = Path.GetRelativePath(MediaDir, absPath).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return $"{origin}/media/{string.Join("/", rel.Select(Uri.EscapeDataString))}";
    }

    private static string InferMovieName(string[] relParts)
    {
        var fileTitle = CleanTitle(relParts[^1]);
        if (relParts.Length >= 2)
        {
            var parent = relParts[^2];
            if (!new[] { "movies", "movie", "电影" }.Contains(parent, StringComparer.OrdinalIgnoreCase))
            {
                return CleanTitle(parent);
            }
        }
        return fileTitle;
    }

    private static int? ExtractEpisode(string fileName)
    {
        var baseName = CleanTitle(fileName);
        var match = Regex.Match(baseName, @"第\s*(\d{1,4})\s*[集回]$", RegexOptions.IgnoreCase);
        if (!match.Success) match = Regex.Match(baseName, @"\bE(?:P)?\s*(\d{1,4})\b", RegexOptions.IgnoreCase);
        if (!match.Success) match = Regex.Match(baseName, @"\bS\d{1,2}E(\d{1,4})\b", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups[1].Value, out var number) ? number : null;
    }

    private static string CleanTitle(string name)
    {
        return Regex.Replace(Path.GetFileNameWithoutExtension(name).Replace('.', ' ').Replace('_', ' '), @"\s+", " ").Trim();
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        return query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(parts => parts.Length > 0)
            .ToDictionary(parts => Uri.UnescapeDataString(parts[0]), parts => parts.Length > 1 ? Uri.UnescapeDataString(parts[1].Replace('+', ' ')) : "", StringComparer.OrdinalIgnoreCase);
    }

    private static void EnsureMediaDirs()
    {
        Directory.CreateDirectory(Path.Combine(MediaDir, "movies"));
        Directory.CreateDirectory(Path.Combine(MediaDir, "tv"));
    }

    private sealed record VodClass(int type_id, int type_pid, string type_name);

    private sealed class ApiResponse
    {
        public int code { get; set; }
        public string msg { get; set; } = "";
        public int page { get; set; }
        public int pagecount { get; set; }
        public string limit { get; set; } = "";
        public int total { get; set; }
        public VodItem[] list { get; set; } = Array.Empty<VodItem>();
        public VodClass[] @class { get; set; } = Array.Empty<VodClass>();
    }

    private sealed class VodItem
    {
        public int vod_id { get; set; }
        public string vod_name { get; set; } = "";
        public int type_id { get; set; }
        public string type_name { get; set; } = "";
        public string vod_en { get; set; } = "";
        public string vod_time { get; set; } = "";
        public string vod_remarks { get; set; } = "";
        public string vod_play_from { get; set; } = "";
        public string vod_play_url { get; set; } = "";
        public string vod_pic { get; set; } = "";
        public string vod_year { get; set; } = "";
        public string vod_area { get; set; } = "";
        public string vod_lang { get; set; } = "";
        public string vod_actor { get; set; } = "";
        public string vod_director { get; set; } = "";
        public string vod_content { get; set; } = "";
    }
}
