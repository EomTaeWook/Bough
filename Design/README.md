# Bough 기획

이 폴더는 Bough의 화면과 작업 흐름을 설명한다. 스크린샷은 배치 참고용이며 문서에는 계획 중인 기능도 포함된다. 배포된 기능과 파일은 [릴리스 기록](../Docs/Releases.md)에서 확인한다.

| 문서 | 내용 |
| --- | --- |
| [화면과 동작](GitClientWorkflow.md) | 탐색, 로컬 변경, 브랜치, 설정, 계정, 터미널의 사용자 흐름 |
| [UI 간격과 동작 배치](UIConsistency.md) | 사이드바, 메뉴, 도구막대와 대화상자의 담당별 정리 |
| [저장소 복제](RepositoryClone.md) | 원격 주소와 생성 위치 입력, Git 복제·취소·완료 후 저장소 열기 |
| [커밋·브랜치 우클릭 동작](CommitActions.md) | 체크아웃, 브랜치 생성·초기화, 커밋 Revert와 계속·중단 |
| [태그 삭제](TagDeletion.md) | 로컬·원격 태그 삭제의 확인, 작업 큐, 변경된 대상 보호 |
| [브랜치·태그 이름 변경](ReferenceRename.md) | 로컬 참조 이름 변경, 주석·서명 보존과 작업 큐 |
| [커밋 상세와 파일 탐색](CommitInspection.md) | 커밋 상세의 변경 파일 검색·diff·파일 메뉴, 내부 File Tree |
| [파일 히스토리](FileHistory.md) | 파일별 커밋 목록·날짜·이름 변경 이력, 당시 경로와 줄 번호 diff |
| [충돌 해결 창 진입](ConflictEntry.md) | 실제 충돌 시 해결 창 자동 열기와 Local Changes에서 다시 열기 |
| [원격 작업과 Stash](RemoteAndStash.md) | Fetch, Pull, Push, Stash의 화면·실행·갱신 흐름 |
| [원격 변경 자동 확인](RemoteAutoFetch.md) | 설정한 주기의 백그라운드 Fetch와 받을 새 커밋 표시 |
| [서브모듈 저장소 열기](SubmoduleNavigation.md) | 실제 하위 저장소 확인·전환과 해당 저장소의 Fetch·Pull·Push 사용 |
| [원격 주소와 GitHub 인증](GitHubAccountSwitching.md) | 원격 카드, 주소 출처, 별도 푸시 주소 제거 및 GCM 로그인 |
| [화면 테마](AppearanceTheme.md) | 기본 밝은 테마와 어두움·시스템 설정 전환 |
| [앱 업데이트](ApplicationUpdate.md) | 정식 릴리스 조회·다운로드·교체, 좌하단 진행과 GitHub 페이지 버튼 |
| [저장소 전환 응답성](RepositorySwitchResponsiveness.md) | 저장소 선택 직후 보여 줄 상태와 영역별 로딩 순서 |

구현 구조와 작업자별 지시는 [개발 문서](../Docs/README.md)에서 관리한다.

