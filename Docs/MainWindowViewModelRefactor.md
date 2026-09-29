# 메인 창 ViewModel 역할 분리

현재 `MainWindowViewModel.cs`는 약 1,939줄이며 저장소 목록·전환, 화면 탐색, 자식 ViewModel 연결, 비동기 갱신, 충돌 문서 편집·저장을 함께 맡는다. 특히 `ConflictWindow`가 `MainWindowViewModel` 전체를 DataContext로 사용해 충돌 창 상태가 메인 창으로 퍼져 있다.

## 목표 구조

- `MainWindowViewModel`은 현재 저장소, 활성 화면, 자식 ViewModel의 명시적 호출 순서, 공통 오류·진행 상태만 조정한다. 저장소가 바뀌거나 Git 작업이 끝났을 때 어떤 화면을 갱신할지 여기서 직접 호출한다. 범용 이벤트 버스나 `Func<Task>` 다중 구독 호출로 작업 순서를 숨기지 않는다.
- `ConflictResolutionViewModel`은 충돌 파일 목록·선택, 문서 로드, ours/theirs/base/결과 텍스트, hunk 선택 명령, 수정 여부, 저장·스테이징을 소유한다. `ConflictWindow`는 이 ViewModel을 DataContext로 받아 필요한 속성만 바인딩한다. 저장소 전환 전 미저장 수정 확인과 해결 완료 시 창 닫기 동작은 기존과 같아야 한다.
- 저장소 목록의 그룹화·추가·제거·마지막 선택 저장은 별도 `RepositoryListViewModel` 또는 작은 전용 컴포넌트로 옮긴다. 메인 창은 사용자가 선택한 경로를 받아 저장소 열기와 화면 갱신을 조정한다.
- 저장소 전환·Refresh·History/References/Remote/Local Changes 연결의 요청 버전·취소·중복 조회 방지는 유지한다. 분리 때문에 첫 화면에서 불필요한 Git 조회를 추가하지 않는다. 이미 자식 ViewModel에 구현된 기능은 다시 래핑하거나 복제하지 않는다.

## 연결 순서

1. 충돌 ViewModel을 분리하고 `ConflictWindow`의 XAML 타입·생성자·메인 창 연결을 바꾼다. 기존 창의 선택, 결과 편집, 저장·스테이징, F5, 미저장 닫기 확인을 보존한다.
2. 저장소 목록 상태를 분리하고 `MainWindow.axaml`의 목록 바인딩과 저장소 추가·제거·전환 경로를 연결한다. 마지막 저장소 복원과 빠른 저장소 전환을 보존한다.
3. `MainWindowViewModel`에는 한 화면이 다른 화면에 영향을 줄 때 필요한 명시적인 조정 메서드만 남긴다. 단순히 `partial` 파일로 나누거나 범용 전달 메서드를 늘리는 것으로 끝내지 않는다. DI 생성자도 실제 역할에 맞게 줄인다.

진행 중인 테마·원격·Stash·충돌 진입 작업과 같은 파일을 수정하므로 작업자 3이 소유하고 기존 변경을 잃지 않게 이어서 적용한다. 현재 개발 단계의 테스트·시나리오 검증 보류는 유지한다.
