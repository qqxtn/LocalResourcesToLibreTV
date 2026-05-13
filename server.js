const http = require('http');
const fs = require('fs');
const path = require('path');
const { URL } = require('url');

const HOST = process.env.HOST || '0.0.0.0';
const PORT = Number(process.env.PORT || 9978);
const ROOT = __dirname;
const MEDIA_DIR = path.join(ROOT, 'media');
const VIDEO_EXTS = new Set(['.mp4', '.mkv', '.avi', '.mov', '.webm', '.m4v', '.ts', '.flv', '.wmv', '.rmvb']);
const MIME = {
  '.mp4': 'video/mp4',
  '.m4v': 'video/mp4',
  '.webm': 'video/webm',
  '.mov': 'video/quicktime',
  '.mkv': 'video/x-matroska',
  '.avi': 'video/x-msvideo',
  '.ts': 'video/mp2t',
  '.flv': 'video/x-flv',
  '.wmv': 'video/x-ms-wmv',
  '.rmvb': 'application/vnd.rn-realmedia-vbr',
};

function ensureMediaDirs() {
  for (const dir of ['movies', 'tv']) {
    fs.mkdirSync(path.join(MEDIA_DIR, dir), { recursive: true });
  }
}

function walk(dir) {
  if (!fs.existsSync(dir)) return [];
  const entries = fs.readdirSync(dir, { withFileTypes: true });
  const files = [];

  for (const entry of entries) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      files.push(...walk(full));
    } else if (entry.isFile() && VIDEO_EXTS.has(path.extname(entry.name).toLowerCase())) {
      files.push(full);
    }
  }

  return files;
}

function cleanTitle(name) {
  return name
    .replace(/\.(mp4|mkv|avi|mov|webm|m4v|ts|flv|wmv|rmvb)$/i, '')
    .replace(/[._]+/g, ' ')
    .replace(/\s+/g, ' ')
    .trim();
}

function extractEpisode(fileName) {
  const base = cleanTitle(fileName);
  const match =
    base.match(/(?:第\s*)?(\d{1,4})\s*(?:集|话)$/i) ||
    base.match(/\bE(?:P)?\s*(\d{1,4})\b/i) ||
    base.match(/\bS\d{1,2}E(\d{1,4})\b/i);
  return match ? Number(match[1]) : null;
}

function inferMovieName(relParts) {
  const fileTitle = cleanTitle(relParts[relParts.length - 1]);
  if (relParts.length >= 2) {
    const parent = relParts[relParts.length - 2];
    if (!['movies', 'movie', '电影'].includes(parent.toLowerCase())) return cleanTitle(parent);
  }
  return fileTitle;
}

function makeVideoUrl(absPath, origin) {
  const rel = path.relative(MEDIA_DIR, absPath).split(path.sep).map(encodeURIComponent).join('/');
  return `${origin}/media/${rel}`;
}

function scanLibrary(origin) {
  ensureMediaDirs();
  const files = walk(MEDIA_DIR);
  const movies = new Map();
  const shows = new Map();

  for (const absPath of files) {
    const relParts = path.relative(MEDIA_DIR, absPath).split(path.sep);
    const top = (relParts[0] || '').toLowerCase();
    const isTv = ['tv', 'series', '电视剧', 'shows'].includes(top) || relParts.length >= 3;

    if (isTv) {
      const showName = relParts.length >= 2 ? cleanTitle(relParts[1]) : cleanTitle(path.basename(absPath));
      const episodeNumber = extractEpisode(path.basename(absPath));
      const fallbackEpisode = shows.get(showName)?.episodes.length + 1 || 1;
      const episodeName = episodeNumber ? `第${String(episodeNumber).padStart(2, '0')}集` : cleanTitle(path.basename(absPath)) || `第${fallbackEpisode}集`;

      if (!shows.has(showName)) shows.set(showName, { name: showName, episodes: [] });
      shows.get(showName).episodes.push({
        number: episodeNumber || fallbackEpisode,
        name: episodeName,
        url: makeVideoUrl(absPath, origin),
      });
      continue;
    }

    const movieName = inferMovieName(relParts);
    if (!movies.has(movieName)) {
      movies.set(movieName, { name: movieName, files: [] });
    }
    movies.get(movieName).files.push({
      name: cleanTitle(path.basename(absPath)),
      url: makeVideoUrl(absPath, origin),
    });
  }

  let id = 1;
  const list = [];

  for (const movie of movies.values()) {
    const playUrl = movie.files.map((file, index) => {
      const label = movie.files.length > 1 ? `${file.name || `线路${index + 1}`}` : movie.name;
      return `${label}$${file.url}`;
    }).join('#');

    list.push(makeVod(id++, 1, '电影', movie.name, playUrl, movie.files.length > 1 ? `${movie.files.length}个视频` : '本地'));
  }

  for (const show of shows.values()) {
    show.episodes.sort((a, b) => a.number - b.number || a.name.localeCompare(b.name, 'zh-Hans-CN'));
    const playUrl = show.episodes.map(ep => `${ep.name}$${ep.url}`).join('#');
    list.push(makeVod(id++, 2, '电视剧', show.name, playUrl, `全${show.episodes.length}集`));
  }

  return list;
}

function makeVod(id, typeId, typeName, name, playUrl, remarks) {
  return {
    vod_id: id,
    vod_name: name,
    type_id: typeId,
    type_name: typeName,
    vod_en: '',
    vod_time: new Date().toISOString().replace('T', ' ').slice(0, 19),
    vod_remarks: remarks,
    vod_play_from: 'local',
    vod_play_url: playUrl,
    vod_pic: '',
    vod_year: '',
    vod_area: '本地',
    vod_lang: '',
    vod_actor: '',
    vod_director: '',
    vod_content: `${name} - 本地视频`,
  };
}

function json(res, data) {
  const body = JSON.stringify(data);
  res.writeHead(200, {
    'Content-Type': 'application/json; charset=utf-8',
    'Access-Control-Allow-Origin': '*',
    'Access-Control-Allow-Methods': 'GET, HEAD, OPTIONS',
    'Access-Control-Allow-Headers': 'Range, Content-Type',
    'Access-Control-Expose-Headers': 'Content-Length, Content-Range, Accept-Ranges',
  });
  res.end(body);
}

function vodResponse(query, origin = `http://127.0.0.1:${PORT}`) {
  const library = scanLibrary(origin);
  const classes = [
    { type_id: 1, type_pid: 0, type_name: '电影' },
    { type_id: 2, type_pid: 0, type_name: '电视剧' },
  ];

  let result = library;
  const ids = query.get('ids');
  const wd = (query.get('wd') || '').trim().toLowerCase();
  const typeId = Number(query.get('t') || query.get('type') || 0);
  const page = Math.max(1, Number(query.get('pg') || 1));
  const limit = Math.max(1, Number(query.get('limit') || 20));

  if (ids) {
    const idSet = new Set(ids.split(',').map(value => Number(value.trim())).filter(Boolean));
    result = result.filter(item => idSet.has(item.vod_id));
  }

  if (typeId) {
    result = result.filter(item => item.type_id === typeId);
  }

  if (wd) {
    result = result.filter(item => item.vod_name.toLowerCase().includes(wd));
  }

  const total = result.length;
  const pagecount = Math.max(1, Math.ceil(total / limit));
  const start = (page - 1) * limit;
  const pageList = result.slice(start, start + limit);

  return {
    code: 1,
    msg: '数据列表',
    page,
    pagecount,
    limit: String(limit),
    total,
    list: pageList,
    class: classes,
  };
}

function sendMedia(req, res, pathname) {
  const decoded = decodeURIComponent(pathname.replace(/^\/media\//, ''));
  const absPath = path.resolve(MEDIA_DIR, decoded);
  const mediaRoot = path.resolve(MEDIA_DIR);

  if (!absPath.startsWith(mediaRoot + path.sep) || !fs.existsSync(absPath) || !fs.statSync(absPath).isFile()) {
    res.writeHead(404);
    res.end('Not found');
    return;
  }

  const stat = fs.statSync(absPath);
  const range = req.headers.range;
  const contentType = MIME[path.extname(absPath).toLowerCase()] || 'application/octet-stream';

  res.setHeader('Access-Control-Allow-Origin', '*');
  res.setHeader('Access-Control-Expose-Headers', 'Content-Length, Content-Range, Accept-Ranges');
  res.setHeader('Accept-Ranges', 'bytes');
  res.setHeader('Content-Type', contentType);

  if (range) {
    const match = range.match(/bytes=(\d*)-(\d*)/);
    const start = match && match[1] ? Number(match[1]) : 0;
    const end = match && match[2] ? Number(match[2]) : stat.size - 1;

    if (start >= stat.size || end >= stat.size || start > end) {
      res.writeHead(416, { 'Content-Range': `bytes */${stat.size}` });
      res.end();
      return;
    }

    res.writeHead(206, {
      'Content-Length': end - start + 1,
      'Content-Range': `bytes ${start}-${end}/${stat.size}`,
    });
    fs.createReadStream(absPath, { start, end }).pipe(res);
    return;
  }

  res.writeHead(200, { 'Content-Length': stat.size });
  fs.createReadStream(absPath).pipe(res);
}

function router(req, res) {
  const url = new URL(req.url, `http://${req.headers.host || `127.0.0.1:${PORT}`}`);

  if (req.method === 'OPTIONS') {
    res.writeHead(204, {
      'Access-Control-Allow-Origin': '*',
      'Access-Control-Allow-Methods': 'GET, HEAD, OPTIONS',
      'Access-Control-Allow-Headers': 'Range, Content-Type',
    });
    res.end();
    return;
  }

  if (url.pathname === '/' || url.pathname === '/api.php/provide/vod') {
    const origin = `http://${req.headers.host || `127.0.0.1:${PORT}`}`;
    json(res, vodResponse(url.searchParams, origin));
    return;
  }

  if (url.pathname.startsWith('/media/')) {
    sendMedia(req, res, url.pathname);
    return;
  }

  res.writeHead(404, { 'Content-Type': 'text/plain; charset=utf-8' });
  res.end('Not found');
}

ensureMediaDirs();

if (process.argv.includes('--scan')) {
  console.log(JSON.stringify(vodResponse(new URLSearchParams(), `http://127.0.0.1:${PORT}`), null, 2));
} else {
  http.createServer(router).listen(PORT, HOST, () => {
    console.log(`Local resource API: http://127.0.0.1:${PORT}/api.php/provide/vod`);
    console.log(`Put videos in: ${MEDIA_DIR}`);
  });
}
