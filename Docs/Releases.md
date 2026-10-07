# 릴리스

## v0.1.0-beta.6 — 게시 준비

- 생성일: 2026-10-07
- 소스 커밋: `3493e259c78c47810ebda0204094cf0855dd735a`
- 대상: Windows x64 자체 포함 단일 `Bough-v0.1.0-beta.6-win-x64.exe`
- 상태: 실행 파일 생성 완료, 게시에 필요한 커밋·태그 푸시 승인 대기
- 원격 실행·Revert 완료·충돌 파일 조회의 Presenter 분리 및 완료 재조회 호출 보완까지 포함한 최신 파일을 생성했다.

선택 커밋 취소(Revert)를 추가했다. History 메뉴에서 고정 저장소·HEAD·대상을 확인하고 새 커밋을 만들며 병합 커밋은 기준 부모를 직접 선택한다. 기존 저장소 FIFO에서 실행 직전에 상태를 다시 검사한다. 충돌 시 기존 해결 창에서 저장·스테이징한 뒤 계속하거나 영향 확인 후 중단한다. 저장소 재진입에도 진행 상태를 읽으며 미저장 초안을 보호한다.

전용 파일 히스토리, 커밋 파일의 기본 연결 프로그램 열기, 순차 백그라운드 History 미리보기와 파일 메뉴 대상 전달, 상세·이동 상태를 포함한다. 충돌 초안·늦은 저장소 응답 보호, Git 성공과 후속 조회 실패 구분, Stash 완료 처리 및 원격 영역 툴팁 제거도 반영했다. 원격 후속 조회 오류의 별도 진행창 반환·재시도 UI는 포함하지 않는다.

다음 명령으로 배포 파일을 생성했다. 이전 생성의 메뉴 클릭 연결 수정에 이어, 최신 Presenter 분리 소스로 다시 생성했다. 이번 생성에서 드러난 충돌 완료 재조회 호출을 새 Load Presenter로 연결한 뒤 생성을 완료했다. 별도 테스트·UI 실행·하네스·diff check·리소스 대조는 수행하지 않았다.

```powershell
dotnet publish Bough.App/Bough.App.csproj -c Release -r win-x64 --self-contained true -m:1 -nr:false -p:UseSharedCompilation=false -p:NuGetAudit=false -p:Version=0.1.0-beta.6 -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -p:OutputPath=D:\Source\Bough\.codex-build\release-v0.1.0-beta.6\build\ -o D:\Source\Bough\.codex-build\release-v0.1.0-beta.6\win-x64\
```

실행 파일에 .NET 런타임·필수 JSON·기본 로그 설정·네이티브 라이브러리를 포함한다. Git은 별도로 설치하며 기존 설정·최근 저장소 목록은 유지한다. macOS/Linux 배포 파일은 제공하지 않는다.

## v0.1.0-beta.5

- 배포일: 2026-10-03
- 소스 커밋: `6cf89ca`
- [릴리스 정보](https://github.com/EomTaeWook/Bough/releases/tag/v0.1.0-beta.5)
- [Windows x64 단일 실행파일 다운로드](https://github.com/EomTaeWook/Bough/releases/download/v0.1.0-beta.5/Bough-v0.1.0-beta.5-win-x64.exe)

저장소 열기·복제를 상단 `+` 메뉴로 옮기고 현재 저장소 헤더에서 최근 목록을 선택한다. 작업·상태 아이콘, 글씨 크기, 버튼 간격과 테마 색상을 정리했다. 설정에서 한국어·영어를 선택하면 즉시 적용하고 다음 실행에도 유지하며 OS UI 언어 감지는 제거했다. 파일 트리 진입 메뉴를 제거하고 명령·인터페이스·저장·UI 큐 보조 코드를 역할별 폴더로 분리했다. 한영 README의 실행 안내와 History·Local Changes·충돌 해결 이미지를 갱신했다.

다음 명령으로 배포 파일을 생성했다. README 이미지는 임시 프로필과 데모 저장소의 실제 앱 화면을 Avalonia 렌더링으로 저장했다. 별도 테스트·기능 및 회귀 검증은 수행하지 않았다.

```powershell
dotnet publish Bough.App/Bough.App.csproj -c Release -r win-x64 --self-contained true -m:1 -nr:false -p:UseSharedCompilation=false -p:NuGetAudit=false -p:Version=0.1.0-beta.5 -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -p:OutputPath=D:\Source\Bough\.codex-build\release-v0.1.0-beta.5\build\ -o D:\Source\Bough\.codex-build\release-v0.1.0-beta.5\win-x64\
```

출력 `Bough.exe`를 버전이 포함된 이름으로 게시했다. .NET 런타임·필수 JSON·기본 로그 설정·네이티브 라이브러리를 포함하며 Git은 별도로 설치한다. 기존 사용자 설정·최근 저장소 목록·로그 저장 정책과 이전 릴리스는 유지한다. macOS 배포 파일은 포함하지 않는다.

## v0.1.0-beta.4

- 배포일: 2026-10-03
- 소스 커밋: `ad5f414`
- [릴리스 정보](https://github.com/EomTaeWook/Bough/releases/tag/v0.1.0-beta.4)
- [Windows x64 단일 실행파일 다운로드](https://github.com/EomTaeWook/Bough/releases/download/v0.1.0-beta.4/Bough-v0.1.0-beta.4-win-x64.exe)

복제 실패의 종료 코드와 함께 Git 진단에서 식별한 인증·접근·연결·저장공간·파일 쓰기·목적지 충돌 원인을 표시한다. 원인을 식별하지 못한 경우에는 일반 실패 문구를 사용한다. 민감한 stderr·원격 주소·헤더는 표시·로그·예외 인수에 전달하지 않는다. 목적지 상태는 없음·빈 폴더·내용 남음·조회 실패로 구분하며 빈 폴더는 실패 원인을 해결한 뒤 같은 경로로 다시 시도할 수 있다고 안내한다. 자동 삭제는 하지 않는다.

배포 파일은 다음 명령으로 생성했다. 별도 테스트·복제 재현·UI 동작 검증은 수행하지 않았으며 제보된 종료 코드 128의 실제 원인은 미확정이다.

```powershell
dotnet publish Bough.App/Bough.App.csproj -c Release -r win-x64 --self-contained true -m:1 -nr:false -p:UseSharedCompilation=false -p:NuGetAudit=false -p:Version=0.1.0-beta.4 -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -p:OutputPath=D:\Source\Bough\.codex-build\release-v0.1.0-beta.4\build\ -o D:\Source\Bough\.codex-build\release-v0.1.0-beta.4\win-x64\
```

출력 `Bough.exe`를 버전이 포함된 이름으로 게시했으며 이전 릴리스도 보존한다. .NET 런타임은 포함하며 Git은 별도로 설치한다. macOS 배포 파일은 포함하지 않는다.

## v0.1.0-beta.3

- 배포일: 2026-10-02
- 소스 커밋: `c23b0bb`
- [릴리스 정보](https://github.com/EomTaeWook/Bough/releases/tag/v0.1.0-beta.3)
- [Windows x64 단일 실행파일 다운로드](https://github.com/EomTaeWook/Bough/releases/download/v0.1.0-beta.3/Bough-v0.1.0-beta.3-win-x64.exe)
- 다운로드한 실행파일을 바로 실행한다. 압축 해제·별도 .NET 설치는 필요 없으며 Git은 별도로 설치한다.

자체 포함 단일 실행파일, JSON·로그 기본 설정 임베딩, UI 간격과 대화상자 배치 통일, 저장소 목록 아래의 열기·복제 직접 진입을 포함한다. [공통 릴리스 정책](ReusableArchitecture/ReleasePolicy.md)을 적용한다. 기존 사용자 설정·저장소 목록·로그 저장 위치는 유지하며 이전 ZIP 릴리스도 보존한다. 이번 배포를 위한 별도 테스트·UI 동작 검증은 수행하지 않았다.

다음 명령으로 배포 파일을 생성했다.

```powershell
dotnet publish Bough.App/Bough.App.csproj -c Release -r win-x64 --self-contained true -m:1 -nr:false -p:UseSharedCompilation=false -p:NuGetAudit=false -p:Version=0.1.0-beta.3 -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -p:OutputPath=D:\Source\Bough\.codex-build\release-v0.1.0-beta.3\build\ -o D:\Source\Bough\.codex-build\release-v0.1.0-beta.3\win-x64\
```

출력 `Bough.exe`를 `Bough-v0.1.0-beta.3-win-x64.exe` 이름으로 게시했다. 실행 시 네이티브 라이브러리의 내부 추출은 런타임이 처리한다. macOS 배포 파일은 포함하지 않는다.

## v0.1.0-beta.2

- 배포일: 2026-10-02
- 소스 커밋: `13ee7a5`
- [릴리스 정보](https://github.com/EomTaeWook/Bough/releases/tag/v0.1.0-beta.2)
- [Windows x64 ZIP 다운로드](https://github.com/EomTaeWook/Bough/releases/download/v0.1.0-beta.2/Bough-v0.1.0-beta.2-win-x64.zip)
- 압축 해제 후 `Bough.exe` 실행. .NET 런타임은 포함하며 Git은 별도로 설치한다.

태그 삭제 메뉴·창 통합, 로컬 브랜치·태그 이름 변경, History 참조 배지 갱신, 복제 목적지 폴더 직접 지정과 사이드바·폴더 열기 개선을 포함한다. 이번 배포를 위한 별도 테스트·UI 검증은 수행하지 않았다.

배포물은 다음 명령으로 생성했다. 이 명령은 패키지 생성용이며 별도 테스트를 실행하지 않는다.

```powershell
dotnet publish Bough.App/Bough.App.csproj -c Release -r win-x64 --self-contained true -m:1 -nr:false -p:Version=0.1.0-beta.2 -p:PublishSingleFile=false -p:PublishTrimmed=false -p:OutputPath=D:\Source\Bough\.codex-build\release-v0.1.0-beta.2\build\ -o D:\Source\Bough\.codex-build\release-v0.1.0-beta.2\win-x64\
```

배포 ZIP에는 자체 포함 실행 파일과 런타임, `Datas` 문자열 데이터, 로그 설정, 라이선스와 한영 README·릴리스 노트를 넣었다. macOS 배포 파일은 포함하지 않는다.

## v0.1.0-beta.1

- [첫 Windows x64 베타 릴리스](https://github.com/EomTaeWook/Bough/releases/tag/v0.1.0-beta.1)
- 기존 ZIP 실행 파일명은 `Bough.App.exe`이다.
