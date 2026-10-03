# Bough 개발 지침

이 폴더는 AI 작업 정책, 코딩 규칙, 도구 사용법 등 개발 운영 문서를 기록한다. 프로젝트 개요와 실행 방법은 [저장소 README](../README.md)를 참고한다.

**현재는 사용자 지시에 따라 추가 검증을 중단한 상태다.** 빌드·테스트·UI 실행·캡처·diff check·리소스 대조는 사용자가 다시 명시적으로 요청할 때까지 수행하지 않는다. 각 문서의 검증 절차보다 [현재 검증 방침](../AGENTS.md#현재-검증-방침)을 우선한다.

| 문서 | 내용 |
| --- | --- |
| [코딩 컨벤션](CodingConvention.md) | C#·AXAML·Markdown 서식, 역할별 폴더·네임스페이스, 책임·DI·작업 큐, 아이콘·글씨·테마와 작업 정책 |
| [데이터 변환 도구 사용법](<데이터 변환 도구 사용법.md>) | `Excel/String.xlsx`에서 JSON·C#을 생성하는 순서와 검증 |
| [현재 아키텍처와 Avalonia MVP 적용](AvaloniaMvpAdoption.md) | 프로젝트·역할별 폴더 구성, DI 수명·화면 조정·표현·배포의 현재 구조와 남은 MVP 이행 |
| [구현 현황](CurrentStatus.md) | 반영된 기능, 진행 중인 작업과 남은 검증 |
| [릴리스](Releases.md) | 배포 버전, 다운로드와 패키지 생성 기록 |
| [공통 릴리스 정책](ReusableArchitecture/ReleasePolicy.md) | 단일 실행 파일 배포와 필수 데이터·설정 포함 규칙 |
| [구현 작업 지시](ImplementationPlan.md) | 작업자별 소유 파일, 구현 순서와 검증 기준 |
| [저장소별 Git 작업 큐](GitOperationQueue.md) | 변경 명령의 저장소별 FIFO 실행 경계 |
| [브랜치 생성 창의 작업 큐](BranchDialogQueue.md) | 창 내부 입력 큐와 명시적 취소 |
| [Git 오류 지역화](GitErrorLocalization.md) | Core 오류 코드와 앱 표시 언어의 연결 |
| [메인 창 역할 분리](MainWindowViewModelRefactor.md) | 메인 창·충돌 화면·저장소 목록의 책임 |
| [StringHelper DI](StringHelperDependencyInjection.md) | 문자열 도우미 생성과 주입 경계 |
| [재사용 아키텍처 카탈로그](ReusableArchitecture/README.md) | 프로젝트 독립 규칙과 적용 가이드 |

사용자 화면과 작업 흐름의 기획은 [기획 문서](../Design/README.md)에서 관리한다. 저장소 복제의 입력·실행·결과는 [저장소 복제 기획](../Design/RepositoryClone.md)을 따른다.
