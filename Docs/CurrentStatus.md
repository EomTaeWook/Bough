# 구현 현황

이 문서는 현재 코드에 반영된 범위와 남은 검증을 구분한다. 기존 [구현 작업 지시](ImplementationPlan.md)는 기능별 원래 요구와 작업자 소유권을 기록한 문서이며, 항목의 명령형 문장만으로 미완료 여부를 판단하지 않는다.

## 반영된 기능

- 원격 URL 또는 로컬 경로에서 새 폴더로 저장소를 복제하고 최근 목록에 등록해 여는 흐름. Core와 저장소별 큐의 임시 bare 원격 복제는 확인했다.
- Local Changes의 diff·줄 선택, 스테이징·해제, 커밋, 변경 폐기·추적 중지·무시와 Stash 작업.
- History의 커밋 그래프·상세·파일 탐색과 브랜치 생성·전환·삭제, 태그 생성.
- Fetch·Pull·Push, 설정 가능한 기본 Pull 방식, 충돌 구간별·일괄 선택과 저장·스테이징, 리베이스 계속.
- 저장소별 FIFO Git 작업 큐, 한국어·영어 문자열 리소스, 밝음·어두움 테마.
- 1차 MVP 책임 분리: Local Changes·Stash·Git Settings, History·참조, 충돌 Save/Stage·원격 완료 흐름에 Core 서비스와 기능별 Presenter를 연결했다. 세부 경계는 [Avalonia MVP 적용](AvaloniaMvpAdoption.md)을 따른다.

## 남은 검증과 후속 정리

- [저장소 복제](../Design/RepositoryClone.md)의 입력·진행·취소·완료 창은 작업자 세션에서 실제 UI 조작과 캡처를 확인하지 못했다. 실패·취소 뒤에는 생성 위치의 현재 존재 여부를 표시하며, 그 파일의 생성 주체는 판별하지 않는다.
- Local Changes diff를 처음 열 때 텍스트가 자동 선택되지 않는지 실제 최신 앱 화면에서 확인해야 한다. 최신 DLL의 선택 상태와 복사 가능 여부는 확인했으나 작업자 세션에서 정상 창 캡처가 되지 않았다.
- Windows 외 플랫폼에서의 클론·터미널·경로 동작은 별도로 검증해야 한다.
- Conflict 파일 로드·일괄 선택, MainWindow 저장소 전환·공통 상태 문구, RemoteOperationsViewModel 내부 실행·문구 등에는 ViewModel 책임이 남아 있다. 1차 분리를 순수 MVP 완료로 표시하지 않는다.
