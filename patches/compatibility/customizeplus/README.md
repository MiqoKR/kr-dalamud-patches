# Customize+ KR 캐릭터 인식

대상: **Customize+ 2.2.0.3**

이 모듈은 프로필의 캐릭터 조건에 한국어 단일 이름과 한국 서비스 월드를 사용할 수 있게 합니다. Customize+ 자체의 외형 데이터나 프로필 파일을 수정하지 않고, 의존 라이브러리의 캐릭터 식별 검증만 보정합니다.

## 적용 전

- 게임과 Dalamud/XIVLauncher를 모두 종료합니다.
- Customize+ 2.2.0.3이 정상 설치되어 있어야 합니다.
- 한 캐릭터에 여러 활성 프로필이 걸려 있으면 Customize+가 충돌 경고를 표시할 수 있습니다. 이는 패치 오류가 아니라 프로필 우선순위 설정 문제입니다.

## 사용

GitHub Release에서 `CustomizePlus.KR.Actor.Patcher-<version>.zip`을 내려받아 압축을 풀고 실행합니다.

1. `적용`을 선택합니다.
2. 대상 버전과 원본 파일 해시가 확인되면 자동 백업 후 패치합니다.
3. Dalamud를 다시 시작하고 프로필의 캐릭터 조건을 다시 지정합니다.

원본 복구가 필요하면 같은 실행 파일에서 `복원`을 선택합니다.

## 지원 범위와 안전장치

- 지원 버전: `Customize+ 2.2.0.3`
- 원본 `CustomizePlus.dll`, `Penumbra.GameData.dll`의 SHA-256을 모두 확인합니다.
- 백업 위치: `%APPDATA%\\XIVLauncherKR\\kr-patch-backups\\CustomizePlus\\2.2.0.3\\<timestamp>`
- 미검증 버전, 이미 다른 방식으로 수정된 파일, 실행 중인 게임/런처 환경에서는 적용하지 않습니다.

## 개발자 검증

```powershell
.\scripts\Build-CustomizePlusKrActorPatcher.ps1
.\dist\CustomizePlusKrActorPatcher\CustomizePlus.KR.Actor.Patcher.exe --test-verify <plugin-directory> <hook-directory>
```

`--test-patch`, `--test-verify`, `--test-discover`는 배포 전 검증용 CLI입니다. 일반 사용자는 GUI만 사용하면 됩니다.
