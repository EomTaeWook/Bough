# 메인 창 역할 분리

## 현재 구조

`RepositoryListViewModel`이 최근 저장소의 그룹화·추가·제거·마지막 선택을 관리한다. `ConflictResolutionViewModel`이 충돌 파일과 편집 상태를 소유하고 `ConflictWindow`는 그 화면 상태를 바인딩한다. `MainWindowViewModel`은 저장소 열기·화면 전환과 자식 영역의 갱신 순서를 조정한다. 원격 작업 완료 뒤 스냅샷 적용과 영향 영역 갱신은 `MainWindowRemoteCompletionPresenter`로 분리했다.

저장소를 선택하면 요청 버전을 올리고 이전 열기 요청을 취소한다. 선택한 경로와 Local Changes 화면을 먼저 표시하며 저장소 루트 확인 뒤 Local Changes·참조·원격·리베이스 상태를 영역별로 조회한다. 늦게 끝난 이전 저장소 결과는 현재 화면에 적용하지 않는다. History와 Git Settings의 상세 조회는 해당 화면이 필요할 때 시작한다.

## 남은 경계

- `MainWindowViewModel`에는 저장소 전환, 공통 상태·오류 문구와 여러 자식 화면 조정이 여전히 남아 있다. ViewModel의 줄 수나 파일 분리 자체를 완료 기준으로 삼지 않고, 실제 비동기 조정 책임을 기능별 Presenter로 옮길 때 중복 조회와 늦은 결과 차단을 유지한다.
- 충돌 파일 로드와 일괄 선택의 조정은 충돌 저장·스테이징 분리와 별도 후속 단계다. `ConflictWindow`의 미저장 편집 확인과 저장소 전환 순서는 보존한다.
- 새 저장소 [복제](../Design/RepositoryClone.md)는 성공 후 최근 목록 등록과 열기를 기존 저장소 전환 경로에 연결한다. 복제 창이 다른 화면의 저장소 상태를 직접 갱신하지 않는다.

진행 상황과 검증 범위는 [구현 현황](CurrentStatus.md)을 따른다.
