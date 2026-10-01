# 릴리스

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
