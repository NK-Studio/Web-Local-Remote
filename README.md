# 🌐 Web Local Remote

![Hero](web_local_remote_hero_1775753412251.png)

**Web Local Remote**는 Unity 에디터에서 WebGL 빌드를 즉시 테스트할 수 있는 강력한 로컬 서버 확장 도구입니다. 네이티브 HTTPS 지원과 QR 코드를 통한 즉각적인 모바일 원격 디버깅 기능을 제공하며, 별도의 터널링 프로그램이나 복잡한 설정 없이 바로 사용할 수 있습니다.

---

## 🚀 주요 기능

*   **⚡ 설정이 필요 없는 서버**: 유니티 내장 Node.js 런타임을 활용하여 즉시 로컬 서버를 구동합니다.
*   **🔒 네이티브 HTTPS 지원**: 로컬 환경에서도 HTTPS를 지원하여 GZip/Brotli 압축 및 브라우저의 보안 컨텍스트가 필요한 기능을 테스트할 수 있습니다.
*   **📂 Root CA 및 SSL 자동 생성**: 단 한 번의 클릭으로 로컬 테스트용 self-signed 인증서를 자동으로 생성합니다.
*   **📱 QR 코드 원격 디버깅**: 생성된 QR 코드를 모바일 기기로 스캔하여 동일 네트워크상의 WebGL 빌드에 즉시 접속할 수 있습니다.
*   **🎨 현대적인 UI/UX**: UI Toolkit을 사용한 반응형 디자인과 부드러운 상태 전환 애니메이션이 적용된 프리미엄 에디터 윈도우를 제공합니다.
*   **🛡️ Git 보안**: SSL 개인 키가 실수로 버전 관리 시스템에 올라가지 않도록 .gitignore 자동 안내 기능을 포함합니다.

---

## 📥 설치 방법 (Git UPM)

**Unity Package Manager**에서 **"Add package from git URL..."**을 선택한 후 아래 주소를 입력하세요:

```text
https://github.com/NK-Studio/Web-Local-Remote.git?path=/Assets/Plugins/Web Local Remote
```

> [!IMPORTANT]
> 이 패키지는 로컬 프로젝트 관리 편의를 위해 서브 폴더 내에 유지되고 있으므로, 주소 뒤의 `?path=` 쿼리 파라미터가 반드시 포함되어야 합니다.

---

## 🛠️ 사용 방법

1.  **윈도우 열기**: 상단 메뉴의 `Tools > Web Local Remote`를 선택합니다.
2.  **빌드 폴더 선택**: 테스트할 WebGL 빌드가 포함된 폴더를 지정합니다.
3.  **HTTPS 설정 (선택 사항)**:
    *   설정(⚙️) 아이콘을 클릭합니다.
    *   **"Auto Generate SSL (.cert)"** 버튼을 눌러 로컬 인증서를 생성합니다.
4.  **서버 실행**: **▶ Run** 버튼을 클릭하여 서버를 가동합니다.
5.  **스캔 및 테스트**: 화면에 나타난 QR 코드를 모바일 기기로 스캔하여 WebGL 게임을 즉시 테스트하세요!

---

## 📜 요구 사양

*   **Unity**: 6.2 이상 버전 권장
*   **Module**: WebGL Build Support 모듈이 설치되어 있어야 합니다.
*   **Platform**: Windows 지원 (MacOS/Linux 지원 예정)

---

## ⚖️ 라이선스

이 프로젝트는 MIT 라이선스에 따라 라이선스가 부과됩니다. 자세한 내용은 LICENSE 파일을 참조하십시오.

Developed with ❤️ by **NK Studio**
