# 재사용 아키텍처의 Bough 적용

## 기준과 우선순위

Bough는 Eidolon 같은 응용프로그램의 적용 기준 사례다. 다른 앱으로 전달할 공통 경계와 매핑 항목은 [데스크톱 응용프로그램 기준](DesktopApplicationArchitecture.md)을 따르며 Bough 전용 Git 기능·현재 구현 상태는 이 프로젝트에 둔다.

`Teeeb/ReusableArchitecture`의 공통 컨벤션과 계층형 MVP 원칙을 Bough의 .NET·Avalonia·Git 실행 경계에 매핑한다. 이 문서는 채택 여부·실제 경로·프로젝트 차이를 소유하며 원본 패키지를 복제하지 않는다. 현재 사용자 지시, [AGENTS.md](../AGENTS.md), 기능별 확정 계약을 우선한다.

원본 링크는 Bough와 Teeeb가 같은 상위 폴더에 있는 작업 환경을 기준으로 한다. 외부 카탈로그를 사용할 수 없는 환경에서도 Bough의 [코딩 컨벤션](CodingConvention.md)과 [아키텍처](AvaloniaMvpAdoption.md)가 현재 프로젝트 계약이다. 문서 적용은 전체 코드가 규칙을 충족한다는 실행·검수 결과가 아니다.

## 채택 범위

| 원본 | Bough에서 적용할 내용 | 프로젝트 계약 |
| --- | --- | --- |
| [공통 컨벤션](../../Teeeb/ReusableArchitecture/CodingConvention.md) | 최소 변경, 실제 책임별 타입, 구성과 생성자 DI, 데이터 단일 소유자, 검사 위치, C#·문서·생성물 규칙 | [코딩 컨벤션](CodingConvention.md) |
| [Unity 계층형 MVP](../../Teeeb/ReusableArchitecture/UnityClientArchitecture.md) | 원본 상태·표현 분리, 입력과 확정 결과, 지역화·서식, 구독·비동기 수명 | [Avalonia MVP 적용](AvaloniaMvpAdoption.md) |
| 공통 컨벤션의 작업 큐·영속 확정 원칙 | 접수·실행·완료 구분, await 동안 실행 소유권 유지, 확정된 성공과 후속 읽기 오류 분리 | [Git 작업 큐](GitOperationQueue.md), [오류 지역화](GitErrorLocalization.md) |
| 공통 컨벤션의 데이터 원본·생성물 원칙 | 원본과 산출물 소유, 기존 ID·내용 보존, 원본 변경과 생성 절차 구분 | [데이터 변환](<데이터 변환 도구 사용법.md>) |

Unity·서버 전용 규칙은 해당 구조가 없으면 적용하지 않는다. 규칙을 적용하기 위해 없는 기능이나 계층을 만들지 않으며 작업자 역할은 [AGENTS.md](../AGENTS.md)를 유지한다.

## 실제 경계 매핑

| 개념 | Bough 소유자·경로 | 연결 기준 |
| --- | --- | --- |
| Domain / Gateway | `Bough.Core/Git`, `Conflicts`, `Updates` | Git·파일·HTTP·파싱·입력 및 현재 상태 판정. Avalonia·표시 언어에 의존하지 않는다. |
| Presenter | `Bough.App/Presenters` | 사용자 요청, 기존 FIFO 또는 독립 읽기 수명, 취소·결과 적용 순서. 완성된 UI 문장을 만들지 않는다. |
| Presentation Model | `Bough.App/ViewModels`, `ViewModels/Models` | 기존 바인딩, 원본 값·선택·진행·코드형 오류 상태. VM이라는 기존 이름을 유지한다. |
| View / UIActions | AXAML·코드비하인드·기존 ICommand와 입력 메서드 | 지역화·서식·초점·스크롤·OS 표시 연결. 새로운 UIActions 계층을 의무화하지 않는다. |
| Lifecycle Adapter | `App.axaml.cs`, `Composition`, View 연결·분리와 창 종료 | DI 구성, 모델·Presenter 연결, 구독 해제와 자신이 소유한 자원 정리. |
| 변경 실행 | `GitOperationQueue`의 저장소별 lane | 명령 하나의 실행·필요 완료 조회까지 소유. 요청 시점 대상과 실행 직전 기대값을 보존한다. |
| 읽기 실행 | 기능별 미리보기·조회 Presenter | Task/취소로 기다리고 UI 입력을 계속 받는다. 선택 미리보기는 실행 하나·최신 대기 하나를 사용한다. |
| 표현 원본 | `StringHelper`, `LocalizedText`, `LanguageChangeBinding` | 키·인수·원본 예외를 유지하고 View에서 현재 언어로 표시한다. 언어 변경으로 Git을 다시 실행하지 않는다. |
| 데이터 원본·생성 | `Excel/String.xlsx`, `ExportTools`, `Datas`, `DataContainer/Generated` | 공유 원본·JSON은 총괄 소유, 생성 C#은 직접 수정하지 않는다. |

## 프로젝트 차이와 유지할 경계

- 재사용 문서의 enum별 파일 예시 대신 Bough의 각 프로젝트 `Internals/Enums.cs`를 사용한다. 상태와 결과 모델의 실제 책임은 구분하며 불필요한 enum·Result를 만들지 않는다.
- 직접 관리하는 일반 C# 타입을 파일 길이 때문에 `partial`로 나누지 않는다. Avalonia 생성 선언과 결합하는 `App`·Window·UserControl 코드비하인드의 기존 `partial`은 프레임워크 연결 경계로 유지한다. 생성 파일은 편집하지 않는다. 기존 수동 분할의 정리는 이후 승인된 구현 범위에서 책임·호출부를 함께 조정한다.
- 서비스는 기존 Dignus DI 등록·생성자 주입을 사용한다. 화면별 모델과 고정 입력을 받아 만드는 Presenter의 구성 수명은 기존 App·Composition 경계에 매핑한다. 단일 사용 서비스도 서비스 생성·정적 접근을 소비자에 숨기지 않는다. 현 코드의 남은 직접 구성은 단계적 이행 대상으로 구분한다.
- Git·파일·업데이트는 기존 구조화된 결과·오류와 원래 예외, Git 성공과 후속 조회 실패의 구분을 유지한다.
- Unity 이벤트 전용 흐름을 강제하지 않는다. 같은 실행 경계에서는 기존 직접 호출·Task 반환을 사용하고 실제 다른 화면 상태를 갱신할 때만 확정된 결과를 기존 이벤트·완료 callback으로 전달한다. 같은 Git 변경을 수신자가 다시 실행하지 않는다.
- 문자열 모델은 Bough의 `LocalizedText` 키·인수 계약을 유지한다. 최종 번역·단위·서식은 View가 소유하며 기존 번역 문자열 속성은 해당 기능 수정 때 원본 상태로 이행한다. Git 출력·파일 본문·SHA·사용자 입력은 번역하지 않는 데이터다.
- 설정·최근 저장소·인증의 기존 저장 소유자와 파일 위치를 유지한다. 앱 업데이트의 helper는 동일 실행 파일과 요청별 검증 사본만 소비하며 사용자 설정을 따로 초기화하거나 복제하지 않는다.
- Excel·JSON의 전체 동기화 미결 때문에 기존 항목을 보존하는 총괄의 제한된 동시 병합을 유지한다. 이를 생성 C# 편집이나 임의 JSON 단독 수정의 허가로 확대하지 않는다.
- 입력·기대 OID·HEAD·작업 상태·경로·digest·설치 승인 등 기능의 런타임 검사는 유지한다. 작업 검증 중단을 해당 무결성 검사 제거로 해석하지 않는다.

## 작업 범위와 미결

이번 반영은 문서 규칙과 프로젝트 매핑 갱신이다. 기존 소스 전체의 `partial`, 타입별 파일, DI·표시 책임 준수 여부를 전수 검사하거나 일괄 리팩터링하지 않았다. 해당 구현을 요청받을 때 현재 코드의 필요한 선언·호출·등록을 읽어 이행 범위와 남은 연결을 인계한다.

`[TBD]`는 아직 결정하지 않은 프로젝트 계약에만 사용하고 기존에 확정한 실행·데이터·오류 계약을 지우지 않는다. 실제 반영 범위는 [구현 현황](CurrentStatus.md)이 소유한다. Git·검증·배포 권한은 [AGENTS.md](../AGENTS.md), 표현·구현 규칙은 [코딩 컨벤션](CodingConvention.md)이 소유한다.
