# StringHelper의 Dignus DI

`App.axaml.cs`는 현재 UI 문화권에서 `StringLanguageSelection`을 만들고 앱 수명의 Dignus `ServiceContainer`에 등록한다. 같은 컨테이너에서 `StringHelper`와 화면·Git 서비스를 해석해 `MainWindow` 생성자에 전달한다. 일반 화면 경로는 공유 인스턴스를 전달하며, 일부 기존 `GitActionDialogs` 호출에는 `StringHelper`가 전달되지 않았을 때의 생성 fallback이 남아 있다. 새 호출부에서는 공유 인스턴스를 전달한다.

한국어 UI 문화권에서는 한국어, 그 밖에는 영어를 사용한다. 실행 중 언어 전환은 현재 기능에 포함되지 않는다. 언어 전환을 추가할 때는 창·메뉴·툴팁과 이미 생성된 화면 항목의 문자열 갱신 계약을 함께 정의해야 한다.

`MainWindow`는 앱 시작 코드에서 의존성을 주입받아 생성되고 창 안에서 `InitializeComponent()`를 호출한다. XAML 로더 경고를 피하기 위해 전역 서비스 조회나 기능 없는 공개 기본 생성자를 추가하지 않는다. 새 생성자 인수가 생기면 `App.axaml.cs`의 등록·해석을 함께 수정한다. 실제 앱 빌드는 [현재 검증 방침](../AGENTS.md#현재-검증-방침)에 따라 사용자가 요청한 경우에만 수행한다.
