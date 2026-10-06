# 구현 작업 지시

현재 작업자와 세션 전달 방식은 [AGENTS.md](../AGENTS.md), 완료·진행·미검증 상태는 [구현 현황](CurrentStatus.md)을 기준으로 한다. 이 문서는 담당 범위와 구현·인계 계약을 정의하며, 이미 구현한 기능을 다시 작업하라는 지시가 아니다.

## 공통 계약

- 사용자의 최신 요청과 총괄이 확정한 최종 계약을 적용한다. 이전 큐 메시지나 과거 컴파일 오류 보고를 근거로 완료된 변경을 되돌리지 않는다.
- **현재는 추가 검증을 수행하지 않는다.** 빌드·테스트·임시 저장소 하네스·UI 실행·캡처·정적 검사·diff check·리소스 대조는 사용자가 다시 요청한 범위에서만 수행한다. 필요한 파일 읽기·편집과 요청된 배포 파일 생성은 별도 검증으로 확대하지 않는다.
- 화면 책임은 [Avalonia MVP 적용](AvaloniaMvpAdoption.md)을 따른다. Git 검증·파일 I/O는 Core, 비동기·큐·결과 적용 순서는 Presenter, 바인딩 상태는 화면 모델, 표시 문구·입력 연결은 View에 둔다. 남은 레거시 이행은 기능 변경 범위에서 진행한다.
- [코딩 컨벤션](CodingConvention.md)을 따르고 `DataContainer/Generated`는 직접 수정하지 않는다. 비동기 요청 버전·저장소·선택 검사는 각각의 조건과 얼리 리턴으로 유지한다.
- 변경 명령은 [저장소별 Git 작업 큐](GitOperationQueue.md)에 연결하고 실행 직전에 고정 대상과 Git 상태를 다시 확인한다. 읽기 조회를 변경 큐에 넣어 화면 표시를 늦추지 않는다.
- 첫 화면은 Local Changes를 먼저 표시하고 다른 영역은 필요한 시점에 읽는다. 늦은 응답을 버리고 같은 상태의 중복 조회를 줄인다. [저장소 전환 응답성](../Design/RepositorySwitchResponsiveness.md)을 따른다.
- 결과·오류는 [공통 상태 메시지](../Design/GitClientWorkflow.md#공통-상태-메시지), 간격·버튼·메뉴는 [UI 일관성](../Design/UIConsistency.md)을 따른다. 입력 확인과 진행 상황은 해당 대화상자에서 표시한다.
- 사용자 문구는 코드에 고정하지 않고 최종 문자열 계약을 총괄에게 전달한다. 총괄이 [데이터 변환 규칙](<데이터 변환 도구 사용법.md>)에 따라 Excel·JSON을 함께 병합한다. Core는 오류 코드·인수만 전달하고 [표시 경계에서 번역](GitErrorLocalization.md)한다.

## 작업자 1: Local Changes, Stash, Git Settings

소유 범위는 해당 View·ViewModel·Presenter, 관련 확인·입력 창 및 Core 서비스다.

- Local Changes: 변경 목록, diff·줄 번호·선택·복사, Stage/Unstage, 커밋·Amend, 변경 버리기·미추적 파일 삭제·추적 중지·무시, 큰 파일 확인, 목록·미리보기·커밋 영역 크기 저장.
- Stash: 보관 입력 창과 대상 고정, 목록·미리보기, Apply/Pop/Drop, 부분 실패와 작업 트리 변경 여부에 따른 갱신 계약. 취소·Drop에서 작업 트리를 불필요하게 다시 읽지 않는다.
- 보관 창의 완료는 현재 요청 집합과 미해결 오류로 판정한다. `Succeeded`는 Git 변경 성공을 유지하고 `CompleteStashSaveAsync`는 후속 갱신 오류가 포함된 `StashMutationResult`를 반환한다. 보관 성공 뒤 갱신 재시도는 읽기만 수행한다. Unstaged 메뉴는 메뉴 시점 저장소·경로를 고정하고 무효 대상의 이유를 표시한다.
- Save 완료에 상태가 없으면 `Task<GitWorktreeStatus> RefreshStashSaveWorktreeAsync(GitRepository repository)`로 고정 저장소의 상태를 한 번 조회·적용하고 동일 스냅샷을 반환한다. 저장소·조회 요청 대체로 적용하지 않으면 null을 반환하고, 현재 조회·적용 실패는 예외를 전달한다. 이전 큐 작업 오류를 이번 조회 실패로 사용하지 않는다. Core 조회와 Presenter의 요청·적용 책임을 유지하며 Apply/Pop용 기존 API는 보존한다. 실제 구현·인계 상태는 CurrentStatus를 따른다.
- Git Settings: Git 실행 파일·작성자·인증 계정·테마·Bough 기본 Pull 방식과 앱 언어의 즉시 적용·저장. GitHub 계정 인증과 커밋 작성자를 구분하며 `credential.helper` 출처·값 목록은 노출하지 않는다.
- 표시 계약은 [화면과 동작](../Design/GitClientWorkflow.md), [원격과 Stash](../Design/RemoteAndStash.md), [GitHub 계정](../Design/GitHubAccountSwitching.md), [테마](../Design/AppearanceTheme.md)를 따른다.
- MainWindow와 원격 실행 파일은 수정하지 않고 갱신·상태·설정 변경 이벤트 계약을 총괄에게 전달한다.

## 작업자 2: History, Git 참조 및 관련 대화상자

소유 범위는 History·Reference View·ViewModel·Presenter, 그래프·상세 화면 항목, GitActionDialogs와 해당 Core 서비스다.

- History: 커밋 그래프·참조 배지, 전체/현재 브랜치 범위, 페이지 추가 조회, 상세·비교 부모·변경 파일·파일 내용, 파일 내보내기와 커밋 명령. [파일 히스토리](../Design/FileHistory.md)는 독립 창에서 파일 커밋 목록·날짜 위치·줄 번호 diff를 제공한다. 파일 트리의 내부 조회 구현과 현재 제공하지 않는 UI 진입을 구분한다.
- 커밋 이동 결과 계약은 `Task<HistoryCommitSelectionResult> SelectCommitAsync(string commitHash)`와 `SelectCommitAsync(GitRepository repository, string commitHash, GitHistoryScope scope)`다. 결과는 `RepositoryRoot`, `CommitHash`, `Scope`, `Outcome`, 원본 `Exception Error`, `RequestVersion`을 보존한다. `HistoryCommitSelectionOutcome`은 App의 `Internals/Enums.cs`에 `Found`, `NotFoundInScope`, `Failed`, `Superseded`로 둔다. 단일 인수는 현재 저장소·범위를 유지하고 태그 진입은 전달한 저장소·전체 범위를 사용한다. 새 API의 구현·인계 상태는 CurrentStatus를 따른다.
- 이동 결과는 `ReportCommitSelectionResult(result)`로 표시한다. 현재 저장소·범위·Presenter 요청 버전이 맞는 결과만 소비하고 `Superseded`는 표시하지 않는다. 성공은 공통 `ActionMessage`, 미발견·실패는 머리글의 `NavigationMessage`와 공통 상태로 전달한다. 목록·상세 오류와 이동 안내를 구분하며 MainWindow에서 같은 결과를 다시 번역하거나 중복 발행하지 않는다.
- 선택 기반 파일 미리보기와 명시적 Open의 수명을 구분한다. 선택 해제는 선택 요청을 무효화하고, 저장소·커밋·부모 변경은 두 경로를 무효화한다. 참조 전환 실패 후 조회 완료에서도 저장소와 요청 수명을 각각 재확인한다. 이미 반영한 Changes 로딩·오류·빈 결과와 전용 파일 히스토리 연결은 되돌리지 않는다.
- 참조: 조회·현재 브랜치 표시, 브랜치 생성·추적·전환·삭제, 태그 생성·단일 삭제 대화상자, 로컬 브랜치·태그 이름 변경. 메뉴를 연 시점의 저장소·참조·객체 OID를 고정하며 원격 변경은 서버 영향을 구분한다.
- [커밋 상세](../Design/CommitInspection.md), [커밋 명령](../Design/CommitActions.md), [태그 삭제](../Design/TagDeletion.md), [참조 이름 변경](../Design/ReferenceRename.md)을 따른다.
- MainWindow는 수정하지 않는다. 참조 변경의 `RepositoryChanged`와 History 갱신 의존을 총괄에게 전달한다. 숨긴 History의 참조 변경은 작업자 3이 복귀 시 반영한다.
- Git Settings는 작업자 1, 원격 실행·진행 창과 RemoteOperationsViewModel은 작업자 3 소유다.

## 작업자 3: MainWindow, 원격 작업, 충돌 해결, 복제와 배포 연결

소유 범위는 MainWindow·MainWindowViewModel, 원격 View·ViewModel·Presenter·진행 창과 서비스, 충돌 해결 화면·Presenter·Core 계약, 복제 화면·Presenter·Core, App 시작·DI·배포 설정이다.

- 상단 저장소 선택·추가 메뉴·시작 화면의 열기·복제 진입, 화면 전환·영역별 조회·공통 사이드바 상태, 작업 완료 후 갱신 순서와 숨긴 History의 참조 변경 반영.
- 태그 커밋 이동은 `History.SelectCommitAsync(repository, hash, GitHistoryScope.All)` 뒤 `History.ReportCommitSelectionResult(result)`로 연결한다. 저장소 인스턴스·저장소 요청·태그 선택 요청 버전을 각각 검사하며 현재 결과만 표시한다. History의 dirty 버전은 해당 갱신이 성공하고 버전이 일치한 경우에만 해제한다.
- Fetch/Pull/Push의 FIFO·실행 직전 검증·진행·취소·완료 연결. 기본 Pull 실행은 작업자 1의 GitSettingsService 설정을 읽으며 일회성 메뉴는 저장값을 바꾸지 않는다.
- [충돌 창](../Design/ConflictEntry.md)의 진입·파일 선택·개별/일괄 선택·직접 편집·저장·스테이징·리베이스 계속. 화면에서 Git 도메인 검증을 재구현하지 않는다.
- [복제](../Design/RepositoryClone.md)의 단일 목적지, 시작 신호, 고정 오류 분류와 현재 목적지 상태 안내. 실패 이유와 폴더 상태를 구분하고 원문 stderr를 노출하지 않는다.
- [단일 파일 배포 정책](ReleasePolicy.md)에 따른 필수 리소스 포함과 로더·시작 연결. 릴리스 파일 생성·게시와 README 편집은 총괄이 맡는다.

## 인계와 완료

작업자는 담당 파일만 편집하고 구현 완료 후 편집을 중지한다. 변경 내용, 새 미추적 파일을 포함한 경로, 최종 API·문자열 계약 파일, 남은 의존성, 검증 미실시를 총괄에게 한 번에 전달한다. 다른 담당 파일의 오류를 임의로 수정하거나 반복 빌드로 확인하지 않는다.

총괄은 소유권·교차 의존을 조정하고 문서·Excel·JSON·README·릴리스 기록을 관리한다. 인계 반영이 끝나면 한국어 메시지로 로컬 커밋한다. 푸시는 사용자가 명시적으로 요청하거나 승인한 경우에만 진행하며 과거의 자동 푸시 지시는 재사용하지 않는다. 구현 완료·배포 파일 생성·실제 동작 검증 완료는 구분해 보고한다.
