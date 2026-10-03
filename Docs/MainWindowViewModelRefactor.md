# 메인 창 역할 분리

## 현재 구조

`RepositoryListViewModel`이 최근 저장소의 그룹화·추가·제거·마지막 선택을 관리한다. `ConflictResolutionViewModel`이 충돌 파일과 편집 상태를 소유하고 `ConflictWindow`는 그 화면 상태를 바인딩한다. `MainWindowViewModel`은 저장소 열기·화면 전환과 자식 영역의 갱신 순서를 조정한다. 원격 작업 완료 뒤 스냅샷 적용과 영향 영역 갱신은 `MainWindowRemoteCompletionPresenter`로 분리했다.

자식 화면을 모으는 `MainWindowChildren`은 `Composition`, 최근 저장소를 읽고 쓰는 `RepositoryListStore`와 저장 형식 `RepositoryListState`는 `Persistence`에 둔다. `RepositoryItem`과 `RepositoryGroup`은 `ViewModels/Models`의 목록 상태다. Stash·충돌 작업 완료를 메인 화면에 전달하는 두 인터페이스는 `Interfaces`에 두고 `MainWindowViewModel`이 기존 계약을 구현한다. 공통 `ICommand` 구현은 `Commands`에서 사용한다.

저장소 진입은 `MainWindow`의 상단 저장소 드롭다운과 `+` 메뉴로 연결한다. 내부 목록 모델의 부모 경로별 그룹은 유지하되 화면에는 저장소 이름만 표시한다. 중앙 시작 화면은 저장소가 없고 Git Settings를 보고 있지 않을 때 열기·복제 버튼을 보여 준다. 이 표시 상태는 `MainWindowViewModel.IsRepositoryStartView`로 관리한다. 왼쪽은 참조 탐색과 공통 상태 영역에 사용한다.

아이콘과 번역된 라벨, 탐색 선택 밑줄은 View·공통 앱 리소스의 책임이다. 이 표현 변경으로 ViewModel에 Git 실행을 추가하거나 저장소·테마 변경 시 불필요한 재조회를 하지 않는다. 전체 책임과 DI 수명은 [현재 아키텍처](AvaloniaMvpAdoption.md)를 따른다.

저장소를 선택하면 요청 버전을 올리고 이전 열기 요청을 취소한다. 선택한 경로와 Local Changes 화면을 먼저 표시하며 저장소 루트 확인 뒤 Local Changes·참조·원격·리베이스 상태를 영역별로 조회한다. 늦게 끝난 이전 저장소 결과는 현재 화면에 적용하지 않는다. History와 Git Settings의 상세 조회는 해당 화면이 필요할 때 시작한다.

## 남은 경계

- `MainWindowViewModel`에는 저장소 전환, 공통 상태·오류 문구와 여러 자식 화면 조정이 여전히 남아 있다. ViewModel의 줄 수나 파일 분리 자체를 완료 기준으로 삼지 않고, 실제 비동기 조정 책임을 기능별 Presenter로 옮길 때 중복 조회와 늦은 결과 차단을 유지한다.
- 충돌 파일 로드와 일괄 선택의 조정은 충돌 저장·스테이징 분리와 별도 후속 단계다. `ConflictWindow`의 미저장 편집 확인과 저장소 전환 순서는 보존한다.
- 새 저장소 [복제](../Design/RepositoryClone.md)는 성공 후 최근 목록 등록과 열기를 기존 저장소 전환 경로에 연결한다. 복제 창이 다른 화면의 저장소 상태를 직접 갱신하지 않는다.

진행 상황과 검증 범위는 [구현 현황](CurrentStatus.md)을 따른다.
