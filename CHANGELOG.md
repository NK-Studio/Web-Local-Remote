# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.1.1] - 2026-10-27
### Fixed
- 설정한 포트를 다른 프로그램이 쓰고 있으면(127.0.0.1에만 묶인 것 포함) 서버가 켜지지 않거나, 폰의 `localhost`(adb reverse)가 그 프로그램으로 가던 문제. 이제 다음 빈 포트(최대 +50)로 켜서 알리고, 그 포트를 저장해 다음에도 같은 주소를 쓴다. 폰 브라우저의 카메라 권한이 origin 단위라 랜덤 포트는 쓰지 않는다.
- 이전 서버를 끄는 stop 요청을 포트가 사용 중일 때만 보낸다.
- 서버가 스스로 끝나면(포트 충돌 등) 창을 Offline으로 되돌리고, 서버가 이유를 남긴다.
- 서브셋 폰트를 다시 만들면 열려 있던 에디터에서 글자가 다른 글자로 뒤섞여 보이던 문제. `Tools~/subset_font.py`가 폰트에 새 GUID를 준다.

## [1.1.0] - 2026-10-26
### Added
- 원격 콘솔: 서버가 빌드의 HTML에 작은 스크립트를 넣어 폰의 `console` 로그, 오류, 처리되지 않은 Promise, 기기 정보를 PC로 보낸다. Android와 iPhone 모두 케이블 없이 `/__wlr/` 대시보드에서 실시간으로 보고, 선택한 기기에서 JS를 실행할 수 있다.
- 폰 로그를 Unity 콘솔로도 받는 옵션(`Unity 콘솔로 받기`). 창을 닫거나 도메인 리로드가 일어나도 이어서 받는다.
- 로컬 루트 CA와 그 CA로 서명한 서버 인증서(SAN: localhost, `<호스트>.local`, 현재 LAN IP) 자동 생성. IP가 바뀌면 서버를 켤 때 서버 인증서만 다시 발급한다.
- 기기 설정 페이지(`/__wlr/setup`)와 QR 전환(빌드 열기 / 기기 설정 · 인증서): 폰에 루트 CA를 한 번 설치하면 iPhone에서도 경고 없는 HTTPS가 된다.
- Android USB: `adb reverse`로 폰의 `localhost:포트`를 PC로 연결하고 폰 브라우저에서 빌드를 연다. Chrome DevTools(`chrome://inspect`) 바로 열기.
- iPhone USB(Mac): Safari 웹 속성 보기 안내와 Safari 열기.
- `이 PC에서 CA 신뢰`(설정 탭): 이 PC 브라우저에서 대시보드를 경고 없이 연다.
- 창 글꼴을 IBM Plex Sans KR(Regular/SemiBold, SIL OFL 1.1)로 바꿨다. 창에 쓰는 글자만 남긴 서브셋(`Fonts/`, 각 60KB)이며, 문구를 바꾸면 `Tools~/subset_font.py`로 다시 만든다. 굵은 글씨는 가짜 굵게 대신 SemiBold를 쓴다. 문구의 이모지 아이콘은 뺐다.
- 사용법 문서(`Documentation~/index.html`, CSS·JS 내장 단일 파일). 창 탭의 ⋮ 메뉴 › `사용법 (Documentation)`으로 연다.

### Changed
- 압축 빌드: 브라우저가 그 인코딩을 받으면(HTTPS) `Content-Encoding`으로 그대로 보내고, 아니면 전처럼 서버에서 풀어 보낸다.
- 정적 파일을 스트림으로 보내고 HTTP Range(206)를 지원한다. iOS Safari는 Range가 없으면 `<video>`를 재생하지 않는다.
- 창을 400×600 고정 크기에 서버 · 원격 디버그 · 설정 탭으로 다시 짰다. 탭마다 카드를 쌓고 마지막 카드가 남은 높이를 채우며, 서버가 꺼져 있어도 QR 자리는 안내 문구로 유지된다. 설정 탭에 인증서 상태(루트 CA, 만료일, 주소)를 보여 준다. ⚙️ 버튼은 설정 탭으로, 헤더의 `?`는 사용법 문서로 바뀌었다.
- 알림·확인 창을 `EditorUtility.DisplayDialog` 대신 창 안의 모달(`WebLocalRemoteDialog`)로 띄운다. Enter는 확인, Esc는 취소.
- 모든 응답에 COOP/COEP/CORP를 붙이고 영상·이미지 Content-Type을 추가했다.
- QR 주소는 기본 게이트웨이가 있는 인터페이스의 IPv4를 쓴다(VPN, 가상 어댑터 주소 제외).
- macOS에서 Homebrew OpenSSL을 찾는다.

### Fixed
- 빌드 폴더 밖 경로(`../`) 요청을 막는다.
- USS/UXML `.meta`를 현재 ScriptedImporter 형식(버전 2)으로 올렸다. Unity 6.7에서 "ScriptedImporter object at version 1" 경고가 나던 문제.
- USS에서 지원하지 않는 `:last-child` 선택자를 없앴다.
- Unity 6.7 분석기 경고(UAL0010/UAL0013): `RemoteConsoleBridge`에 `[NoAutoStaticsCleanup]` (6.7 이상에서만).

## [1.0.0] - 2026-04-09
### Added
- `Tools > Web Local Remote` 에디터 윈도우 인터페이스 제공.
- 유니티 내장 Node.js(Emscripten `node`)를 활용한 원클릭 WebGL 로컬 서버 구축 로직 추가 (별도의 설치 과정 자동 생략).
- 모바일에서 즉시 테스트 가능한 QR 코드 자동 생성기 도입 및 표시 UI 구성 (`ZXing` 사용).
- 최신 유니티 UI Toolkit을 활용한 세련된 그룹 박스(`GroupBoxStyle`)와 단일형 스위치 서버 토글 버튼(`ButtonStyle`) 디자인 도입.
- 유저 편의성을 위해 빌드 경로 값(`Build Path`)을 자동으로 저장하고 불러오는 `EditorPrefs` 캐싱 기능.
- "Web Remote" 타이틀 클릭 시 해당 에디터 코드(`WebLocalRemoteWindow.cs`)로 즉시 이동이 가능한 핫링크 훅 추가.

### Fixed
- 브라우저 상에서 컴파일 속도를 높이는 `.gz`, `.br` 압축파일이 로딩될 때 발생하는 `SyntaxError: Invalid character \u001f` 문제 해결 (`server.js`의 `Content-Encoding` 헤더 동적 주입 로직 개선).
- 유틸리티 환경에서 폰트 스타일 매핑 오류로 인해 텍스트 렌더링이 되지 않는 현상 제거.
- 에디터를 껐다 켰을 때도 `EditorPrefs`의 세션 PID를 추적하여 백그라운드 구동 중인 로컬 서버를 유실하지 않고 다시 연결하는 Recovery 논리 패치.
