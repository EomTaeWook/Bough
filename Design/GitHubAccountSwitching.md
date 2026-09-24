# GitHub 인증 계정 전환

## 범위와 화면

- 사용자가 바꾸려는 것은 **Fetch·Pull·Push에 쓰는 GitHub 인증 계정**이다. 커밋에 기록되는 `user.name`·`user.email`과 히스토리 아바타용 GitHub 사용자명 연결은 별개이며 이 화면에서 함께 바꾸지 않는다.
- Git 설정의 **Accounts and authentication**에서 현재 저장소의 GitHub HTTPS 원격과 **이 저장소에 선택한 인증 계정**을 보여 준다. 원격이 여러 개면 원격별로 대상과 적용 범위를 구분한다. 계정 목록에서 기존 계정을 선택하거나 **다른 GitHub 계정 추가…**를 통해 Git Credential Manager(GCM)의 로그인 창을 연다. 앱은 비밀번호·토큰을 입력받거나 저장하지 않는다.
- 계정 선택은 현재 저장소에만 적용한다. 전역 GitHub 계정, 다른 저장소의 원격, 커밋 작성자 정보는 바꾸지 않는다. 선택 전에는 적용 원격과 GitHub 사용자명을 보여 준다. 성공 후에는 선택된 사용자명과 **다음 원격 작업부터 적용**이라는 상태를 표시한다. 자격 증명 보유 여부를 실제로 확인하지 못했다면 **로그인됨**이라고 단정하지 않는다.
- 원격이 GitHub HTTPS가 아니거나 GCM을 사용할 수 없으면 선택 UI를 비활성화하고 이유를 보여 준다. SSH 원격은 SSH 키가 인증 계정을 결정한다는 점을 설명한다. 이때 HTTPS 계정 선택을 SSH 작업에 적용된다고 표시하지 않는다.

## 실행 계약

- GCM이 제공하는 계정 목록·로그인 기능을 사용한다. 목록이 비어 있거나 불완전해도 GitHub 사용자명을 직접 지정하고 GCM 로그인으로 추가할 수 있게 한다. 계정을 바꿀 때 기존 계정의 자격 증명을 지우거나 모든 GitHub 저장소에서 로그아웃하지 않는다.
- 선택한 사용자명을 **현재 저장소의 Git credential 설정**에 연결해 Git이 해당 GitHub HTTPS 원격의 인증 요청에서 그 사용자를 GCM에 전달하게 한다. 사용자명 문자열만 앱에 저장하고 인증 수단은 GCM에 맡긴다. Git이 실제로 선택한 계정과 다르거나 권한이 없으면 원격 작업의 오류를 보여 주고 계정 선택·재로그인으로 돌아갈 수 있게 한다. 원격 URL을 바꿔야 하는 구현이라면 변경 전 URL을 보여 주고 해당 원격만 변경하며, URL에 비밀번호·토큰을 넣지 않는다.
- 설정만 고를 때 네트워크 작업을 자동 실행하지 않는다. 진행 중 Fetch·Pull·Push에는 계정을 바꾸지 못하게 하거나 다음 작업에만 적용되게 한다. 취소·로그인 실패·설정 저장 실패 시 기존 계정 선택을 유지한다. 저장소 전환 뒤 늦은 계정 조회 결과가 다른 저장소 화면을 덮어쓰지 않는다.

## 근거

- [Git Credential Manager의 여러 사용자 안내](https://github.com/git-ecosystem/git-credential-manager/blob/main/docs/multiple-users.md)
- [GitHub의 여러 계정 사용 안내](https://docs.github.com/en/account-and-profile/how-tos/account-management/managing-multiple-accounts)
