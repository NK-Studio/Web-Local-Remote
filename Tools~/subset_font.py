"""창(UXML/C#)에 쓰는 글자만 남긴 IBM Plex Sans KR 서브셋을 Fonts/에 다시 만든다.

창의 문구를 바꾸면 새 글자가 빠지지 않도록 이 스크립트를 다시 실행한다.
원본 폰트: https://github.com/IBM/plex (SIL OFL 1.1, Fonts/OFL.txt)

    pip install fonttools
    python3 Tools~/subset_font.py /path/to/IBM_Plex_Sans_KR
"""
import re
import sys
import unicodedata
import uuid
from pathlib import Path

from fontTools import subset
from fontTools.ttLib import TTFont

WEIGHTS = ["Regular", "SemiBold"]
# 경로, URL, 숫자가 그대로 보이도록 ASCII 전체와 버튼에 쓰는 기호
EXTRA = "".join(chr(c) for c in range(0x20, 0x7F)) + "▶■·…×—–‘’“”•→←"


def collect(package: Path) -> set[str]:
    chars = set(EXTRA)
    editor = package / "Scripts" / "Editor"
    for path in [*editor.rglob("*.uxml"), *editor.rglob("*.cs")]:
        for ch in path.read_text(encoding="utf-8"):
            if ord(ch) >= 0x80 and not unicodedata.category(ch).startswith("C"):
                chars.add(ch)
    return chars


def main() -> None:
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    source = Path(sys.argv[1])
    package = Path(__file__).resolve().parent.parent
    fonts = package / "Fonts"

    chars = collect(package)
    cmap = TTFont(source / "IBMPlexSansKR-Regular.ttf").getBestCmap()
    missing = sorted(c for c in chars if ord(c) not in cmap)
    kept = "".join(sorted(c for c in chars if ord(c) in cmap))
    (fonts / "subset-characters.txt").write_text(kept, encoding="utf-8")

    # Unity는 SDF 아틀라스로 그리므로 힌팅은 필요 없다
    options = subset.Options()
    options.hinting = False
    for weight in WEIGHTS:
        font = TTFont(source / f"IBMPlexSansKR-{weight}.ttf")
        subsetter = subset.Subsetter(options)
        subsetter.populate(text=kept)
        subsetter.subset(font)
        out = fonts / f"IBMPlexSansKR-{weight}-Subset.ttf"
        font.save(out)
        # 글자 번호가 바뀐 폰트를 같은 GUID로 덮으면 에디터가 예전 글자표를 계속 써서 글자가 뒤섞인다
        # (도메인 리로드로도 안 풀림). 새 GUID를 주면 새 폰트로 읽는다. USS는 경로로 가리키므로 그대로 둔다.
        meta = out.with_name(out.name + ".meta")
        if meta.exists():
            meta.write_text(re.sub(r"guid: [0-9a-f]{32}", f"guid: {uuid.uuid4().hex}", meta.read_text()))
        print(f"{out.name}: {out.stat().st_size // 1024} KB")

    hangul = sum(1 for c in kept if 0xAC00 <= ord(c) <= 0xD7A3)
    print(f"{len(kept)} chars ({hangul} Hangul)")
    if missing:
        print("not in font (falls back to the editor font):", " ".join(f"U+{ord(c):04X}" for c in missing))


if __name__ == "__main__":
    main()
