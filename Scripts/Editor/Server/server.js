const http = require('http');
const https = require('https');
const fs = require('fs');
const path = require('path');
const zlib = require('zlib');
const os = require('os');

const buildPath = process.argv[2] || './';
const port = process.argv[3] || 8080;
const certPath = process.argv[4] || '';
const keyPath = process.argv[5] || '';
const caPath = process.argv[6] || '';

const enableWasmMultithreading = true;
const enableCORS = true;

// 원격 디버그 경로. 빌드 파일과 겹치지 않도록 접두사를 둔다.
const WLR = '/__wlr/';
const maxLogs = 5000;

let server;

// ---- 원격 콘솔 ---------------------------------------------------------------
// 폰 페이지에 주입한 client.js가 console/오류를 보내면 여기 쌓고, 대시보드(SSE)와
// 유니티 에디터(/__wlr/logs 폴링)에 그대로 넘긴다. Android와 iPhone 모두 같은 방식이라
// USB 케이블이나 브라우저 개발자 도구 없이 볼 수 있다.
const logs = [];
const devices = new Map();
const viewers = new Set();
const deviceStreams = new Map();
let seq = 0;

function broadcast(event, data) {
    const payload = `event: ${event}\ndata: ${JSON.stringify(data)}\n\n`;
    for (const res of viewers) res.write(payload);
}

function addLogs(device, entries) {
    for (const e of entries) {
        const log = {
            seq: ++seq,
            device: device.id,
            name: device.name,
            level: String(e.level || 'log'),
            time: Number(e.time) || Date.now(),
            message: String(e.message ?? '').slice(0, 20000)
        };
        logs.push(log);
        if (logs.length > maxLogs) logs.shift();
        broadcast('log', log);
    }
}

function deviceName(ua) {
    if (/iPhone/.test(ua)) return 'iPhone';
    if (/iPad/.test(ua) || (/Macintosh/.test(ua) && /Mobile/.test(ua))) return 'iPad';
    const android = ua.match(/Android [\d.]+; ([^;)]+)/);
    if (android) return android[1].replace(/ Build\/.*/, '').trim() || 'Android';
    if (/Android/.test(ua)) return 'Android';
    if (/Macintosh/.test(ua)) return 'Mac';
    if (/Windows/.test(ua)) return 'Windows';
    return 'Browser';
}

function touchDevice(id, info, req) {
    let device = devices.get(id);
    if (!device) {
        const ua = (info && info.userAgent) || req.headers['user-agent'] || '';
        device = { id, name: deviceName(ua), ua, address: req.socket.remoteAddress, info: {}, first: Date.now() };
        devices.set(id, device);
    }
    if (info) device.info = Object.assign(device.info, info);
    device.last = Date.now();
    device.online = deviceStreams.has(id);
    broadcast('device', device);
    return device;
}

function readBody(req, limit, callback) {
    let size = 0;
    const chunks = [];
    req.on('data', chunk => {
        size += chunk.length;
        if (size > limit) { req.destroy(); return; }
        chunks.push(chunk);
    });
    req.on('end', () => {
        try { callback(JSON.parse(Buffer.concat(chunks).toString('utf8') || '{}')); }
        catch { callback(null); }
    });
}

function send(res, status, type, body, extra) {
    res.writeHead(status, Object.assign({
        'Content-Type': type,
        'Cache-Control': 'no-store',
        'Access-Control-Allow-Origin': '*',
        'Cross-Origin-Resource-Policy': 'cross-origin',
        'Cross-Origin-Opener-Policy': 'same-origin',
        'Cross-Origin-Embedder-Policy': 'require-corp'
    }, extra || {}));
    res.end(body);
}

function openStream(res) {
    res.writeHead(200, {
        'Content-Type': 'text/event-stream',
        'Cache-Control': 'no-store',
        'Connection': 'keep-alive',
        'Access-Control-Allow-Origin': '*',
        'Cross-Origin-Resource-Policy': 'cross-origin'
    });
    res.write('retry: 1000\n\n');
}

// 프록시나 휴대폰 절전 때문에 끊긴 SSE 연결을 빨리 알아채도록 주기적으로 주석을 보낸다.
setInterval(() => {
    for (const res of viewers) res.write(': ping\n\n');
    for (const set of deviceStreams.values()) for (const res of set) res.write(': ping\n\n');
}, 15000);

function handleRemote(req, res, route, query) {
    if (route === '' || route === 'index.html') {
        return send(res, 200, 'text/html; charset=utf-8', dashboardHtml);
    }
    if (route === 'client.js') {
        return send(res, 200, 'application/javascript; charset=utf-8', clientJs);
    }
    if (route === 'setup') {
        return send(res, 200, 'text/html; charset=utf-8', setupHtml(req));
    }
    if (route === 'ca.crt') {
        if (!caPath || !fs.existsSync(caPath)) return send(res, 404, 'text/plain', 'No root CA. Generate the certificate in Unity first.');
        // iOS Safari는 이 형식이면 "프로파일 다운로드"로, Android는 인증서 파일로 받는다.
        return send(res, 200, 'application/x-x509-ca-cert', fs.readFileSync(caPath),
            { 'Content-Disposition': 'attachment; filename="WebLocalRemote-CA.crt"' });
    }
    if (route === 'log' && req.method === 'POST') {
        return readBody(req, 2 * 1024 * 1024, body => {
            if (!body || typeof body.id !== 'string') return send(res, 400, 'text/plain', 'bad request');
            const device = touchDevice(body.id.slice(0, 64), body.info, req);
            if (Array.isArray(body.entries)) addLogs(device, body.entries.slice(0, 500));
            send(res, 204, 'text/plain', '');
        });
    }
    if (route === 'logs') {
        const after = Number(query.get('after')) || 0;
        return send(res, 200, 'application/json', JSON.stringify({
            seq, logs: logs.filter(l => l.seq > after), devices: [...devices.values()]
        }));
    }
    if (route === 'clear' && req.method === 'POST') {
        logs.length = 0;
        broadcast('clear', {});
        return send(res, 204, 'text/plain', '');
    }
    if (route === 'events') {
        openStream(res);
        res.write(`event: init\ndata: ${JSON.stringify({ logs, devices: [...devices.values()] })}\n\n`);
        viewers.add(res);
        req.on('close', () => viewers.delete(res));
        return;
    }
    // 폰 페이지가 여는 명령 채널: 대시보드에서 입력한 JS를 그 기기에서 실행한다.
    if (route === 'device-events') {
        const id = (query.get('id') || '').slice(0, 64);
        if (!id) return send(res, 400, 'text/plain', 'missing id');
        openStream(res);
        if (!deviceStreams.has(id)) deviceStreams.set(id, new Set());
        deviceStreams.get(id).add(res);
        touchDevice(id, null, req);
        req.on('close', () => {
            const set = deviceStreams.get(id);
            if (set) { set.delete(res); if (set.size === 0) deviceStreams.delete(id); }
            if (devices.has(id)) { devices.get(id).online = deviceStreams.has(id); broadcast('device', devices.get(id)); }
        });
        return;
    }
    if (route === 'eval' && req.method === 'POST') {
        return readBody(req, 256 * 1024, body => {
            if (!body || typeof body.code !== 'string') return send(res, 400, 'text/plain', 'bad request');
            const targets = body.id ? [body.id] : [...deviceStreams.keys()];
            let sent = 0;
            for (const id of targets) {
                const set = deviceStreams.get(id);
                if (!set) continue;
                for (const stream of set) stream.write(`event: eval\ndata: ${JSON.stringify({ code: body.code })}\n\n`);
                sent++;
            }
            send(res, 200, 'application/json', JSON.stringify({ sent }));
        });
    }
    send(res, 404, 'text/plain', 'Not Found');
}

// ---- 정적 파일 ---------------------------------------------------------------

const contentTypes = {
    '.wasm': 'application/wasm',
    '.js': 'application/javascript',
    '.mjs': 'application/javascript',
    '.json': 'application/json',
    '.webmanifest': 'application/manifest+json',
    '.data': 'application/octet-stream',
    '.bundle': 'application/octet-stream',
    '.unityweb': 'application/octet-stream',
    '.html': 'text/html; charset=utf-8',
    '.htm': 'text/html; charset=utf-8',
    '.css': 'text/css',
    '.png': 'image/png',
    '.jpg': 'image/jpeg',
    '.jpeg': 'image/jpeg',
    '.gif': 'image/gif',
    '.svg': 'image/svg+xml',
    '.ico': 'image/x-icon',
    '.webp': 'image/webp',
    '.mp4': 'video/mp4',
    '.webm': 'video/webm',
    '.mov': 'video/quicktime',
    '.mp3': 'audio/mpeg',
    '.wav': 'audio/wav',
    '.ogg': 'audio/ogg',
    '.txt': 'text/plain; charset=utf-8'
};

const injectTag = `<script src="${WLR}client.js"></script>`;

function injectClient(html) {
    // 다른 스크립트보다 먼저 console을 가로채야 로더 오류까지 잡힌다.
    const head = html.match(/<head[^>]*>/i);
    if (head) return html.replace(head[0], head[0] + injectTag);
    return injectTag + html;
}

const requestHandler = (req, res) => {
    // 1. 기존 서버 프로세스 안전 종료 엔드포인트
    if (req.url === '/stop-web-local-remote-server') {
        res.writeHead(200, { 'Access-Control-Allow-Origin': '*' });
        res.end('Server stopped');
        console.log("Stop request received. Shutting down...");
        server.close(() => {
            process.exit(0);
        });
        // 열린 SSE 연결이 close를 막지 않도록 잠시 뒤 강제 종료
        setTimeout(() => process.exit(0), 500);
        return;
    }

    const [rawPath, rawQuery] = req.url.split('?');
    let requestUrl;
    try { requestUrl = decodeURIComponent(rawPath); }
    catch { requestUrl = rawPath; }

    if (req.method === 'OPTIONS') {
        res.writeHead(204, {
            'Access-Control-Allow-Origin': '*',
            'Access-Control-Allow-Methods': 'GET, POST, OPTIONS',
            'Access-Control-Allow-Headers': 'Content-Type'
        });
        res.end();
        return;
    }

    if (requestUrl === '/__wlr') {
        res.writeHead(302, { Location: WLR });
        res.end();
        return;
    }
    if (requestUrl.startsWith(WLR)) {
        handleRemote(req, res, requestUrl.slice(WLR.length), new URLSearchParams(rawQuery || ''));
        return;
    }

    // 빌드 폴더 밖으로 나가는 경로(../) 차단
    const root = path.resolve(buildPath);
    let filePath = path.resolve(path.join(root, requestUrl === '/' ? 'index.html' : requestUrl));
    if (filePath !== root && !filePath.startsWith(root + path.sep)) {
        res.writeHead(403);
        res.end('Forbidden');
        return;
    }
    if (fs.existsSync(filePath) && fs.statSync(filePath).isDirectory()) {
        filePath = path.join(filePath, 'index.html');
    }

    // 파일이 물리적으로 존재하는지 확인 (없으면 404)
    if (!fs.existsSync(filePath)) {
        res.writeHead(404);
        res.end('File Not Found');
        return;
    }

    const headers = {};

    // 2. SharedArrayBuffer 멀티스레딩(COOP, COEP, CORP) 헤더 설정
    if (enableWasmMultithreading) {
        headers['Cross-Origin-Opener-Policy'] = 'same-origin';
        headers['Cross-Origin-Embedder-Policy'] = 'require-corp';
        headers['Cross-Origin-Resource-Policy'] = 'cross-origin';
    }

    // 3. CORS 권한 헤더
    if (enableCORS) {
        headers['Access-Control-Allow-Origin'] = '*';
    }

    // 4. 압축 파일: 브라우저가 그 인코딩을 받는다고 하면(HTTPS) Content-Encoding으로 그대로 보내고,
    // 아니면(모바일 HTTP 등) 서버에서 풀어서 보낸다. 브라우저는 HTTP에서 br을 받지 않는다.
    let encoding = null;
    if (filePath.endsWith('.br')) encoding = 'br';
    else if (filePath.endsWith('.gz')) encoding = 'gzip';
    const accepts = (req.headers['accept-encoding'] || '').split(',').map(s => s.trim().split(';')[0]);
    const passThrough = encoding && accepts.includes(encoding);

    // 5. 요청 파일 종류에 따른 명시적 Content-Type 설정 (압축 확장자는 떼고 판단)
    const baseName = encoding ? filePath.slice(0, -3) : filePath;
    headers['Content-Type'] = contentTypes[path.extname(baseName).toLowerCase()] || 'application/octet-stream';

    // 6. 캐시 방지 (테스트 서버 용도이므로 무조건 덮어씌움)
    headers['Cache-Control'] = 'no-store, no-cache, must-revalidate, max-age=0';

    const isHtml = !encoding && /\.html?$/i.test(filePath);

    if (encoding && !passThrough) {
        fs.readFile(filePath, (err, data) => {
            if (err) {
                res.writeHead(500);
                res.end('Server Error: ' + err.code);
                return;
            }
            const done = (err, decompressed) => {
                if (err) {
                    res.writeHead(500);
                    res.end((encoding === 'br' ? 'Brotli' : 'Gzip') + ' Decompression Failed on Server');
                    return;
                }
                res.writeHead(200, headers);
                res.end(decompressed);
            };
            if (encoding === 'br') zlib.brotliDecompress(data, done);
            else zlib.gunzip(data, done);
        });
        return;
    }

    if (passThrough) headers['Content-Encoding'] = encoding;

    if (isHtml) {
        fs.readFile(filePath, 'utf8', (err, html) => {
            if (err) {
                res.writeHead(500);
                res.end('Server Error: ' + err.code);
                return;
            }
            res.writeHead(200, headers);
            res.end(injectClient(html));
        });
        return;
    }

    // 7. Range 요청: iOS Safari는 206 응답을 주지 않으면 <video>를 재생하지 않는다.
    const size = fs.statSync(filePath).size;
    const range = !passThrough && req.headers.range && req.headers.range.match(/^bytes=(\d*)-(\d*)$/);
    headers['Accept-Ranges'] = passThrough ? 'none' : 'bytes';
    if (range) {
        let start = range[1] === '' ? size - Number(range[2]) : Number(range[1]);
        let end = range[1] === '' || range[2] === '' ? size - 1 : Number(range[2]);
        end = Math.min(end, size - 1);
        if (isNaN(start) || start < 0 || start > end) {
            res.writeHead(416, { 'Content-Range': `bytes */${size}` });
            res.end();
            return;
        }
        headers['Content-Range'] = `bytes ${start}-${end}/${size}`;
        headers['Content-Length'] = end - start + 1;
        res.writeHead(206, headers);
        if (req.method === 'HEAD') return res.end();
        fs.createReadStream(filePath, { start, end }).pipe(res);
        return;
    }

    headers['Content-Length'] = size;
    res.writeHead(200, headers);
    if (req.method === 'HEAD') return res.end();
    fs.createReadStream(filePath).on('error', () => res.destroy()).pipe(res);
};

// ---- 폰 페이지에 주입하는 클라이언트 ---------------------------------------------

const clientJs = `(function () {
  if (window.__wlr) return;
  var base = '${WLR}';
  var id;
  try { id = sessionStorage.getItem('__wlr_id'); } catch (e) {}
  if (!id) {
    id = Math.random().toString(36).slice(2, 10);
    try { sessionStorage.setItem('__wlr_id', id); } catch (e) {}
  }
  var queue = [], timer = null, sending = false;
  var original = {};

  function format(value) {
    if (typeof value === 'string') return value;
    if (value instanceof Error) return (value.stack && value.stack.indexOf(value.message) >= 0) ? value.stack : value.name + ': ' + value.message + (value.stack ? '\\n' + value.stack : '');
    if (value === undefined) return 'undefined';
    if (typeof value === 'function') return value.toString().slice(0, 200);
    if (typeof Element !== 'undefined' && value instanceof Element) return value.outerHTML.slice(0, 500);
    try {
      var seen = [];
      return JSON.stringify(value, function (k, v) {
        if (typeof v === 'object' && v !== null) { if (seen.indexOf(v) >= 0) return '[Circular]'; seen.push(v); }
        if (typeof v === 'bigint') return v.toString() + 'n';
        return v;
      }, 2);
    } catch (e) { return String(value); }
  }

  function push(level, args) {
    var parts = [];
    for (var i = 0; i < args.length; i++) parts.push(format(args[i]));
    queue.push({ level: level, time: Date.now(), message: parts.join(' ') });
    if (queue.length > 2000) queue.splice(0, queue.length - 2000);
    if (!timer) timer = setTimeout(flush, 200);
  }

  function info() {
    var nav = navigator, scr = window.screen || {};
    return {
      userAgent: nav.userAgent,
      url: location.href,
      screen: (scr.width || 0) + 'x' + (scr.height || 0) + ' @' + (window.devicePixelRatio || 1) + 'x',
      viewport: window.innerWidth + 'x' + window.innerHeight,
      secure: window.isSecureContext,
      isolated: window.crossOriginIsolated,
      memory: nav.deviceMemory || null,
      cores: nav.hardwareConcurrency || null,
      webgpu: !!nav.gpu
    };
  }

  var sentInfo = false;
  function flush(beacon) {
    timer = null;
    if (sending && !beacon) { timer = setTimeout(flush, 200); return; }
    if (!queue.length && sentInfo) return;
    var batch = queue.splice(0, 300);
    var body = JSON.stringify({ id: id, info: sentInfo ? null : info(), entries: batch });
    sentInfo = true;
    if (beacon && navigator.sendBeacon) { navigator.sendBeacon(base + 'log', new Blob([body], { type: 'application/json' })); return; }
    sending = true;
    fetch(base + 'log', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: body, keepalive: body.length < 60000 })
      .catch(function () { queue = batch.concat(queue); })
      .then(function () { sending = false; if (queue.length) timer = setTimeout(flush, 200); });
  }

  ['log', 'info', 'warn', 'error', 'debug'].forEach(function (level) {
    var fn = console[level];
    original[level] = fn;
    console[level] = function () {
      try { push(level, arguments); } catch (e) {}
      return fn.apply(console, arguments);
    };
  });

  window.addEventListener('error', function (e) {
    if (e.error) push('error', ['Uncaught', e.error]);
    else if (e.target && e.target !== window) push('error', ['Failed to load ' + (e.target.src || e.target.href || e.target.tagName)]);
    else push('error', ['Uncaught ' + e.message + ' (' + e.filename + ':' + e.lineno + ':' + e.colno + ')']);
  }, true);
  window.addEventListener('unhandledrejection', function (e) { push('error', ['Unhandled rejection', e.reason]); });
  window.addEventListener('pagehide', function () { flush(true); });
  document.addEventListener('visibilitychange', function () { push('info', ['[page] ' + document.visibilityState]); });

  // 대시보드에서 보낸 JS를 이 기기에서 실행하고 결과를 콘솔로 돌려준다.
  function connect() {
    if (!window.EventSource) return;
    var es = new EventSource(base + 'device-events?id=' + encodeURIComponent(id));
    es.addEventListener('eval', function (e) {
      var code = JSON.parse(e.data).code;
      push('command', [code]);
      try {
        var result = (0, eval)(code);
        if (result && typeof result.then === 'function') result.then(function (v) { push('result', [v]); }, function (err) { push('error', [err]); });
        else push('result', [result]);
      } catch (err) { push('error', [err]); }
    });
  }

  window.__wlr = { id: id, flush: flush, original: original };
  push('info', ['[page] ' + location.href]);
  flush();
  connect();
})();
`;

// ---- 대시보드 ----------------------------------------------------------------

const dashboardHtml = `<!doctype html>
<html lang="ko">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Web Remote Console</title>
<style>
:root { --bg:#1e1e1e; --panel:#252526; --line:#333; --text:#ddd; --dim:#888; --warn:#e2c08d; --warnbg:#332b00; --err:#f48771; --errbg:#3a1d1d; --info:#75beff; --ok:#89d185; }
* { box-sizing: border-box; }
body { margin:0; background:var(--bg); color:var(--text); font:13px/1.45 -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif; height:100vh; display:flex; flex-direction:column; }
header { display:flex; gap:8px; align-items:center; padding:8px 12px; background:var(--panel); border-bottom:1px solid var(--line); flex-wrap:wrap; }
header h1 { font-size:14px; margin:0 8px 0 0; }
select, input, button { background:#3c3c3c; color:var(--text); border:1px solid #555; border-radius:4px; padding:4px 8px; font:inherit; }
button { cursor:pointer; } button:hover { background:#4a4a4a; }
label { color:var(--dim); display:flex; gap:4px; align-items:center; }
main { flex:1; display:flex; min-height:0; }
#devices { width:250px; border-right:1px solid var(--line); overflow:auto; padding:8px; flex-shrink:0; }
.device { padding:8px; border-radius:6px; margin-bottom:6px; background:var(--panel); cursor:pointer; border:1px solid transparent; }
.device.selected { border-color:var(--info); }
.device .name { font-weight:600; display:flex; align-items:center; gap:6px; }
.device .dot { width:8px; height:8px; border-radius:4px; background:#666; } .device.online .dot { background:var(--ok); }
.device .meta { color:var(--dim); font-size:11px; word-break:break-all; margin-top:2px; }
#log { flex:1; overflow:auto; font:12px/1.5 ui-monospace, Menlo, Consolas, monospace; }
.row { display:flex; gap:8px; padding:2px 10px; border-bottom:1px solid #2a2a2a; white-space:pre-wrap; word-break:break-word; }
.row .t { color:var(--dim); flex-shrink:0; } .row .d { color:var(--info); flex-shrink:0; min-width:70px; } .row .m { flex:1; }
.row.warn { background:var(--warnbg); color:var(--warn); } .row.error { background:var(--errbg); color:var(--err); }
.row.debug .m { color:var(--dim); } .row.command .m { color:var(--info); } .row.command .m::before { content:"› "; }
.row.result .m { color:var(--ok); } .row.result .m::before { content:"‹ "; }
footer { display:flex; gap:8px; padding:8px; border-top:1px solid var(--line); background:var(--panel); }
footer input { flex:1; font-family:ui-monospace, Menlo, Consolas, monospace; }
.empty { color:var(--dim); padding:20px; }
@media (max-width: 700px) { #devices { display:none; } }
</style>
</head>
<body>
<header>
  <h1>Web Remote Console</h1>
  <select id="level">
    <option value="all">모든 레벨</option>
    <option value="warn">경고 이상</option>
    <option value="error">오류만</option>
  </select>
  <input id="filter" placeholder="필터 (텍스트)" size="20">
  <label><input type="checkbox" id="follow" checked> 자동 스크롤</label>
  <button id="clear">지우기</button>
  <a id="setup" href="setup" style="color:var(--info); margin-left:auto;">기기 설정 / 인증서</a>
</header>
<main>
  <div id="devices"><div class="empty">연결된 기기가 없습니다.<br>폰에서 빌드 페이지를 열면 여기 나타납니다.</div></div>
  <div id="log"></div>
</main>
<footer>
  <input id="code" placeholder="선택한 기기(없으면 모두)에서 실행할 JS — 예: location.reload()">
  <button id="run">실행</button>
</footer>
<script>
var logs = [], devices = {}, selected = null;
var logEl = document.getElementById('log'), devEl = document.getElementById('devices');
var levelEl = document.getElementById('level'), filterEl = document.getElementById('filter'), followEl = document.getElementById('follow');
var rank = { debug:0, log:1, info:1, command:1, result:1, warn:2, error:3 };

function visible(l) {
  if (selected && l.device !== selected) return false;
  var min = levelEl.value === 'error' ? 3 : levelEl.value === 'warn' ? 2 : 0;
  if ((rank[l.level] || 1) < min) return false;
  var f = filterEl.value.toLowerCase();
  return !f || l.message.toLowerCase().indexOf(f) >= 0;
}
function row(l) {
  var d = document.createElement('div');
  d.className = 'row ' + l.level;
  var t = new Date(l.time);
  d.innerHTML = '<span class="t"></span><span class="d"></span><span class="m"></span>';
  d.children[0].textContent = t.toTimeString().slice(0, 8) + '.' + String(t.getMilliseconds()).padStart(3, '0');
  d.children[1].textContent = l.name + ' ' + l.device.slice(0, 4);
  d.children[2].textContent = l.message;
  return d;
}
function renderLogs() {
  logEl.textContent = '';
  var frag = document.createDocumentFragment();
  logs.filter(visible).forEach(function (l) { frag.appendChild(row(l)); });
  logEl.appendChild(frag);
  if (followEl.checked) logEl.scrollTop = logEl.scrollHeight;
}
function addLog(l) {
  logs.push(l);
  if (logs.length > 5000) logs.shift();
  if (!visible(l)) return;
  logEl.appendChild(row(l));
  while (logEl.childElementCount > 5000) logEl.firstChild.remove();
  if (followEl.checked) logEl.scrollTop = logEl.scrollHeight;
}
function renderDevices() {
  var list = Object.values(devices).sort(function (a, b) { return (b.last || 0) - (a.last || 0); });
  if (!list.length) return;
  devEl.textContent = '';
  list.forEach(function (d) {
    var el = document.createElement('div');
    el.className = 'device' + (d.online ? ' online' : '') + (selected === d.id ? ' selected' : '');
    var i = d.info || {};
    el.innerHTML = '<div class="name"><span class="dot"></span><span></span></div><div class="meta"></div>';
    el.querySelector('.name span:last-child').textContent = d.name + ' · ' + d.id.slice(0, 4);
    el.querySelector('.meta').textContent = [d.address, i.screen, i.viewport && ('창 ' + i.viewport),
      'secure ' + i.secure + ' · isolated ' + i.isolated, i.webgpu ? 'WebGPU' : 'WebGPU 없음', d.ua].filter(Boolean).join('\\n');
    el.style.whiteSpace = 'pre-wrap';
    el.onclick = function () { selected = selected === d.id ? null : d.id; renderDevices(); renderLogs(); };
    devEl.appendChild(el);
  });
}
levelEl.onchange = filterEl.oninput = renderLogs;
document.getElementById('clear').onclick = function () { fetch('clear', { method: 'POST' }); };
function run() {
  var code = document.getElementById('code').value.trim();
  if (!code) return;
  fetch('eval', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ id: selected, code: code }) })
    .then(function (r) { return r.json(); })
    .then(function (r) { if (!r.sent) addLog({ seq: 0, device: '-', name: 'console', level: 'warn', time: Date.now(), message: '실행할 기기가 연결되어 있지 않습니다.' }); });
}
document.getElementById('run').onclick = run;
document.getElementById('code').onkeydown = function (e) { if (e.key === 'Enter') run(); };

var es = new EventSource('events');
es.addEventListener('init', function (e) {
  var d = JSON.parse(e.data);
  logs = d.logs; devices = {};
  d.devices.forEach(function (x) { devices[x.id] = x; });
  renderDevices(); renderLogs();
});
es.addEventListener('log', function (e) { addLog(JSON.parse(e.data)); });
es.addEventListener('device', function (e) { var d = JSON.parse(e.data); devices[d.id] = d; renderDevices(); });
es.addEventListener('clear', function () { logs = []; renderLogs(); });
</script>
</body>
</html>`;

// ---- 기기 설정 페이지 (QR로 연다) ------------------------------------------------

function setupHtml(req) {
    const ua = req.headers['user-agent'] || '';
    const ios = /iPhone|iPad/.test(ua) || (/Macintosh/.test(ua) && /Mobile/.test(ua));
    const android = /Android/.test(ua);
    const secure = !!req.socket.encrypted;
    const hasCa = caPath && fs.existsSync(caPath);
    const host = req.headers.host || '';
    const app = `${secure ? 'https' : 'http'}://${host}/`;
    const iosSteps = `
<h2>iPhone / iPad</h2>
<ol>
  <li>아래 <b>인증서 받기</b>를 누르고 "허용"을 누릅니다. (Safari로 열어야 합니다)</li>
  <li><b>설정 앱 › 프로파일이 다운로드됨</b>(또는 일반 › VPN 및 기기 관리)에서 설치합니다.</li>
  <li><b>설정 › 일반 › 정보 › 인증서 신뢰 설정</b>에서 "Web Local Remote CA"를 켭니다.</li>
  <li>USB로 Mac Safari 개발자 도구를 쓰려면 <b>설정 › Safari › 고급 › 웹 속성 보기</b>를 켭니다. (선택)</li>
</ol>`;
    const androidSteps = `
<h2>Android</h2>
<ol>
  <li>아래 <b>인증서 받기</b>로 파일을 받습니다.</li>
  <li><b>설정 › 보안 › 암호화 및 사용자 인증 정보 › 인증서 설치 › CA 인증서</b>에서 받은 파일을 고릅니다.<br>
      (기종마다 메뉴 이름이 조금 다릅니다. 설정 검색에서 "CA 인증서"를 찾으면 빠릅니다.)</li>
  <li>Chrome을 완전히 닫았다 다시 엽니다.</li>
  <li>USB로 Chrome 개발자 도구를 쓰려면 개발자 옵션의 <b>USB 디버깅</b>을 켭니다. (선택)</li>
</ol>`;
    return `<!doctype html>
<html lang="ko"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>Web Remote 설정</title>
<style>
body { font:16px/1.55 -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif; margin:0; padding:20px 16px 40px; background:#f5f5f7; color:#1d1d1f; }
main { max-width:560px; margin:0 auto; }
h1 { font-size:22px; } h2 { font-size:18px; margin-top:28px; }
.card { background:#fff; border-radius:12px; padding:14px 16px; margin:12px 0; box-shadow:0 1px 3px rgba(0,0,0,.08); }
.btn { display:block; text-align:center; background:#0071e3; color:#fff; text-decoration:none; padding:12px; border-radius:10px; font-weight:600; margin:10px 0; }
.btn.secondary { background:#e8e8ed; color:#1d1d1f; }
.status { font-size:14px; }
.ok { color:#1a7f37; } .bad { color:#c62828; }
ol { padding-left:20px; } li { margin:6px 0; }
@media (prefers-color-scheme: dark) { body { background:#000; color:#f5f5f7; } .card { background:#1c1c1e; } .btn.secondary { background:#2c2c2e; color:#f5f5f7; } }
</style></head>
<body><main>
<h1>Web Remote 기기 설정</h1>
<div class="card status">
  연결: <b class="${secure ? 'ok' : 'bad'}">${secure ? 'HTTPS' : 'HTTP (카메라·압축 빌드 제한)'}</b><br>
  보안 컨텍스트: <b id="secure"></b> · 교차 출처 격리: <b id="isolated"></b>
</div>
${hasCa ? `<a class="btn" href="ca.crt">인증서 받기</a>` : `<div class="card bad">루트 인증서가 없습니다. Unity의 Web Local Remote 설정에서 인증서를 생성하세요.</div>`}
<a class="btn secondary" href="${app}">빌드 열기</a>
<p style="font-size:14px; color:#86868b;">인증서는 한 번만 설치하면 됩니다. 설치 후에는 주소창 경고 없이 카메라, 서비스 워커, Brotli 빌드가 동작합니다.
폰의 콘솔 로그는 PC의 <b>/__wlr/</b> 대시보드와 Unity 콘솔에 자동으로 나타납니다.</p>
${ios ? iosSteps : android ? androidSteps : iosSteps + androidSteps}
</main>
<script>
document.getElementById('secure').textContent = window.isSecureContext ? '예' : '아니오';
document.getElementById('isolated').textContent = window.crossOriginIsolated ? '예' : '아니오';
</script>
</body></html>`;
}

// ---- 서버 시작 ---------------------------------------------------------------

let protocol = 'http';

if (certPath && keyPath && fs.existsSync(certPath) && fs.existsSync(keyPath)) {
    try {
        const options = {
            cert: fs.readFileSync(certPath),
            key: fs.readFileSync(keyPath)
        };
        server = https.createServer(options, requestHandler);
        protocol = 'https';
    } catch (err) {
        console.error("Failed to load certificates, falling back to HTTP:", err);
        server = http.createServer(requestHandler);
    }
} else {
    server = http.createServer(requestHandler);
}

server.listen(port, '0.0.0.0', () => {
    console.log(`NK Studio Web Local Remote Server running at ${protocol}://0.0.0.0:${port}/ (${os.hostname()})`);
    console.log(`Remote console: ${protocol}://localhost:${port}${WLR}`);
});
