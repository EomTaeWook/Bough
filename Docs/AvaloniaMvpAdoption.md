# Avalonia MVP 적용

> 책임 원칙: [재사용 Unity 클라이언트 계층형 MVP](ReusableArchitecture/UnityClientArchitecture.md)
> Bough 규칙: [코딩 컨벤션](CodingConvention.md), [구현 작업 지시](ImplementationPlan.md)

## 적용 범위

재사용 문서의 Domain, Presenter, Presentation Model, View 분리를 Bough의 Avalonia 화면에 적용한다. Unity의 Scene, `MonoBehaviour`, 프레임 반복, 프리팹과 풀 규칙은 이 데스크톱 앱의 구현 계약이 아니다. 기존 MVVM 타입의 이름만 Presenter로 바꾸지 않는다. 한 기능을 수정할 때 책임을 실제로 옮기고 기존 바인딩과 사용자 동작을 유지한다.

| 역할 | Bough의 경계 | 소유할 일 |
| --- | --- | --- |
| Domain / Gateway / Storage | `Bough.Core/Git`, `Bough.Core/Conflicts`의 서비스와 결과 모델 | Git 명령, 충돌 파싱, 사전 검증, 파일·설정 저장, 안정적인 결과 코드와 원본 데이터 |
| Presenter | `Bough.App`의 기능별 조정 객체. 복잡한 흐름은 ViewModel에서 분리 | 사용자 요청 수신, Git 작업 큐에 실행 요청, 서비스 결과 적용 순서, 요청 버전·취소·구독 수명 |
| Presentation Model | `Bough.App/ViewModels`의 바인딩 상태와 `ViewModels/Models`의 화면 항목 | 선택, 로딩, 입력 가능 여부, 원본 결과·오류 코드와 인수 등 화면이 관찰할 상태 |
| View | `Bough.App/Views`와 `MainWindow`·`ConflictWindow`의 AXAML 및 코드비하인드 | 바인딩, 입력 전달, 초점·스크롤·선택, 테마, 표시 문자열과 서식 |
| Lifecycle Adapter | `App.axaml.cs`, 화면 생성·해제 경계 | DI 구성, View·Presenter·화면 상태 연결과 이벤트 구독 해제 |

기본 흐름은 `View → Presenter → Core 서비스 → Presenter → Presentation Model → View`다. `GitOperationQueue`는 저장소별 변경 명령을 직렬화하는 실행 경계이며 Domain 결과나 UI 상태를 소유하지 않는다. MainWindow는 저장소 전환과 화면 사이 갱신 순서를 조정하고 기능별 Git 규칙을 재구현하지 않는다.

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

Local Changes·Stash의 변경 명령과 Git Settings의 Git 경로 확인·저장, History 목록·상세·명령, Git 참조 조회·변경, 원격 작업 실행과 MainWindow 원격 완료 갱신에도 기능별 Presenter를 연결했다. 저장소 복제의 목적지별 큐 진입과 Core 실행은 `CloneRepositoryPresenter`로 연결했다. 기존 ViewModel은 바인딩 상태와 일부 레거시 조정을 계속 소유한다. 남은 이행 범위와 실제 검증 상태는 [구현 현황](CurrentStatus.md)에서 관리한다. 작업자 소유권은 [AGENTS.md](../AGENTS.md)를 따른다.

## 완료 기준

- 변경한 흐름에서 View가 Git 서비스나 파일 저장을 직접 실행하지 않고, Core가 Avalonia·화면 문자열에 의존하지 않는다.
- Presenter의 사용자 요청과 서비스 결과 적용 경로, 화면 상태의 소유자가 코드에서 구분된다.
- 기존 큐 순서, 저장소 전환 중 늦은 응답 폐기, 충돌 편집 내용 보존, 오류 표시 위치와 다국어 표시가 유지된다.
- 검증 실행은 [현재 검증 방침](../AGENTS.md#현재-검증-방침)을 우선한다. 사용자가 요청한 범위에서만 빌드·변경 검사·대표 시나리오를 수행한다. 구현 완료와 검증 완료를 구분하고 미실시 항목은 [구현 현황](CurrentStatus.md)에 적는다.
