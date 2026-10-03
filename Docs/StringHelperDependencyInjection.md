# 앱 언어와 StringHelper의 Dignus DI

Settings의 **언어**에서 한국어·영어를 선택한다. 선택은 즉시 적용하며 재시작 후에도 유지한다. OS의 UI 언어는 읽지 않는다. 저장된 값이 없거나 읽을 수 없는 경우 기본값은 한국어다. 언어 설정은 Git 설정과 별도로 사용자별 `%LocalAppData%/Bough/language.json`에 저장한다.

`App.axaml.cs`는 `Persistence/LanguageSettingsStore`에서 초기 언어를 읽고 `StringLanguageSelection`과 저장기를 앱 수명의 Dignus `ServiceContainer`에 등록한다. 같은 컨테이너에서 Singleton `StringHelper`와 `LanguageSelectionPresenter`를 해석한다. View나 대화상자는 별도 컨테이너나 언어 도우미를 만들지 않는다. 기존 `GitActionDialogs`의 선택적 인수 경로도 앱이 이미 해석한 공유 인스턴스를 사용하며 OS 언어 fallback을 제거했다. 새 호출에서는 공유 인스턴스를 명시적으로 전달한다.

## 실행 중 전파

`GitSettingsView → GitSettingsViewModel → LanguageSelectionPresenter → LanguageSettingsStore 저장 → StringHelper.Language 변경 → LanguageChanged → LanguageChangeBinding → 화면 라벨과 ViewModel.RefreshLocalization` 순서로 전달한다. Presenter는 변경 요청을 직렬화하고 저장이 성공한 뒤 활성 언어를 바꾼다. 저장이 실패하면 기존 언어를 유지하고 설정 화면에 오류를 표시한다.

- `LanguageChangeBinding`은 일반 컨트롤의 현재 시각 트리 연결 상태도 읽어 이미 붙은 화면에 즉시 구독한다. 창은 `Opened`에서 연결하고 `Closed`에서 해제한다. 컨트롤 분리·DataContext 변경 때 이전 구독을 해제하며, 문자열 도우미가 나중에 주입되는 원격 패널은 `Rebind`를 호출한다. UI 스레드에서 고정 라벨·메뉴·툴팁·접근성 이름을 갱신하고 바인딩 속성의 변경을 알린다. 다시 붙거나 열리는 화면은 현재 언어로 표시를 갱신한다. 상태 조회에는 [Avalonia 12.1.3의 공개 시각 트리 확장 API](https://github.com/AvaloniaUI/Avalonia/blob/12.1.3/src/Avalonia.Base/VisualTree/VisualExtensions.cs)를 사용한다.
- 상단 Local Changes·History·Settings의 라벨과 접근성 이름은 `MainWindowViewModel`이 공유 `StringHelper`에서 현재 언어로 읽는다. Settings 탐색 버튼은 설정 화면의 캐시된 제목이나 화면 구독 수명에 의존하지 않는다.
- History의 라벨·커밋 참조·변경 파일·파일 트리 항목은 항목별 알림을 전달한다. 참조 트리는 항목을 교체하지 않고 표시 문구를 갱신해 선택·펼침 상태를 유지한다.
- `LocalizedText`는 안내·미리보기·상태·오류의 키와 원래 인수를 보관하고 표시 시점의 언어로 변환한다. Git 로그·파일 내용·경로·브랜치 이름과 사용자 입력은 원문을 유지한다. 조합된 레거시 작업 로그까지 번역 가능한 상태로 전환했다고 기록하지 않는다.
- Local Changes의 상태 아이콘 툴팁·문구·접근성 도움말은 파일과 공유 문자열 도우미·활성 언어를 받는 `WorktreeStatusTextConverter`로 갱신한다. 언어 전환을 위해 파일 목록이나 선택을 다시 만들지 않는다.

언어 변경만으로 Git 조회·화면 재생성·입력 초기화를 수행하지 않는다. 현재 저장소·커밋·검색·선택과 충돌 편집 결과를 유지한다. 사용자 표시 리소스의 원본은 `Excel/String.xlsx`, 앱의 로드 데이터는 `Datas/String.json`이며 생성 C#은 직접 수정하지 않는다.

이번 변경은 사용자 지시에 따라 빌드·테스트·UI 실행·리소스 대조를 수행하지 않았다.
