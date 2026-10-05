# Bough 현재 아키텍처와 Avalonia MVP 적용

> 책임 원칙: 아래 적용 범위의 Domain·Presenter·Presentation Model·View 분리
> Bough 규칙: [코딩 컨벤션](CodingConvention.md), [구현 작업 지시](ImplementationPlan.md)
> 현재 작업 배분과 검증 방침: [AGENTS.md](../AGENTS.md)

## 현재 구성

Bough는 .NET 10·Avalonia 12.1.3 기반 데스크톱 Git 클라이언트다. AXAML 바인딩과 ViewModel을 유지하면서 기능별 비동기 조정을 Presenter로 분리한 구조이며, 순수 MVP로의 이행은 아직 끝나지 않았다. 이 문서는 현재 책임과 앞으로 지켜야 할 경계를 함께 기록한다. 구현·검증 상태는 [구현 현황](CurrentStatus.md)을 따른다.

| 프로젝트·영역 | 현재 책임 |
| --- | --- |
| `Bough.Core` | Git 프로세스 실행, 저장소·작업 트리·참조·원격·복제 서비스, 저장소별 작업 큐와 충돌 파싱. Avalonia와 표시 언어에 의존하지 않는다. |
| `Bough.Core/Interfaces` | `ITerminalLauncher`의 공통 실행 계약. OS 구현이나 앱의 화면 계약을 넣지 않는다. |
| `Bough.App/Presenters` | 기능별 요청 조정, 큐 진입, 실행 시점의 조건 검사와 결과 전달. |
| `Bough.App/ViewModels` | 화면의 바인딩 상태·선택·로딩·명령 연결과 남아 있는 화면 조정, 공통 `ViewModelBase`. 기존 일부 표시 문구 생성도 여기 남아 있다. |
| `Bough.App/ViewModels/Models` | 커밋·파일·참조·저장소·계정 목록 항목과 화면 결과 모델. 항목의 속성 변경 알림도 여기 둔다. |
| `Bough.App/Commands` | `RelayCommand`, `AsyncRelayCommand`, `QueuedAsyncRelayCommand`의 `ICommand` 어댑터와 활성화 알림. |
| `Bough.App/Interfaces` | `IStashMutationCompletion`, `IConflictStageCompletion`의 화면 간 완료·갱신 계약. |
| `Bough.App/Services`, `Threading` | `HistoryGraphBuilder`의 그래프 행 계산과 `UiQueuedOperation`의 UI 스레드 연결. Git 실행·FIFO 큐와 별도 책임이다. |
| `Bough.App/Composition` | `MainWindowChildren`의 자식 화면 구성과 의존성 연결. |
| `Bough.App/Persistence` | `RepositoryListStore`·`RepositoryListState`의 최근 저장소 저장·읽기와 `LanguageSettingsStore`의 앱 언어 저장·읽기. |
| `Bough.App/Views`, `MainWindow`, `ConflictWindow` | AXAML과 입력·모달 창·초점·선택·스크롤·표시 연결. |
| `Bough.App/Appearance`, `Localization`, `Converters`, `Assets` | 테마, 문자열 표시, 상태의 아이콘 변환과 공통 벡터 리소스 등 앱 표현. |
| 앱의 표시 설정·데이터 로더 | `AppearanceThemeService`의 앱 표시 설정 저장, 외부 JSON 또는 내장 리소스 로딩. |
| `DataContainer/Generated` | 문자열 등 템플릿 타입의 생성 결과. 직접 수정하지 않는다. |

현재 주요 입력 경로는 `View → ViewModel의 명령·연결 또는 Presenter → Core 서비스 → 결과 적용 → ViewModel 바인딩 상태 → View`다. 기능마다 이행 수준이 다르므로 모든 ViewModel이 이미 상태만 소유한다고 가정하지 않는다.

공통 명령·인터페이스·저장·구성 클래스를 ViewModel에서 사용해도 해당 역할의 폴더와 네임스페이스에 둔다. 이번 분리는 파일 위치와 참조의 정리이며, 비동기 실행 책임을 추가로 Presenter에 옮겼다는 의미는 아니다.

## 시작과 수명

`App.axaml.cs`가 Dignus DI 구성과 메인 창 연결을 맡는다. 저장된 앱 언어로 `StringLanguageSelection`을 등록하고 공유 `StringHelper`를 컨테이너에서 해석한다. 저장값이 없으면 한국어를 사용하며 OS UI 언어를 읽지 않는다. Settings의 언어 변경은 `LanguageSelectionPresenter`가 저장한 뒤 `StringHelper.LanguageChanged`로 전파하고, 화면 수명에 연결된 `LanguageChangeBinding`이 라벨과 바인딩을 갱신한다. `LocalizedText`는 원래 키·인수로 현재 안내를 표시한다. Git 조회나 화면·입력 재생성은 하지 않는다. 자세한 계약은 [앱 언어와 StringHelper](StringHelperDependencyInjection.md)를 따른다. `AppearanceThemeService`와 Git 실행 설정은 앱 시작 지점에서 등록한다. Core와 App 어셈블리의 `[Injectable]` 등록을 사용하며 기존 명시 등록과 팩토리도 함께 남아 있다.

`GitCommandRunner`는 Transient, `GitRepositoryService`, 공통 `GitOperationQueue`와 `Composition/MainWindowChildren`은 Singleton으로 구성된다. `MainWindowChildren`이 메인 화면의 자식 ViewModel 의존성을 모은다. `Persistence/RepositoryListStore`의 명시 Singleton 등록도 유지한다. `RemoteOperationsViewModel`은 Transient이며, 원격 실행 창에는 `Func<RemoteOperationsViewModel>`로 별도 세션을 생성한다. 개별 View가 두 번째 서비스 컨테이너나 공통 작업 큐를 만들지 않는다.

## 저장소·화면 조정

- `RepositoryListViewModel`과 `RepositoryListStore`가 등록 목록·활성 항목·마지막 저장소와 저장을 관리한다. 내부 모델은 부모 경로별 그룹을 유지하지만 상단 드롭다운에는 저장소 이름만 표시한다. 부모 폴더 이름을 별도 제목으로 표시하지 않고 전체 경로는 항목 툴팁으로 제공한다.
- `MainWindow`가 상단 저장소 선택·`+` 메뉴와 중앙 시작 화면을 기존 폴더 선택기·복제 창에 연결한다. `MainWindowViewModel`이 저장소 전환·화면 선택·영역별 조회 순서를 조정한다. Git Settings는 저장소가 없어도 열 수 있으며, 이때 시작 화면을 숨긴다.
- 왼쪽은 현재 저장소의 참조 탐색과 하단 공통 상태 영역이다. Local Changes·History·Git Settings는 상단에서 전환하며 충돌 해결은 별도 `ConflictWindow`에서 진행한다.
- 저장소 선택 시 요청 버전을 갱신하고 이전 열기를 취소한다. Local Changes 화면과 선택 경로를 먼저 표시하고, History·설정 상세는 필요한 시점에 조회한다. 늦은 결과는 요청 버전과 저장소를 각각 검사한다.
- 변경 명령은 공통 `GitOperationQueue`의 저장소별 FIFO 경계를 사용한다. 복제는 목적지 절대 경로를 큐 키로 사용하며 성공 후 등록·열기는 메인 전환 경로에 연결한다.
- `GitOperationQueue`의 내부 `RepositoryOperationQueue`가 각 저장소의 대기·실행 상태를 소유한다. 그래프 레인을 뜻하는 이름과 구분하며 저장소별 순차 실행과 서로 다른 저장소의 독립 실행을 유지한다.
- 추적 중지·무시는 `LocalChangesMutationPresenter`가 확인과 큐 실행을 조정한다. Core의 `GitStopTrackingPlan`은 파일·인덱스 스냅샷과 `.gitignore` 변경 계획을 함께 보관하며 `GitWorkingTreeService`·`GitIgnoreService`가 확인 후 재검사, 규칙 추가와 인덱스 제거를 맡는다. 작업 파일을 보존하고 `.gitignore`는 자동 스테이징하지 않는다. 규칙 추가 후 추적 중지 중 오류가 나면 부분 변경을 알리고 화면을 갱신한다.
- 병합 커밋 기본 메시지는 `GitWorkingTreeService`가 `rev-parse --git-path MERGE_MSG`로 실제 메타데이터 위치를 구하고 `MERGE_HEAD`가 남아 있을 때 읽는다. 일반 저장소·연결된 worktree의 `.git` 경로를 화면에서 추측하지 않는다. `GitWorktreeStatus.MergeCommitMessage`가 Git 원문을 상태 스냅샷에 담고 `LocalChangesViewModel`은 자동 입력의 출처를 보관해 사용자 초안·Amend·편집한 메시지를 보호한다. 파일 조회는 Core, 입력 상태는 ViewModel, 실제 커밋은 기존 Mutation Presenter와 저장소 큐의 책임이다. 기존 `--cleanup=verbatim` 정책을 유지한다.
- `ConflictResolutionViewModel`이 충돌 파일과 편집 상태를 소유하고 `ConflictStagePresenter`가 저장·스테이징을 조정한다. `MainWindowRemoteCompletionPresenter`는 원격 작업 완료의 스냅샷 적용과 영향 영역 갱신을 맡는다. 미저장 충돌 편집 확인과 외부 활성화 뒤 갱신은 메인 창의 연결 책임이다.
- 충돌 일괄 선택은 현재 파일의 미선택 구간만 변경한다. ViewModel이 선택 개수와 처음 반영된 구간을 표시 상태에 적용하고, View는 파일 상단의 일괄 선택과 비교 아래의 현재 구간 선택, 최종 결과의 저장·스테이징을 구분해 배치한다. 파일 저장과 큐 실행은 기존 Presenter 경계를 유지한다.
- 원격 실패의 `GitRemoteOperationException`은 Core의 오류 식별자·인수와 기존 민감 정보 제거를 거친 Git 진단을 분리한다. `RemoteOperationsViewModel`은 짧은 지역화 상태와 상세 진단·Pull 요약을 별도 바인딩 상태로 보관하고, View는 상세를 기본으로 접힌 영역에 표시한다. 진행 단계·경과 시간은 실행 중에만 보이며 완료 뒤의 Pull 전략을 자동 변경하지 않는다.
- History의 기본 상세 탭은 Commit·Changes다. 사용자 요청으로 커밋 우클릭의 File Tree와 파일 우클릭의 파일 트리에서 보기 항목을 제거했다. 기존 `IsFileTreeView` 보조 화면과 `HistoryFileTreePresenter` 조회 구현은 내부에 남아 있으며 현재 UI 진입 메뉴는 제공하지 않는다. `Services/HistoryGraphBuilder`가 커밋 그래프 행을 계산하고 `ViewModels/Models`의 항목이 표시 상태를 보관한다.

## 터미널 연결

`Composition/MainWindowChildren`과 Console 호출부는 `Core/Interfaces/ITerminalLauncher`를 생성자 주입받는다. `App.axaml.cs`가 시작 시 OS를 판별해 `WindowsTerminalLauncher`·`MacOsTerminalLauncher`·`LinuxTerminalLauncher` 중 하나를 Singleton으로 등록하며, 지원하지 않는 OS에는 오류를 반환하는 구현을 등록한다. 공통 추상 `TerminalLauncher`는 저장소 확인·프로세스 실행·Git 경로 연결만 맡고 OS 분기를 갖지 않는다.

각 구현은 공유 `GitExecutableSettings`의 현재 경로를 읽어 저장소 루트에서 연다. 설정한 Git 실행 파일의 폴더만 해당 콘솔의 `PATH` 앞에 연결한다. 경로를 지정하지 않았으면 기존 콘솔 환경을 사용하며 배포본 내부 경로를 추측하거나 시스템 환경·사용자 셸 프로필을 저장하지 않는다.

| 구현 | 실행과 초기화 |
| --- | --- |
| Windows | Windows Terminal의 PowerShell, 실행 실패 시 독립 PowerShell. 시작 명령에서 Git 경로·저장소 이동·Git 버전 표시를 처리한다. |
| macOS | Terminal에 저장소 이동과 Git 경로를 포함한 셸 시작 명령을 전달한다. |
| Linux | 기존 `x-terminal-emulator`에 저장소 작업 디렉터리와 자식 프로세스 환경을 전달한다. |

터미널 실행은 Core가 맡고 View는 기존 Console 명령 연결을 유지한다. OS별 실제 실행 검증 상태는 [구현 현황](CurrentStatus.md)에 기록한다.

## 표현·데이터·배포

`App.axaml`이 밝음·어두움 공통 색과 기본 글씨 크기를 제공한다. 일반 본문·목록·입력은 14px, 보조 문구는 12px을 기본으로 사용한다. 상단 버튼은 헤더 바탕을 공유하고 선택된 탐색은 강조색 글자와 밑줄로 표시한다.

`Assets/ActionIcons.axaml`의 `StreamGeometry`를 앱 리소스에 합쳐 재사용한다. `PathIcon.boughIcon`은 기본 16px, 펼침 화살표는 10px이며 버튼·메뉴의 현재 전경색을 따른다. 주요 동작은 아이콘과 번역된 라벨을 함께 표시한다. 아이콘은 View 표현이며 Git 상태나 요청을 소유하지 않는다.

Local Changes의 Staged·Unstaged는 공유 파일 행 템플릿을 사용한다. `WorktreeStatusIconConverter`가 기존 파일 모델의 `StatusCode`를 공통 아이콘과 표시 조건으로 변환하며 Git 명령이나 상태 판정은 추가하지 않는다. 흔한 수정·추가·삭제는 아이콘과 툴팁으로 표시하고 부분 스테이징과 그 밖의 설명이 필요한 상태는 행 안의 문구도 유지한다. 상태 색은 테마 리소스의 동적 바인딩으로 적용한다. 어두운 테마의 브랜치·선택·포커스 강조는 하늘색 계열이다.

문자열 원본은 `Excel/String.xlsx`, 산출물은 `Datas/String.json`과 생성 템플릿이다. `TemplateDataLoader`는 기존 외부 파일이 있으면 읽고, 없으면 `PackagedResources`를 통해 내장 JSON을 읽는다. 최근 저장소·표시 설정 등 사용자 데이터는 기존 사용자별 저장 위치를 유지한다.

Windows x64 Release는 .NET 런타임·네이티브 라이브러리·필수 JSON·로그 기본 설정을 포함한 단일 `Bough.exe`로 배포한다. 일반 개발 실행은 기존 외부 파일 방식을 유지한다. 자세한 게시 계약은 [릴리스 정책](ReleasePolicy.md)을 따른다.

## 적용 범위

재사용 문서의 Domain, Presenter, Presentation Model, View 분리를 Bough의 Avalonia 화면에 적용한다. Unity의 Scene, `MonoBehaviour`, 프레임 반복, 프리팹과 풀 규칙은 이 데스크톱 앱의 구현 계약이 아니다. 기존 MVVM 타입의 이름만 Presenter로 바꾸지 않는다. 한 기능을 수정할 때 책임을 실제로 옮기고 기존 바인딩과 사용자 동작을 유지한다.

| 역할 | Bough의 경계 | 소유할 일 |
| --- | --- | --- |
| Domain / Gateway / Storage | `Bough.Core/Git`, `Bough.Core/Conflicts`의 서비스와 결과 모델 | Git 명령, 충돌 파싱, 사전 검증, 파일·설정 저장, 안정적인 결과 코드와 원본 데이터 |
| Presenter | `Bough.App`의 기능별 조정 객체. 복잡한 흐름은 ViewModel에서 분리 | 사용자 요청 수신, Git 작업 큐에 실행 요청, 서비스 결과 적용 순서, 요청 버전·취소·구독 수명 |
| Presentation Model | `Bough.App/ViewModels`의 바인딩 상태와 `ViewModels/Models`의 화면 항목 | 선택, 로딩, 입력 가능 여부, 원본 결과·오류 코드와 인수 등 화면이 관찰할 상태 |
| View | `Bough.App/Views`와 `MainWindow`·`ConflictWindow`의 AXAML 및 코드비하인드 | 바인딩, 입력 전달, 초점·스크롤·선택, 테마, 표시 문자열과 서식 |
| Lifecycle Adapter | `App.axaml.cs`, 화면 생성·해제 경계 | DI 구성, View·Presenter·화면 상태 연결과 이벤트 구독 해제 |

이행 목표의 기본 흐름은 `View → Presenter → Core 서비스 → Presenter → Presentation Model → View`다. `GitOperationQueue`는 저장소별 변경 명령을 직렬화하는 실행 경계이며 Domain 결과나 UI 상태를 소유하지 않는다. MainWindow는 저장소 전환과 화면 사이 갱신 순서를 조정하고 기능별 Git 규칙을 재구현하지 않는다.

## 현재 구조와 이행 기준

현재 여러 `*ViewModel`은 명령 처리와 바인딩 상태를 함께 소유하고, 일부는 `StringHelper`로 완성된 화면 문구도 만든다. 따라서 지금 코드를 이미 순수 MVP라고 부르지 않는다. 기능을 수정할 때 다음 순서로 경계를 정리한다.

1. Git 실행·파일 I/O·충돌 파싱·사전 검증은 Core 서비스에 둔다. 서비스 결과에는 표시 문장 대신 안정적인 코드, 인수와 원본 값을 담는다.
2. 비동기 작업과 요청 버전, 저장소별 큐 진입, 결과 적용 순서는 기능별 Presenter에 둔다. 단순 ViewModel을 이름만 바꿔 별도 래퍼로 만들지 않는다.
3. 화면 상태에는 현재 저장소·선택·원본 수치·의미 있는 enum·진행 상태를 둔다. 기능 계약 자체가 문자열인 경로·브랜치명·사용자 입력은 문자열로 유지한다.
4. View는 현재 언어로 고정 라벨, 상태·오류 코드와 숫자를 표시한다. 기존 `StringHelper`가 ViewModel에 있는 화면은 해당 흐름을 이행할 때 표시 경계를 옮긴다. `Bough.Core`에는 `StringHelper`를 넣지 않는다. `Excel/String.xlsx`가 문자열 원본이고 `Datas/String.json`은 생성 결과다.
5. View의 입력 연결과 Presenter의 서비스 구독은 화면 생성·전환·종료 경계에서 정확히 한 번 등록하고 해제한다. 늦은 결과는 요청 버전과 저장소를 각각 검사해 버린다.

기존 AXAML `INotifyPropertyChanged` 바인딩과 `ICommand`를 일시에 없애지 않는다. 이행 중인 기능에서는 ViewModel을 바인딩 어댑터로 사용할 수 있지만 Git 명령과 검증을 그 안에 새로 추가하지 않는다. 새 Presenter나 모델은 실제로 분리할 책임이 있을 때만 만든다. 임시 이벤트 버스, 중복 저장소, 두 번째 작업 큐를 추가하지 않는다.

## 1차 적용 결과

충돌 저장·스테이징은 `GitRepositoryService`의 Git 검증·파싱·쓰기와 `ConflictStagePresenter`의 큐·요청 버전·stale 재조회로 분리했다. `ConflictResolutionViewModel`은 원본 결과 상태를 보관하고 `ConflictWindow`가 표시 언어로 변환한다. 실패나 늦은 결과가 사용자 편집 내용을 덮지 않게 하는 기존 계약을 유지한다.

Local Changes·Stash의 변경 명령과 Git Settings의 Git 경로 확인·저장, History 목록·상세·명령, Git 참조 조회·변경, 원격 작업 실행과 MainWindow 원격 완료 갱신에도 기능별 Presenter를 연결했다. 저장소 복제의 목적지별 큐 진입과 Core 실행은 `CloneRepositoryPresenter`로 연결했다. 기존 ViewModel은 바인딩 상태와 일부 레거시 조정을 계속 소유한다. 남은 이행 범위와 실제 검증 상태는 [구현 현황](CurrentStatus.md)에서 관리한다. 현재 작업자 소유권과 세션 전달 방식은 [AGENTS.md](../AGENTS.md)를 따른다.

## 완료 기준

- 변경한 흐름에서 View가 Git 서비스나 파일 저장을 직접 실행하지 않고, Core가 Avalonia·화면 문자열에 의존하지 않는다.
- Presenter의 사용자 요청과 서비스 결과 적용 경로, 화면 상태의 소유자가 코드에서 구분된다.
- 기존 큐 순서, 저장소 전환 중 늦은 응답 폐기, 충돌 편집 내용 보존, 오류 표시 위치와 다국어 표시가 유지된다.
- 현재는 사용자 지시에 따라 빌드·테스트·UI 실행·캡처·`git diff --check`·리소스 대조 등 추가 검증을 수행하지 않는다. 구현과 문서 갱신은 계속하며 검증 미실시를 보고한다. 아래 절차는 사용자가 검증 재개를 명시적으로 요청할 때만 적용한다.
- 검증이 재개되면 변경한 프로젝트를 빌드하고 `git diff --check`와 문서 링크를 확인한다. 사용자 동작에 영향이 있는 기능은 가능한 환경에서 대표 시나리오를 확인하고, 확인하지 못한 UI 동작은 [구현 현황](CurrentStatus.md)에 구분해 적는다.
