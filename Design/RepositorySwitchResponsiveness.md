# 저장소 전환 응답성

## 현재 코드에서 보이는 지연 경로

`MainWindowViewModel.OpenRepositoryAsync`는 저장소 경로를 연 뒤 Local Changes, Stashes, 참조 트리, 원격 상태, 충돌 목록을 순서대로 기다린다. 화면의 저장소 이름과 선택 상태도 이 중 일부가 끝난 뒤에 갱신된다. 이 문서는 코드 경로를 읽고 정리한 것으로, 각 단계의 실제 소요 시간은 측정하지 않았다.

- `LocalChangesViewModel.LoadAsync`는 작업 트리 상태를 읽은 뒤 `StashViewModel.LoadAsync`를 기다린다. Stash 화면이 보이지 않아도 Stash 목록과 작업 트리 상태를 다시 읽어 초기 상태 조회가 중복된다.
- `ReferenceExplorerViewModel.SetRepositoryAsync`는 브랜치·원격·태그·Stash·서브모듈을 모두 읽는다. `GitReferenceService.GetSnapshotAsync`는 각 태그의 대상 커밋을 별도 `rev-parse` 명령으로 확인하므로 태그가 많을수록 Git 프로세스가 늘어난다.
- `RemoteOperationsViewModel.SetRepositoryAsync`도 현재 브랜치, upstream, 앞선/뒤처진 커밋 수를 여러 Git 명령으로 읽는다. 이 결과를 기다리는 동안 기본 Local Changes 화면을 보여 주는 일이 지연되지 않아야 한다.

## 화면 동작

1. 저장소를 선택하면 선택 경로와 로딩 상태를 즉시 보여 준다. 저장소 루트와 현재 브랜치를 확인한 뒤 기본 Local Changes 화면을 먼저 표시한다. 참조·원격·Stash의 느린 조회가 끝날 때까지 전체 화면 전환을 막지 않는다.
2. Local Changes의 파일 상태를 우선 읽는다. Stashes 목록은 Stashes 화면이나 보관 창을 열 때 읽고, 같은 전환에서 작업 트리 상태를 중복 조회하지 않는다. 참조 트리와 원격 정보는 독립적으로 갱신하고 각 영역에만 로딩 상태를 표시한다.
3. 참조 목록은 Git 명령을 항목마다 반복하지 않도록 묶어서 읽는다. 특히 태그마다 별도 프로세스를 만들지 않는다. 많은 태그·서브모듈이 있어도 기본 화면이 준비되는 경로를 막지 않는다.
4. 사용자가 로딩 중 다른 저장소를 고르면 마지막 선택을 우선한다. 이전 저장소의 비동기 결과가 현재 화면에 나타나지 않게 하고, 가능하면 이전 읽기 작업을 취소한다. Git 작업을 실행 중인 상태의 저장소 전환은 기존 작업의 안전한 완료·취소 흐름을 따른다.

## 현재 단계

구현을 우선한다. 시간 측정, 임시 저장소 시나리오 테스트, 성능 벤치마크는 기능 구성이 안정된 뒤에 한다.
