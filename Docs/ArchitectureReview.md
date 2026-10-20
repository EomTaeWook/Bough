# 재사용 아키텍처 검수

## 범위와 판단

2026-10-20 `reusable-architecture-review` 스킬로 제품 소스 기준 `7297915`를 읽었다. 기존 작업자 1·2·3이 담당 기능을 검수했고 총괄이 공통 실행·저장·표시 구독 경계와 지적된 호출 흐름을 종합했다. 확인된 불일치는 아래 P2 9건이다. 실제 장애 발생 빈도나 P0/P1 데이터 손상·보안 침해를 입증한 결과는 아니다.

**신규 릴리스는 보류한다.** 아래 1~5의 원래 대상·완료·종료 경계를 보완하기 전에는 새 정식 버전을 게시하지 않는다. 기존 v1.0.2와 태그·파일은 유지한다. 이번 요청은 검수와 Git 게시 범위이므로 제품 코드나 UI는 수정하지 않았다. 앞선 UI 검수의 배경·메타데이터·제목 색상 항목은 이 아키텍처 지적에 포함하지 않는다.

## 적용 규칙

- 원본은 `D:/Source/Paper-Toss/Blueprints/ReusableArchitecture`의 README, Common/CodingConvention·PresentationArchitecture·Collaboration, Application/ApplicationArchitecture를 직접 읽었다.
- [재사용 적용](ReusableArchitectureAdoption.md), [데스크톱 기준](DesktopApplicationArchitecture.md), [현재 MVP 적용](AvaloniaMvpAdoption.md), [코딩 컨벤션](CodingConvention.md), [Git 큐](GitOperationQueue.md), [구현 계약](ImplementationPlan.md)과 담당 기능 기획을 우선했다.
- 일반 앱의 입력·기대값, 백그라운드 조회, 실제 Task 완료, 후속 오류, DI·창/구독·자원 수명을 대조했다. Unity·Actor·DB 구조는 채택하지 않았다.
- 기존 VM 조정·지역화 어댑터·직접 구성, 기존 모델 묶음·수동 partial의 단계적 이행과 호스트의 명시 예외를 신규 결함으로 집계하지 않았다.

## 확인된 불일치와 최소안

### 1. Commit/Amend의 원래 HEAD·인덱스 내용 미고정 — 게시 전 보완

- 코드: [LocalChangesViewModel](../Bough.App/ViewModels/LocalChangesViewModel.cs) 1490행은 메시지·amend·파일 상태만 고정한다. [LocalChangesMutationPresenter](../Bough.App/Presenters/LocalChangesMutationPresenter.cs) 171행과 [GitWorkingTreeService](../Bough.Core/Git/GitWorkingTreeService.cs) 63행은 경로·이름 변경 경로·상태 문자만 비교하고, 같은 서비스 900행은 실행 시점 인덱스/HEAD로 커밋한다.
- 같은 파일이 동일 상태 문자로 다른 내용에 다시 스테이징되거나 HEAD가 교체되면 원래 승인 내용과 달라져도 통과할 수 있다. 특히 Amend의 원래 커밋을 고정하지 못한다. 실행 재현은 하지 않았다.
- 규칙: 공통 UI 원본 4절의 원래 기대값과 앱 원본 7절의 실행 직전 비교, Bough Git 큐의 실행 소유권과 확정 결과.
- 최소안: 기존 Commit 요청에 원래 HEAD와 인덱스 항목 기준을 고정하고 Core 실행 직전에 비교한다. 기존 인덱스 조회 책임을 재사용하고 새 관리자·큐를 만들지 않는다.

### 2. Stash 상위 완료가 FIFO 밖에서 실행 — 게시 전 보완

- 코드: [StashMutationPresenter](../Bough.App/Presenters/StashMutationPresenter.cs) 32행의 큐 callback은 변경 결과 반환까지다. [StashSavePresenter](../Bough.App/Presenters/StashSavePresenter.cs) 39행과 [StashView](../Bough.App/Views/StashView.axaml.cs) 63·88행이 큐 반환 후 상위 완료를 호출한다.
- 다음 동일 root 변경과 이전 Stash 완료 조회/스냅샷 적용이 겹칠 수 있다. root 전환 guard만으로 같은 root의 후속 변경을 구별할 수 없다. 실제 경합 시점은 미확인이다.
- 규칙: 앱 원본 7절과 Bough Git 큐 호출 경계의 필수 완료 callback까지 실행 소유권 유지.
- 최소안: 기존 IStashMutationCompletion의 작업별 완료를 같은 FIFO callback에서 기다린다. Save 성공+읽기 오류와 읽기 전용 재시도는 유지한다.

### 3. 일반 앱 종료에서 사용자 Git 작업 수명 누락 — 게시 전 보완

- 코드: [MainWindow](../Bough.App/MainWindow.axaml.cs) 306~320행은 자동 조회 종료를 기다리지만, 351~381행은 업데이트 종료 요청에서만 Git busy를 확인하고 일반 닫기는 허용한다.
- 충돌 창이 없는 상태에서 사용자 Git 작업이 실행·대기 중이어도 앱이 종료될 수 있다. Task/FIFO 완료를 기다리거나 종료를 거절하는 연결이 없다. 자식 Git 강제 종료·파일 손상은 입증하지 않았다.
- 규칙: 앱 원본 9절의 닫기 시 업무 확인·종료 수락 시 허용 작업 배출, Bough 데스크톱 기준의 요청과 작업 수명.
- 최소안: 일반 종료에도 기존 HasActiveOperations와 필요한 기존 업무 상태를 확인한다. 작업 중 닫기를 거절하면 중단한 자동 조회 수명을 복구한다. 강제 종료·새 종료 큐는 추가하지 않는다.

### 4. 충돌 단일 저장의 자체 갱신이 후속 오류 표시 무효화 — 게시 전 보완

- 코드: [ConflictStagePresenter](../Bough.App/Presenters/ConflictStagePresenter.cs) 488~515행의 Main 완료가 [MainWindowViewModel](../Bough.App/ViewModels/MainWindowViewModel.cs) 1914·1937·1964행을 거쳐 [ConflictResolutionViewModel](../Bough.App/ViewModels/ConflictResolutionViewModel.cs) 678행의 같은 stage 요청 무효화를 호출한다.
- 이어 다른 충돌 파일의 LoadCoreAsync가 실패하면 RefreshFailed를 만들지만 최종 IsCurrent가 false라 표시되지 않는다. [ConflictWindow](../Bough.App/ConflictWindow.axaml.cs) 135~137행도 반환 결과를 별도 소비하지 않는다. 실제 다른 대상/요청의 오류 폐기와 구분되는 자체 완료 경로다.
- 규칙: 앱 원본 7·8절의 성공 사실과 후속 오류 분리·호출자까지 오류 전달.
- 최소안: 자체 완료의 경로 목록 갱신과 사용자 대상 변경의 무효화 수명을 구별하고 현재 저장소의 후속 오류를 기존 상태에 전달한다. 저장 성공·새 초안·다른 대상 guard는 유지한다.

### 5. 일부 브랜치 변경이 성공/원래 root 통지를 잃음 — 게시 전 보완

- 코드: [GitReferenceService](../Bough.Core/Git/GitReferenceService.cs) 260~264행은 switch 성공 뒤 Open 실패·브랜치 불일치를 일반 전환 실패로 전달한다. 462~466행의 생성 후 불일치도 기존 후속 읽기 실패와 다르게 처리된다. [ReferenceExplorerViewModel](../Bough.App/ViewModels/ReferenceExplorerViewModel.cs) 453·466행은 비현재 대상/세대에서 완료하지만 원래 root dirty 통지를 발행하지 않는다.
- 실제 Git 변경 성공이 실패로 설명되거나 상위 완료 snapshot·원래 root 통지 모두 없는 조건부 경로가 있다. 외부 Git 경합과 복귀 화면의 실제 stale 표시는 재현하지 않았다.
- 규칙: 앱 원본 3·7절의 확정 결과 전달·후속 오류 분리, Bough 구현 계약의 비현재 root dirty 통지.
- 최소안: 성공한 변경 뒤 기존 후속 읽기 오류 계약을 소비하고 원래 root에 기존 ReferenceRefreshRequired를 전달한다. 유효한 metadata만 RepositoryChanged로 전달하며 변경을 재실행하지 않는다.

### 6. GitHub 계정 저장 성공과 재조회 오류를 함께 보존하지 못함

- 코드: [GitSettingsViewModel](../Bough.App/ViewModels/GitSettingsViewModel.cs) 611~620·874~939행은 계정 설정 쓰기 뒤 목록을 비우고 재조회한다. 조회 실패는 오류 상태만 남기고 정상 Task로 반환해 저장 성공 표시가 사라진다.
- 규칙: 앱 원본 7·8절의 변경 성공과 읽기 실패 분리.
- 최소안: 기존 GitSettingsDisplayResult의 성공 코드+RefreshError를 계정 선택 저장에도 사용한다. 저장을 반복하지 않는다. 단독 게시 차단 사유로 보지 않는다.

### 7. 원격 팝업의 준비 오류가 원격 없음으로 대체

- 코드: [RemoteOperationPresenter](../Bough.App/Presenters/RemoteOperationPresenter.cs) 53~57·109~111행의 필수 준비가 [RemoteOperationExecutionPresenter](../Bough.App/Presenters/RemoteOperationExecutionPresenter.cs) 64~72·204~209행에서 오류를 상태에만 담고 정상 반환한다. 뒤의 가용성 예외가 원본 준비 오류를 대체한다.
- 규칙: 공통 UI 원본 4·6절과 앱 원본 8절의 원본 오류 보존, 근거 없는 대상 없음으로 변환 금지.
- 최소안: 실행 팝업의 필수 준비에서 현재 원본 예외를 Task 호출자에게 반환한다. 일반 화면 새로고침 표시와 안전 거부·FIFO는 유지한다. 단독 게시 차단 사유로 보지 않는다.

### 8. 클립보드 실패 처리·늦은 오류 guard 누락

- 코드: [HistoryView](../Bough.App/Views/HistoryView.axaml.cs) 302·373·597행 및 [ReferenceExplorerView](../Bough.App/Views/ReferenceExplorerView.axaml.cs) 813행의 async void 복사는 OS 클립보드 오류를 잡지 않는다. History의 CopyPathClicked 1076~1092행은 오류 표시 직전 현재 대상 guard가 없다.
- 규칙: 앱 원본 5·8절의 성공·실패 모두 현재 화면 판정·원본 오류 전달.
- 최소안: 기존 View의 OS 입력 경계에서 오류를 처리하고 고정 VM/root/context와 상세 수명을 재확인한 뒤 기존 지역화 오류에 전달한다. 실제 앱 종료/장애 빈도는 미확인이다. 단독 게시 차단 사유로 보지 않는다.

### 9. Git 바이트 조회의 stderr Task 완료 소유 누락

- 코드: [GitCommandRunner](../Bough.Core/Git/GitCommandRunner.cs) 284행에서 errorTask를 시작하지만 301행의 실패 분기만 기다린다. 성공 시 310행에서 반환하며 취소·출력 제한의 finally 312~324행도 프로세스 종료만 기다린다. 기존 diff/blob/blame/파일 이력·커밋 메시지 등의 RunBytesAsync가 소비한다.
- 규칙: 앱 원본 5·7절의 실제 완료·출력 수집·자원 정리 수명.
- 최소안: runner의 기존 수명 안에서 시작한 읽기 Task를 정상 반환·오류 정리에 포함하고 원래 취소·출력 제한·Git 오류를 보존한다. 실제 읽기 실패나 화면 장애를 재현하지 않았다. 단독 게시 차단 사유로 보지 않는다.

## 유지된 경계와 한계

기존 root별 단일 FIFO·재진입 거부, 단일 미리보기와 최신 대기·독립 대상 guard, 태그 Push의 고정 OID·실제 Push 주소·완료 이벤트, App 배포 인프라와 Core의 분리, 기존 저장 권위자와 표시 구독 해제를 읽었다. 이 한정 검수를 전체 소스의 규칙 충족 선언으로 사용하지 않는다.

제품/리소스 수정, 빌드·테스트·실행·새 촬영·정적 분석·변경 후 검사·리소스/해시 대조는 수행하지 않았다. 기존 실제 History/Settings 촬영은 별도 승인으로 완료한 UI 자료이며 이번 아키텍처 경합이나 업데이트 설치 동작의 증거가 아니다. 검수 결과 문서와 이미 완료된 UI·README 변경만 커밋·푸시 대상으로 삼고 새 태그·배포 파일·GitHub 릴리스는 만들지 않는다.
