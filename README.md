# KR Dalamud Patches

한국 서비스 환경에서 필요한 Dalamud 플러그인 호환성 패치를 배포하는 저장소입니다. 기본 배포 프로그램은 **`KR.Dalamud.PatchManager.exe`**이며, 한 화면에서 필요한 모듈만 선택해 적용하거나 원본으로 복원합니다.

패치 로직은 모듈별로 독립 검증·릴리스할 수 있게 유지하고, Patch Manager는 그 모듈을 하나의 실행 파일과 선택 UI로 묶습니다. 따라서 한 플러그인 업데이트가 다른 모듈의 검증 상태를 바꾸지 않습니다.

| 분류 | 모듈 | 상태 |
| --- | --- | --- |
| 호환성 | Customize+ KR 캐릭터 인식 | 검증 완료 · 첫 릴리스 준비 |
| 호환성 | Glamourer KR | 조사 예정 |
| KR 데이터 | BossModReborn KR | 조사 예정 |
| KR 데이터 | GatherBuddyReborn KR | 조사 예정 |

Patch Manager의 `검증 성공 조건` 열은 단순 파일 존재 여부가 아니라, 각 모듈이 실제로 검사한 패치 효과를 표시합니다.

| 모듈 | 검증 성공 조건 |
| --- | --- |
| Customize+ | 한국어 단일 캐릭터명과 KR 월드 ID 인식 |
| Glamourer | 한국어 캐릭터 조건과 CreateNewModel 호환 |
| BossModReborn | KR Lumina 시트 호출과 legacy map-effect 참조 제거 |
| GatherBuddyReborn | 언어 fallback 및 낚시 Regex fallback |

## Patch Manager 사용

1. 게임, XIVLauncher, Dalamud를 모두 종료합니다.
2. [Releases](https://github.com/MiqoKR/kr-dalamud-patches/releases)에서 `KR.Dalamud.PatchManager-<version>-win-x64.zip`을 받아 압축을 풉니다.
3. `KR.Dalamud.PatchManager.exe`를 실행하고 적용할 모듈을 선택합니다.
4. `선택 항목 적용` 또는 `선택 항목 복원`을 누릅니다.

매니저는 실제 설치 버전과 IL 패치 상태를 먼저 확인합니다. 이미 다른 도구로 패치돼 있지만 원본 백업이 없는 모듈은 현재 파일을 건드리지 않고 보호 상태로 표시합니다. 원본 플러그인을 재설치한 뒤 Patch Manager로 적용하면 백업·복원 관리가 시작됩니다.

## 현재 제공 모듈

`Customize+ KR 캐릭터 인식`은 한국어 단일 캐릭터명과 한국 서버 월드 ID를 Customize+ 프로필에서 인식하도록 보정합니다. Glamourer, BossModReborn, GatherBuddyReborn도 같은 실행 파일에서 독립 항목으로 처리합니다.

- 원본 플러그인 파일은 포함하거나 재배포하지 않습니다.
- 정확히 검증된 Customize+ 버전에만 적용합니다.
- 적용 전 원본을 `%APPDATA%\\XIVLauncherKR\\kr-patch-backups`에 보관합니다.
- 새 플러그인 버전은 해시가 달라 자동으로 거부됩니다. 해당 버전을 따로 검증한 뒤 새 릴리스로 지원합니다.

자세한 사용법은 [Customize+ 모듈 안내](patches/compatibility/customizeplus/README.md)를 참고하세요.

## 구조

```text
catalog/                         updater가 읽을 모듈 카탈로그
patches/                         사용자 문서와 모듈별 지원 정보
src/                             각 패처의 소스 코드
scripts/                         로컬 빌드 스크립트
.github/workflows/               모듈별 독립 GitHub 릴리스 자동화
```

`catalog/testing.json`은 검증 중인 모듈의 내부/테스트 채널입니다. 실제 배포 파일과 SHA-256이 확정된 모듈만 `catalog/stable.json`에 올립니다.

## 릴리스 원칙

1. 대상 플러그인 버전과 원본 SHA-256을 고정한다.
2. 실제 한국어 캐릭터·월드 환경에서 적용 및 복원을 검증한다.
3. 태그 `patch-<module>-vX.Y.Z`를 푸시해 해당 모듈만 릴리스한다.
4. 릴리스 ZIP과 SHA-256을 확인한 뒤 stable 카탈로그에 승격한다.

이 프로젝트는 Square Enix, Dalamud, 각 원본 플러그인 프로젝트와 무관한 비공식 호환성 도구입니다.
