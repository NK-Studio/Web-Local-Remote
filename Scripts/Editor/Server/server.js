const http = require('http');
const https = require('https');
const fs = require('fs');
const path = require('path');
const zlib = require('zlib');

const buildPath = process.argv[2] || './';
const port = process.argv[3] || 8080;
const certPath = process.argv[4] || '';
const keyPath = process.argv[5] || '';

const enableWasmMultithreading = true;
const enableCORS = true;

let server;

const requestHandler = (req, res) => {
    // 1. 기존 서버 프로세스 안전 종료 엔드포인트
    if (req.url === '/stop-web-local-remote-server') {
        res.writeHead(200, { 'Access-Control-Allow-Origin': '*' });
        res.end('Server stopped');
        console.log("Stop request received. Shutting down...");
        server.close(() => {
            process.exit(0);
        });
        return;
    }

    // URL에서 쿼리 스트링 제거
    let requestUrl = req.url.split('?')[0];
    let filePath = path.join(buildPath, requestUrl === '/' ? 'index.html' : requestUrl);

    // 파일이 물리적으로 존재하는지 확인 (없으면 404)
    if (!fs.existsSync(filePath)) {
        res.writeHead(404);
        res.end('File Not Found');
        return;
    }

    const headers = {};

    // 2. SharedArrayBuffer 멀티스레딩(COOP, COEP, CORP) 헤더 설정
    if (enableWasmMultithreading && (
        requestUrl === '/' ||
        requestUrl.includes('.js') ||
        requestUrl.includes('.html') ||
        requestUrl.includes('.htm')
    )) {
        headers['Cross-Origin-Opener-Policy'] = 'same-origin';
        headers['Cross-Origin-Embedder-Policy'] = 'require-corp';
        headers['Cross-Origin-Resource-Policy'] = 'cross-origin';
    }

    // 3. CORS 권한 헤더
    if (enableCORS) {
        headers['Access-Control-Allow-Origin'] = '*';
    }

    // 4. 모바일 기기망 접속(HTTP) 시 브라우저 차단을 우회하기 위한 플래그 설정
    // Content-Encoding 헤더를 세팅하는 대신, 서버 내에서 실시간으로 압축을 풀어 전송합니다.
    let serverSideDecompress = null;
    if (requestUrl.endsWith('.br')) {
        serverSideDecompress = 'br';
    } else if (requestUrl.endsWith('.gz')) {
        serverSideDecompress = 'gzip';
    }

    // 5. 요청 파일 종류에 따른 명시적 Content-Type 설정
    // 파일명에 포함된 확장자를 기반으로 처리 (빌드가 압축을 사용할 때 Content-Type이 꼬이는 것 방지)
    if (requestUrl.includes('.wasm')) {
        headers['Content-Type'] = 'application/wasm';
    } else if (requestUrl.includes('.js')) {
        headers['Content-Type'] = 'application/javascript';
    } else if (requestUrl.includes('.json')) {
        headers['Content-Type'] = 'application/json';
    } else if (
        requestUrl.includes('.data') ||
        requestUrl.includes('.bundle') ||
        requestUrl.endsWith('.unityweb')
    ) {
        headers['Content-Type'] = 'application/octet-stream';
    } else if (requestUrl.endsWith('.html') || requestUrl.endsWith('.htm') || requestUrl === '/') {
        headers['Content-Type'] = 'text/html';
    } else if (requestUrl.endsWith('.css')) {
        headers['Content-Type'] = 'text/css';
    } else {
        headers['Content-Type'] = 'application/octet-stream';
    }

    // 6. 캐시 방지 (테스트 서버 용도이므로 무조건 덮어씌움)
    headers['Cache-Control'] = 'no-store, no-cache, must-revalidate, max-age=0';

    // 최종 파일 읽기 및 전송 (압축 파일인 경우 서버에서 자체적으로 압축을 풀어 전송)
    // 모바일 HTTP 브라우저들은 네트워크를 통해 오는 압축 응답을 보안상 거부하므로 이 우회 과정이 필수적입니다.
    fs.readFile(filePath, (err, data) => {
        if (err) {
            res.writeHead(500);
            res.end('Server Error: ' + err.code);
            return;
        }

        if (serverSideDecompress === 'br') {
            zlib.brotliDecompress(data, (err, decompressed) => {
                if (err) {
                    res.writeHead(500);
                    res.end('Brotli Decompression Failed on Server');
                    return;
                }
                res.writeHead(200, headers);
                res.end(decompressed);
            });
            return;
        } else if (serverSideDecompress === 'gzip') {
            zlib.gunzip(data, (err, decompressed) => {
                if (err) {
                    res.writeHead(500);
                    res.end('Gzip Decompression Failed on Server');
                    return;
                }
                res.writeHead(200, headers);
                res.end(decompressed);
            });
            return;
        }

        res.writeHead(200, headers);
        res.end(data);
    });
};

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
    console.log(`NK Studio Web Local Remote Server running at ${protocol}://0.0.0.0:${port}/`);
});