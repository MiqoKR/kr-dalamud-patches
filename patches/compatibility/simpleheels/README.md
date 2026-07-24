# Simple Heels KR 안정성

지원 버전은 공식 Simple Heels `0.11.1.8`입니다.

한섭 Dalamud Hooks에서 찾을 수 없는 `EffectContainer.MountGroundTiltAngle` 및 `MountGroundTiltSpeed` 참조를 기존 호환 필드인 `TiltParam1Value`, `TiltParam2Value`로 바꿉니다. 또한 `CalculateFloatHeight` 네이티브 훅의 서명 등록을 제거합니다.

이 패치의 의도적인 제한은 **수영 중 높이 보정**을 사용하지 않는 것입니다. 그 외의 높이·회전·동반자 관련 로직은 유지합니다.

적용 전 Patch Manager가 `SimpleHeels.dll`을 `%APPDATA%\XIVLauncherKR\kr-patch-backups`에 백업하며, `선택 항목 복원`으로 원본을 되돌릴 수 있습니다. 공식 원본 SHA-256이 확인된 `0.11.1.8`에만 적용합니다.
