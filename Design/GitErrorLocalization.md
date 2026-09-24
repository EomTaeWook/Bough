# Git 실행 오류 문구의 다국어 처리

전체 사용자 표시 문구와 Core 오류의 다국어 처리 규칙은 [코딩 컨벤션](../Docs/CodingConvention.md)을 따른다. 이 문서는 Git 실행 오류 두 유형의 처리 방식과 배경을 설명한다.

`GitCommandRunner`가 만드는 오류 문구를 C# 코드에 한국어로 고정하지 않는다. 실행 파일을 시작하지 못한 경우와 Git이 오류 문구 없이 종료한 경우에는 각각 안정적인 오류 식별자와 경로·종료 코드 같은 인수를 `GitException`에 담는다. 원래 예외는 `InnerException`으로 보존한다.

`Bough.Core`는 `Bough.App.Localization.StringHelper`나 UI 프로젝트를 참조하지 않는다. 앱의 표시 경계에서 오류 식별자를 `Datas/String.json`의 한국어·영어 템플릿으로 변환한다. 현재 언어 선택은 기존 `StringHelper`를 따른다. `GitException.Message`를 그대로 표시하는 Git 실행 오류 경로도 이 변환기를 사용해 실제 화면에서 선택 언어로 보이게 한다. 식별자가 없는 기존 예외와 Git 자체가 출력한 stderr는 원문을 진단 정보로 유지한다.

첫 대상 문구는 `Git을 실행하지 못했습니다: {0}` / `Could not start Git: {0}`와 `Git이 오류 문구 없이 종료했습니다(코드 {0}).` / `Git exited without an error message (code {0}).`이다. 번역 키가 누락되면 식별자나 빈 문자열 대신 읽을 수 있는 영어 기본 문구를 보여 준다. 이 정리는 Git 실행·오류 표시 흐름에만 적용하며 Git 명령 성공 여부나 실행 파일 설정을 바꾸지 않는다.
