# Glamourer 1.7.0.1 KR Actors 복구 성공 기록

## 검증 환경

- Dalamud `15.0.3.0`
- Glamourer `1.7.0.1`
- 한국 클라이언트

## 증상과 원인

Glamourer의 `Actors` 목록이 비어 있었지만 Debug 화면에서는 Player Character와 KR 월드 ID(`2076`)가 정상적으로 보였다. 동시에 `In Lobby = True`, `Number of Players = 0`으로 표시되었다.

원인은 KR Dalamud 15.0.3에서 `IClientState.IsLoggedIn`이 실제 접속 중에도 false가 되는 호환성 차이였다. Glamourer의 `ActorObjectManager`는 이를 로비 상태로 판정하여 일반 Actor 수집을 건너뛰었다.

## 실제 로드 대상

수정 대상은 공유 Penumbra가 아니라 Glamourer `1.7.0.1` 폴더가 직접 로드하는 자체 `Penumbra.GameData.dll`이다. 공유 DLL만 수정해도 해결로 판단하면 안 된다.

## 성공한 수정

`Penumbra.GameData.Interop.ActorObjectManager.AddLobbyCharacters`에서 `IsLoggedIn == false` 분기를 fallback 블록의 첫 명령으로 연결했다.

1. Player를 읽는다.
2. Player가 유효하지 않으면 기존 로비 경로를 유지한다.
3. HomeWorld가 0이면 기존 로비 경로를 유지한다.
4. 유효한 Player와 HomeWorld가 있으면 false를 반환하여 일반 Actor 수집을 계속한다.

핵심은 fallback 코드의 존재가 아니라, `get_IsLoggedIn` 뒤 false 분기가 실제로 그 fallback의 첫 명령(`ldarg.0 → get_Player → get_Valid → get_HomeWorld`)을 가리키는지이다. 이전 실패 패치는 fallback 코드가 있어도 분기가 기존 로비 경로로 향해 있어 도달할 수 없었다.

## 금지할 접근

- Dalamud `15.0.2`의 `FFXIVClientStructs.dll`을 `15.0.3`에 섞어 쓰지 않는다. 정적 참조는 통과할 수 있어도 게임에서 네이티브 예외 및 종료가 발생했다.
- Dalamud `ClientState.IsLoggedIn` 자체에 임시 fallback을 배포하지 않는다.
- 공유 Penumbra DLL만 보고 해결로 판정하지 않는다. Glamourer가 실제로 로드하는 자체 DLL의 분기를 검증한다.

## 패치 매니저 검증 조건

`get_HomeWorld` 호출 존재만으로는 부족하다. `get_IsLoggedIn`의 false 분기 대상이 fallback 첫 명령인지까지 검증해야 한다.

## 실제 확인 결과

수정 뒤 게임에서 Glamourer `Actors` 목록이 다시 표시되는 것을 사용자 확인으로 검증했다.
