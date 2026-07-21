# KR Dalamud Patches

한국 서비스 환경에서 필요한 Dalamud 플러그인 호환성 패치를 배포하는 저장소입니다.

이 저장소는 패치를 한 실행 파일에 억지로 합치지 않습니다. 패치 대상별로 독립 모듈·독립 릴리스·독립 검증을 유지하고, 이후 `kr-dalamud-updater`가 카탈로그를 읽어 사용자에게 필요한 모듈만 제안하는 구조입니다.

| 분류 | 모듈 | 상태 |
| --- | --- | --- |
| 호환성 | Customize+ KR 캐릭터 인식 | 검증 완료 · 첫 릴리스 준비 |
| 호환성 | Glamourer KR | 조사 예정 |
| KR 데이터 | BossModReborn KR | 조사 예정 |
| KR 데이터 | GatherBuddyReborn KR | 조사 예정 |

## 현재 제공 모듈

`Customize+ KR 캐릭터 인식`은 한국어 단일 캐릭터명과 한국 서버 월드 ID를 Customize+ 프로필에서 인식하도록 보정합니다.

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

