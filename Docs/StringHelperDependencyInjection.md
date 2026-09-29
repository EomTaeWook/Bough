# StringHelper의 Dignus DI 적용

## 현재 상태

`StringHelper`에는 `[Injectable(LifeScope.Singleton)]`이 있지만 앱에서 Dignus 컨테이너를 만들거나 해당 어셈블리를 등록하지 않는다. `MainWindow`가 언어를 정하고 `new StringHelper(language)`로 직접 생성한다. 어트리뷰트만으로는 현재 객체 생성 경로가 바뀌지 않는다.

## 변경 범위

1. Avalonia 앱 시작 지점에 Dignus `ServiceContainer`를 하나 만든다. 현재 UI 문화권으로 `StringLanguage`를 한 번 정해 인스턴스로 등록하고, `StringHelper`가 있는 앱 어셈블리의 `[Injectable]` 등록을 실행한다.
2. 컨테이너에서 `StringHelper`를 해석해 `MainWindow` 생성자에 전달한다. `MainWindow`에서 `new StringHelper(...)`를 제거하고 기존 ViewModel에 같은 인스턴스를 전달한다. 앱 실행 중 단일 언어 값을 쓰는 현재 동작과 Singleton 생명주기를 유지한다.
3. 컨테이너는 앱 수명 동안 보유하고 종료 시 지원되는 정리를 수행한다. View나 ViewModel이 전역 컨테이너에 직접 접근하지 않게 하고, 다른 서비스의 생성 방식을 이번 변경만을 위해 일괄 교체하지 않는다. 향후 전체 객체 그래프를 DI로 옮길 때는 StashViewModel 공유 인스턴스와 각 작업자의 파일 소유 범위를 먼저 조율한다.
4. 언어를 실행 중 변경하는 기능은 현재 범위가 아니다. 그런 기능이 생기면 `Language` 변경 뒤 화면 문자열 갱신 계약을 별도로 정의한다.

## 현재 단계

`StringHelper`가 Dignus DI에서 생성되어 MainWindow와 하위 ViewModel이 같은 인스턴스를 받도록 구현한다. 기능이 계속 바뀌는 동안 테스트 코드와 시나리오 테스트는 작성·실행하지 않는다. 필요한 경우 컴파일 확인만 한다.

## Avalonia XAML 경고

DI 생성자를 적용한 뒤 `MainWindow.axaml`에 `AVLN3001` 경고가 나온다. Avalonia의 런타임 XAML 로더가 사용할 공개 매개변수 없는 생성자가 없다는 뜻이다. 현재 앱은 `App`에서 의존성을 해석한 뒤 `new MainWindow(viewModel, stringHelper)`로 직접 창을 만들고, 창 안에서 `InitializeComponent()`를 호출한다. `MainWindow`를 URI로 런타임 생성하는 참조가 없다면 해당 로더 경로는 필요하지 않다. Avalonia 유지관리자도 이 경우 경고를 무시할 수 있다고 설명한다. [관련 논의](https://github.com/AvaloniaUI/Avalonia/discussions/19126)

작업자 3은 실제 로더 사용 여부와 현재 Avalonia 버전에서 지원하는 경고 처리 방식을 확인한다. 불필요한 경고라면 `AVLN3001`만 최소 범위에서 숨긴다. DI 없이 창을 만들 수 있는 공개 기본 생성자나 전역 서비스 조회를 경고 제거만을 위해 추가하지 않는다. 다른 Avalonia 경고까지 일괄 숨기지 않는다.
