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

## 첫 적용: 충돌 저장·스테이징

`ConflictResolutionViewModel.SaveAndStageCoreAsync`의 충돌 경로·원본 내용 재검증, 결과 파싱 및 저장·스테이징은 Core 경계가 맡는다. Presenter는 현재 요청의 저장소·파일·버전 확인, 큐 실행, 성공·stale 결과에 따른 갱신 순서를 맡는다. 화면 상태는 편집 텍스트, 선택 구간, 진행 여부, 결과 코드·인수를 보관한다. `RefreshAfterStaleStageAsync`의 목록 재조회는 화면 사이 조정이며 Core의 Git 검증과 섞지 않는다. 재조회에 실패해도 원래의 stale 결과와 사용자 편집 내용을 잃지 않아야 한다.

이 흐름을 먼저 분리하고 동작을 확인한 뒤 Local Changes·Stash·Settings, History·References, 원격 작업에 같은 경계를 적용한다. 작업자 소유권은 [구현 작업 지시](ImplementationPlan.md)를 따른다. 다른 작업자의 화면과 공유 문자열 데이터는 총괄을 통해 계약을 전달한다.

## 완료 기준

- 변경한 흐름에서 View가 Git 서비스나 파일 저장을 직접 실행하지 않고, Core가 Avalonia·화면 문자열에 의존하지 않는다.
- Presenter의 사용자 요청과 서비스 결과 적용 경로, 화면 상태의 소유자가 코드에서 구분된다.
- 기존 큐 순서, 저장소 전환 중 늦은 응답 폐기, 충돌 편집 내용 보존, 오류 표시 위치와 다국어 표시가 유지된다.
- 변경한 프로젝트를 빌드하고 `git diff --check`와 문서 링크를 확인한다. 현재 개발 단계의 반복 UI 검증 보류는 [구현 작업 지시](ImplementationPlan.md)를 따른다.
