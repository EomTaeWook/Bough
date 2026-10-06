# 코딩 컨벤션

> 적용 범위: Bough에서 직접 관리하는 C#·AXAML·Markdown. `DataContainer/Generated`의 C#은 변환기 산출물이므로 직접 수정하지 않는다.
> 현재 책임 경계는 [Bough 아키텍처](AvaloniaMvpAdoption.md), 작업 배분·검증 방침은 [AGENTS.md](../AGENTS.md)를 따른다.

## C#

- 직접 관리하는 프로젝트의 `<Nullable>disable</Nullable>`을 유지한다. nullable 타입 표기(`T?`), null-forgiving 연산자(`!`), `#nullable enable`을 사용하지 않는다.
- private 필드와 private 상수는 모두 `_camelCase`로 작성한다. 상수도 `private const int _maximumPending = 64;`처럼 접두사 `_`를 사용한다. 저장소 루트의 [`.editorconfig`](../.editorconfig)가 이 규칙을 정의한다.
- 명시적인 null 검사와 `throw` 문을 유지할 수 있도록 Visual Studio의 `IDE0016`, `IDE0029`, `IDE0030`, `IDE0270` 스타일 제안은 저장소의 `.editorconfig`에서 숨긴다. 컴파일러의 실제 null 관련 오류나 다른 분석 경고까지 끄지 않는다.
- `CA1861`이 지적하는 반복 생성 상수 배열은 호출 대상이 배열을 수정하지 않는다는 점을 확인한 뒤 private `static readonly` 필드로 재사용한다. 값이 호출마다 달라지거나 호출 대상이 배열을 수정할 수 있으면 공유하지 않는다. 이 성능 경고를 프로젝트 전체에서 숨기지 않는다.
- 현재 코드와 같이 중괄호가 있는 네임스페이스와 명시적인 타입을 사용한다. 직접 관리하는 클래스에는 `sealed`를 사용하지 않는다.
- enum은 해당 프로젝트의 기존 `Internals/Enums.cs`에 모으고, Git 결과·요청 모델은 `Bough.Core/Git/Models`, 화면 항목은 `Bough.App/ViewModels/Models`에 둔다. 타입을 옮기면 사용하는 파일의 네임스페이스 참조도 같은 변경에서 수정한다.
- 삼항 연산자, `checked`·`unchecked`, 익명 객체를 사용하지 않는다.
- 모든 메서드 선언과 호출에서 여는 괄호 `(` 직후 줄바꿈하지 않는다. 첫 번째 매개변수나 인수는 여는 괄호와 같은 줄에 둔다. 나머지 항목도 읽을 수 있으면 같은 줄에 둔다.
- 서로 다른 검증 조건은 각각의 `if`에서 검사하고, 오류에는 실패한 조건과 관련 값을 남긴다.
- 사용자에게 보이는 버튼·메뉴·안내·상태·오류·확인 창 문구는 C#이나 AXAML에 특정 언어의 문자열로 고정하지 않는다. `Excel/String.xlsx` 원본에 한국어·영어 키를 추가하고 변환 절차로 `Datas/String.json`을 갱신한 뒤 앱의 `StringHelper`로 표시한다. 기존 키가 맞으면 그대로 재사용한다.
- `Bough.Core`는 앱의 문자열 도우미를 참조하지 않는다. Core에서 발생해 사용자에게 표시될 예외는 안정적인 오류 식별자와 필요한 인수를 담고, 앱의 표시 경계에서 현재 언어로 변환한다. Git 자체의 stdout·stderr, 파일 경로·명령 인수·프로토콜 값 등 번역하면 안 되는 데이터는 원문을 유지한다.
- `GitException`에서 오류 식별자를 사용할 때는 인수가 없어도 `new GitException("ErrorCode", null, Array.Empty<object>())`처럼 세 인수 생성자를 호출한다. `new GitException("ErrorCode", null)`은 일반 메시지 생성자로 연결되어 번역되지 않는다.
- 이벤트 핸들러·명령 메서드뿐 아니라 **비동기 응답의 유효성 검사도 조건 하나당 하나의 `if`**로 검사하고 실패하면 바로 반환한다. 요청 버전, 저장소, 선택 항목, 미리보기 버전은 서로 독립된 조건이므로 `&&`·`||`로 한 가드문에 묶지 않는다. 정상 흐름은 검증문 뒤에 둔다. 하나의 의미를 이루는 단일 논리식까지 기계적으로 쪼갤 필요는 없다.

```csharp
if (DataContext is not ReferenceExplorerViewModel viewModel)
{
    return;
}

if (TopLevel.GetTopLevel(this) is not Window owner)
{
    return;
}

if (viewModel.CurrentRepository == null)
{
    return;
}

if (viewModel.IsBusy)
{
    return;
}

if (_menuNode?.Target is not GitRemoteBranch branch)
{
    return;
}

await CheckoutRemoteBranchAsync(viewModel, branch, _menuRepositoryRoot);
```

비동기 결과를 적용하기 전에도 각각 분리한다.

```csharp
if (previewVersion != _previewVersion)
{
    return;
}

if (requestVersion != _requestVersion)
{
    return;
}
```

- 필요하지 않은 중간 변수나 미래 기능만을 위한 상태를 추가하지 않는다.

## 책임과 의존성

- 폴더와 네임스페이스는 실제 역할에 맞춘다. ViewModel에서 사용한다는 이유만으로 보조 클래스나 인터페이스를 `ViewModels`에 넣지 않는다. `ViewModels`의 직접 파일에는 화면 ViewModel과 공통 바인딩 기반인 `ViewModelBase`를 둔다.
- 앱의 인터페이스는 `Interfaces`와 `Bough.App.Interfaces`에 둔다. `IStashMutationCompletion`, `IConflictStageCompletion`은 화면 간 완료·갱신 계약이며, 인터페이스에 실행 로직이나 상태를 추가하지 않는다. Core의 계약을 앱으로 옮기지는 않는다.
- Core 실행 계약은 `Bough.Core/Interfaces`와 `Bough.Core.Interfaces`에 둔다. `ITerminalLauncher`의 OS 선택은 앱 시작의 DI 등록에서 처리하고 호출부·공통 기반 클래스에 반복하지 않는다. OS별 실행 인수와 셸 초기화는 각 구현이 맡으며 구성 객체와 소비자는 같은 공통 계약을 주입받는다.
- `RelayCommand`, `AsyncRelayCommand`, `QueuedAsyncRelayCommand` 같은 `ICommand` 구현은 `Commands`와 `Bough.App.Commands`에 둔다. 명령 어댑터의 실행·활성화 알림과 Presenter의 기능 조정, Core의 FIFO 큐를 구분한다.
- 목록·트리 항목과 화면 결과 모델은 `ViewModels/Models`에 둔다. `ViewModelBase`를 상속해 속성 변경을 알리더라도 항목 상태만 담는 타입은 화면 ViewModel로 분류하지 않는다. AXAML의 `vm`은 화면 ViewModel, `models`는 항목·결과 모델에 사용한다.
- UI 스레드 연결은 `Threading`, 자식 화면 생성·DI 구성 보조는 `Composition`, 앱 사용자 데이터 저장과 저장 형식은 `Persistence`, 앱의 표시 계산 서비스는 `Services`에 둔다. 각각 `UiQueuedOperation`, `MainWindowChildren`, `RepositoryListStore`·`RepositoryListState`, `HistoryGraphBuilder`가 해당한다. `Services`에 Git 실행이나 도메인 규칙을 새로 넣지 않는다.
- Git 프로세스·파일 변경·충돌 파싱·실행 전 조건 검사는 Core 서비스에 둔다. `Bough.Core`에는 Avalonia 타입·`StringHelper`·앱의 아이콘·화면 상태를 넣지 않는다.
- 원격 오류의 짧은 식별자·인수와 긴 Git 진단은 분리한다. 상세 진단은 기존 민감 정보 제거를 거쳐 전달하고 View가 기본으로 접힌 상세 영역에 표시한다. Git이 준비한 병합 메시지는 원문 데이터로 상태 모델에 담으며 사용자 초안·편집·Amend 보존 여부는 화면 상태에서 관리한다.
- 기능별 비동기 실행·큐 진입·결과 적용은 Presenter로 분리한다. 기존 ViewModel의 바인딩·명령 연결은 유지하고, 실제 책임을 옮기지 않은 래퍼나 이름 변경을 추가하지 않는다. 아직 남은 ViewModel 조정을 순수 MVP 완료로 기록하지 않는다.
- `MainWindow`는 입력·대화상자·창 수명 연결을, `MainWindowViewModel`은 저장소·화면 전환과 자식 조회 순서를 맡는다. 복제 창이나 자식 화면이 다른 화면의 현재 저장소를 직접 바꾸지 않는다.
- 공통 DI 구성은 `App.axaml.cs`에서 관리한다. 현재 `[Injectable]` 등록과 명시 등록·팩토리의 수명을 보존하며 View 안에 별도 컨테이너나 작업 큐를 만들지 않는다. 원격 실행 창은 주입된 팩토리로 작업 세션을 받는다.
- 앱 언어는 `LanguageSettingsStore`에서 읽고 `LanguageSelectionPresenter`를 통해 저장·적용한다. OS UI 언어를 감지하거나 화면마다 `StringHelper`를 다시 만들지 않는다. 열린 화면은 `LanguageChangeBinding`으로 공유 `LanguageChanged`를 구독하고, 분리·DataContext 변경 때 구독을 해제한다. `RefreshLocalization`은 표시 문구와 항목 알림을 갱신하며 Git 재조회·목록 재생성·사용자 입력 초기화를 수행하지 않는다. 실행 중 갱신할 안내·상태·오류는 번역된 문자열만 보관하지 않고 `LocalizedText`의 키·원래 인수 또는 구조화된 결과를 보관한다.
- 변경 명령은 기존 저장소별 FIFO 큐를 재사용한다. 실행 차례의 저장소·선택·대상 상태를 다시 확인하고, 취소하지 않은 요청을 `IsBusy`만으로 버리지 않는다. UI 요청 버전과 Core 작업 큐의 역할을 섞지 않는다.
- Git 변경 성공과 후속 조회·화면 갱신 실패를 결과에서 구분한다. 이미 성공한 변경을 실패로 바꿔 재실행하게 하지 않고, 완료 호출의 갱신 오류는 대화상자까지 전달한다. 갱신 재시도는 필요한 읽기만 수행한다.
- 메뉴 실행 대상을 메뉴 시점 저장소·경로에 고정한다. 클릭 시 대상이 무효화되면 이유를 표시하며, 표시용 활성 조건의 재검사만으로 요청을 조용히 버리거나 현재의 다른 선택으로 대체하지 않는다.

## 화면·아이콘·글씨

- 일반 본문·목록·입력·주요 버튼 글씨는 14px, 보조 설명·상태·메타데이터는 12px을 기본으로 한다. 섹션 제목은 16px 이상으로 구분한다. 조밀한 배치만을 이유로 사용자 표시 문구를 9~11px로 줄이지 않는다.
- 글씨를 키울 때 행 높이·줄 간격·버튼 영역을 함께 조정한다. 참조 트리는 최소 행 높이 32px·위아래 내부 여백 4px·섹션 앞 간격 8px로 체크·추가 아이콘 사이 공간을 확보한다. Local Changes diff는 14px 글씨·22px 줄 간격을 사용하며 줄 번호와 내용의 간격을 맞춘다.
- 반복 동작 아이콘은 `Assets/ActionIcons.axaml`의 `StreamGeometry`를 `StaticResource`로 재사용한다. 새 아이콘은 기존 벡터 표현을 확장하며 같은 동작을 위한 폰트 기호·래스터 파일·외부 패키지를 중복 도입하지 않는다. 기존 참조 트리 등 남아 있는 기호는 해당 화면을 정리할 때 이행한다.
- `PathIcon`에는 `boughIcon` 클래스를 사용한다. 기본 크기는 16px, 펼침 화살표는 `boughChevronIcon`의 10px을 사용한다. 작은 목록 보조 아이콘은 클릭 영역을 유지하면서 12px로 조절할 수 있다. 아이콘과 라벨 간격은 8px으로 맞춘다.
- 아이콘·버튼 라벨은 소유 버튼 또는 메뉴 항목의 `Foreground`를 따라 선택·비활성·주요 동작 색을 함께 바꾼다. 고정 색이나 별도 선택 팔레트를 추가하지 않고 `App.axaml`의 테마 리소스를 사용한다.
- 주요 동작은 아이콘과 번역된 라벨을 함께 표시한다. 아이콘 전용 추가·제거·펼침 버튼에는 기존 툴팁과 접근성 이름을 유지하고, 장식 아이콘이 입력·키보드 포커스를 받지 않게 한다.
- 아이콘이 포함된 버튼을 지역화할 때 `Button.Content`를 문자열로 덮어쓰지 않는다. 내부 `TextBlock.Text` 또는 메뉴의 `Header`를 갱신하고 접근성 이름을 함께 유지한다.
- 파일 상태의 모양 변환은 App의 표시 변환기에서 기존 `GitWorktreeFile.StatusCode`를 사용한다. Core 모델에 `Geometry`·브러시·아이콘 키를 넣거나 표시를 위해 Git 상태 판정을 다시 구현하지 않는다. 공유 `DataTemplate`으로 Staged·Unstaged 행의 표현을 맞추고 기존 파일 항목과 선택 계약을 유지한다.
- 흔한 수정·추가·삭제는 모양이 다른 아이콘과 상태 툴팁으로 표시할 수 있다. 부분 스테이징·이름 변경·복사·미추적처럼 추가 설명이 필요한 상태는 기존 번역된 문구도 행에 남긴다. 상태를 색만으로 구분하지 않는다.
- Staged·Unstaged 머리글은 48px, 액션 버튼은 32px, 버튼 간격은 8px을 사용한다. AXAML 높이와 `LocalChangesView`의 빈 목록 높이 상수를 함께 수정한다.
- 상단 버튼은 헤더의 바탕을 공유하며 선택된 탐색은 강조색 글자와 하단 2px 밑줄로 표시한다. 저장소 목록은 이름과 경로 툴팁으로 식별하고 상위 폴더 이름을 별도 제목으로 표시하지 않는다.

## 작업 배분과 문서

- 기존 작업자 배분과 세션 전달은 [AGENTS.md](../AGENTS.md) 및 사용자가 제공한 현재 작업 지침을 따른다. `AGENTS.local.md`가 없거나 담당 ID가 누락·무효이면 현재 세션에서 직접 진행한다. 세션 ID를 다시 요청하거나 새 하위 에이전트로 대체하지 않는다.
- 현재 구현의 책임·사용자 흐름이 바뀌면 관련 아키텍처·기획·구현 현황 문서를 함께 갱신한다. 기능별 담당·실행 계약은 구현 작업 지시를, 반영된 범위와 미실시 검증은 구현 현황을 기준으로 기록한다.
- 생성 결과·기존에 확인한 범위·이번 검증 미실시를 구분한다. 소스 변경만으로 UI 배치나 동작을 실제 실행에서 확인했다고 기록하지 않는다.

## 변경과 검증

사용자가 검증을 금지하거나 보류하면 [현재 검증 방침](../AGENTS.md#현재-검증-방침)을 따른다. **현재는 사용자 지시에 따라 추가 검증을 수행하지 않는다.** 아래의 하드코딩 검색·리소스 대조·링크 확인·`git diff --check`를 포함한 검증 절차는 사용자가 다시 명시적으로 요청할 때 적용한다. 빌드·테스트·임시 저장소 하네스·UI 실행 및 캡처도 중단한다. 구현·문서·리소스 변경은 계속 진행하고, 미실시 검증을 성공으로 보고하지 않는다.

- 요청을 해결하는 데 필요한 범위만 수정한다. 생성 코드를 직접 고쳐야 할 상황이면 원본과 변환 절차를 확인한다.
- 표시 문구를 바꾼 뒤에는 관련 C#·AXAML의 하드코딩을 다시 검색하고 `Datas/String.json`의 한국어·영어 항목과 `{0}` 같은 자리표시자가 모두 일치하는지 확인한다.
- 데이터 원본과 산출물, 도구 실행 위치는 [데이터 변환 도구 사용법](<데이터 변환 도구 사용법.md>)을 따른다.
- C#과 Markdown의 줄 끝은 CRLF로 유지한다.
- Visual Studio의 **일관성 없는 줄 끝** 경고를 띄우지 않으려면 현재 경고 창의 **이 대화 상자 항상 표시**를 해제하거나 `도구 > 옵션 > 환경 > 문서 > 로드 시 일관된 줄 끝 확인(Check for consistent line endings on load)`을 끈다. 이 설정은 개발자별 Visual Studio 설정이며 저장소 파일의 줄 끝 규칙은 위의 CRLF를 따른다.
- 사용자가 검증 재개를 명시적으로 요청한 경우에 변경 후 파일 경로와 상대 링크를 확인하고 `git diff --check`를 실행한다.

## 작업 인계와 게시

담당 범위·최종 계약·공유 문자열 병합은 [AGENTS.md](../AGENTS.md)를 따른다. 커밋과 푸시의 승인은 구분하고 사용자의 최신 지시를 적용한다. Bough에서는 총괄이 완료된 변경을 로컬 커밋하고 작업자는 인계한다. 현재 자동 푸시는 중단하며 사용자가 해당 푸시를 명시적으로 요청하거나 승인한 경우에만 진행한다. 과거의 자동 푸시 지시는 재사용하지 않는다. 검증 중단은 별도의 검증 방침으로 유지한다.

## 릴리스 배포

[릴리스 정책](ReleasePolicy.md)을 적용한다. Bough의 Windows x64 기본 배포물은 런타임·네이티브 라이브러리·필수 데이터와 기본 설정을 포함한 단일 `Bough.exe`다. 사용자별 저장 정책은 유지하며, 릴리스 파일 생성·게시와 별도 검증의 실행 범위를 구분한다.
