# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
