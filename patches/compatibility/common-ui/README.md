# Dalamud 공통 UI / AtkResNode

대상: 한국 서비스 7.55 및 Dalamud Hook `15.0.3.0`

이 모듈은 개별 플러그인 DLL을 수정하지 않습니다. `FFXIVClientStructs.dll`에서 현재 `AtkResNode.IsVisible` 캐시 키를 확인하고, 실제 한섭 `ffxiv_dx11.exe`의 호출 지점을 분석해 공통 함수 RVA를 `cachedSigs/cs.json`에 기록합니다.

이 보정의 범위는 `AtkResNode.IsVisible` 주소 해석 실패뿐입니다. 플러그인별 언어 데이터, UI 텍스트·애드온 이름, 자동화 상태 등 다른 원인은 별도 호환 작업이 필요합니다.

## 안전 조건

- 한섭 호출 패턴이 2개 이상 발견되어야 합니다.
- 가장 많은 호출이 하나의 함수 RVA로 모여야 합니다.
- 대상 함수가 검증된 `AtkResNode` 표시 플래그 검사 구조와 일치해야 합니다.
- Dalamud Hook의 지원 게임 버전과 `cs.json`의 버전이 같아야 합니다.
- 실행 파일 옆 `ffxivgame.ver`와 Dalamud Hook·캐시 버전이 모두 같아야 합니다.
- 어느 조건이든 다르면 캐시를 수정하지 않습니다.

## 적용과 복원

적용 전 원본 `cs.json`은 `%APPDATA%\\XIVLauncherKR\\kr-patch-backups\\DalamudCommonUi`에 저장됩니다. `선택 항목 복원`은 이 백업을 그대로 되돌립니다.

기본 게임 경로는 `C:\\Program Files (x86)\\FINAL FANTASY XIV - KOREA\\game\\ffxiv_dx11.exe`입니다. 다른 폴더에 설치했다면 `KR_FFXIV_GAME_EXE` 환경 변수에 실행 파일 전체 경로를 지정할 수 있습니다.

Dalamud 업데이트로 Hook 폴더가 교체되면 새 Hook에 대해 다시 적용해야 합니다.
