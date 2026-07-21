# 배포 규칙

- Windows .NET 릴리스는 반드시 framework-dependent 단일 실행 파일로 빌드한다.
- `--self-contained true` 및 .NET 런타임을 포함하는 배포는 사용하지 않는다.
- 필요한 런타임은 사용자가 별도로 설치하며, README에 필요한 Desktop Runtime 버전과 x64 요구 사항을 명시한다.
