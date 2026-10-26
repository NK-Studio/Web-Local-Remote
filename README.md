# 🌐 Web Local Remote

<p align="center">
  <img src="https://github.com/user-attachments/assets/3e1c15a3-e58a-42f8-bde0-f19c457e51ad" width="40%" />
  <img src="https://github.com/user-attachments/assets/10a79ef0-553e-4ecf-ba33-10f5078b792c" width="40%" />
</p>

**Web Local Remote**는 Unity 에디터에서 WebGL 빌드를 즉시 테스트할 수 있는 강력한 로컬 서버 확장 도구입니다. 네이티브 HTTPS 지원과 QR 코드를 통한 즉각적인 모바일 원격 디버깅 기능을 제공하며, 별도의 터널링 프로그램이나 복잡한 설정 없이 바로 사용할 수 있습니다.

---

## 🚀 주요 기능

*   **⚡ 설정이 필요 없는 서버**: 유니티 내장 Node.js 런타임을 활용하여 즉시 로컬 서버를 구동합니다.
*   **🔒 네이티브 HTTPS 지원**: 로컬 환경에서도 HTTPS를 지원하여 GZip/Brotli 압축 및 브라우저의 보안 컨텍스트가 필요한 기능을 테스트할 수 있습니다.
*   **📂 Root CA 및 SSL 자동 생성**: 단 한 번의 클릭으로 로컬 테스트용 self-signed 인증서를 자동으로 생성합니다.
*   **📱 QR 코드 원격 디버깅**: 생성된 QR 코드를 모바일 기기로 스캔하여 동일 네트워크상의 WebGL 빌드에 즉시 접속할 수 있습니다.
*   **🎨 현대적인 UI/UX**: UI Toolkit을 사용한 반응형 디자인과 부드러운 상태 전환 애니메이션이 적용된 프리미엄 에디터 윈도우를 제공합니다.
*   **🐞 원격 디버그 (Android · iPhone)**: 폰의 콘솔 로그와 오류를 PC 대시보드(`/__wlr/`)와 Unity 콘솔에서 실시간으로 보고, 폰에서 JS를 실행합니다. 케이블이 필요 없습니다.
*   **📱 기기 설정 QR**: 로컬 루트 CA를 폰에 한 번 설치하면 iPhone에서도 경고 없는 HTTPS(카메라, 서비스 워커, Brotli)가 됩니다.
*   **🔌 USB 개발자 도구**: Android는 `adb reverse` + Chrome DevTools, iPhone은 Mac Safari 웹 속성 보기를 버튼 하나로 엽니다.
*   **🛡️ Git 보안**: SSL 개인 키가 실수로 버전 관리 시스템에 올라가지 않도록 .gitignore 자동 안내 기능을 포함합니다.

---

## 📥 설치 방법 (Git UPM)

**Unity Package Manager**에서 **"Add package from git URL..."**을 선택한 후 아래 주소를 입력하세요:

```text
https://github.com/NK-Studio/Web-Local-Remote.git#upm
```

---

## 🛠️ 사용 방법

1.  **윈도우 열기**: 상단 메뉴의 `Tools > Web Local Remote`를 선택합니다.
2.  **빌드 폴더 선택**: **서버** 탭의 **Browse**로 테스트할 WebGL 빌드 폴더를 지정합니다.
3.  **HTTPS 설정 (선택 사항)**:
    *   **설정** 탭으로 갑니다.
    *   **로컬 CA + 인증서 생성** 버튼을 눌러 로컬 루트 CA와 서버 인증서를 만듭니다.
4.  **서버 실행**: **▶ Run** 버튼을 클릭하여 서버를 가동합니다.
5.  **스캔 및 테스트**: 화면에 나타난 QR 코드를 모바일 기기로 스캔하여 WebGL 게임을 즉시 테스트하세요!

---

## 🐞 원격 디버그

자세한 사용법은 창 탭의 **⋮ 메뉴 › 사용법 (Documentation)** 또는 `Documentation~/index.html`을 브라우저로 여세요.

1.  **설정** 탭에서 **로컬 CA + 인증서 생성**을 눌러 루트 CA와 서버 인증서를 만듭니다.
2.  서버를 실행하고 QR을 **기기 설정 · 인증서** 로 바꾼 뒤 폰으로 스캔해 루트 CA를 설치합니다 (기기당 한 번).
    *   **iPhone**: 프로파일 설치 후 `설정 › 일반 › 정보 › 인증서 신뢰 설정`에서 켭니다.
    *   **Android**: `설정 › 보안 › 인증서 설치 › CA 인증서`에서 받은 파일을 고릅니다.
3.  QR을 **빌드 열기** 로 돌려 스캔합니다. 폰의 로그는 **원격 디버그** 탭의 **원격 콘솔 열기**(PC 브라우저의 `/__wlr/`)와 Unity 콘솔에 나타납니다.
4.  브레이크포인트, 네트워크, 성능 패널이 필요하면 USB로 연결해
    *   **Android**: **원격 디버그** 탭의 **폰에서 열기**(adb reverse) → **Chrome DevTools**
    *   **iPhone (Mac)**: `설정 › Safari › 고급 › 웹 속성 보기`를 켜고 **Safari 웹 속성 보기**

> 다른 프로그램이 같은 포트를 `127.0.0.1`에서 쓰고 있으면 대시보드와 Unity 콘솔 연결이 그쪽으로 갈 수 있습니다. 설정에서 포트를 바꾸세요.

---

## 📜 요구 사양

*   **Unity**: 6000.2 이상 버전 권장 (Unity 6)
*   **Module**: WebGL Build Support 모듈이 설치되어 있어야 합니다.
*   **Platform**: Windows, macOS 지원 (Linux 지원 계획 없음)

---

## ⚖️ 라이선스

이 프로젝트는 MIT 라이선스에 따라 라이선스가 부과됩니다. 자세한 내용은 LICENSE 파일을 참조하십시오.

Developed with ❤️ by **NK Studio**
