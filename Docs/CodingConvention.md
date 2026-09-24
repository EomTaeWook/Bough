# 코딩 컨벤션

> 적용 범위: Bough에서 직접 관리하는 C#과 Markdown. `DataContainer/Generated`의 C#은 변환기 산출물이므로 직접 수정하지 않는다.

## C#

- 직접 관리하는 프로젝트의 `<Nullable>disable</Nullable>`을 유지한다. nullable 타입 표기(`T?`), null-forgiving 연산자(`!`), `#nullable enable`을 사용하지 않는다.
- private 필드는 `_camelCase`로 작성한다. 저장소 루트의 [`.editorconfig`](../.editorconfig)가 이 규칙을 정의한다.
- 명시적인 null 검사와 `throw` 문을 유지할 수 있도록 Visual Studio의 `IDE0016`, `IDE0029`, `IDE0030`, `IDE0270` 스타일 제안은 저장소의 `.editorconfig`에서 숨긴다. 컴파일러의 실제 null 관련 오류나 다른 분석 경고까지 끄지 않는다.
- `CA1861`이 지적하는 반복 생성 상수 배열은 호출 대상이 배열을 수정하지 않는다는 점을 확인한 뒤 private `static readonly` 필드로 재사용한다. 값이 호출마다 달라지거나 호출 대상이 배열을 수정할 수 있으면 공유하지 않는다. 이 성능 경고를 프로젝트 전체에서 숨기지 않는다.
- 현재 코드와 같이 중괄호가 있는 네임스페이스와 명시적인 타입을 사용한다. 직접 관리하는 클래스에는 `sealed`를 사용하지 않는다.
- 삼항 연산자, `checked`·`unchecked`, 익명 객체를 사용하지 않는다.
- 모든 메서드 선언과 호출에서 여는 괄호 `(` 직후 줄바꿈하지 않는다. 첫 번째 매개변수나 인수는 여는 괄호와 같은 줄에 둔다. 나머지 항목도 읽을 수 있으면 같은 줄에 둔다.
- 서로 다른 검증 조건은 각각의 `if`에서 검사하고, 오류에는 실패한 조건과 관련 값을 남긴다.
- 이벤트 핸들러·명령 메서드뿐 아니라 **비동기 응답의 유효성 검사도 조건 하나당 하나의 `if`**로 검사하고 실패하면 바로 반환한다. 요청 버전, 저장소, 선택 항목, 미리보기 버전은 서로 독립된 조건이므로 `&&`·`||`로 한 가드문에 묶지 않는다. 정상 흐름은 검증문 뒤에 둔다. 하나의 의미를 이루는 단일 논리식까지 기계적으로 쪼갤 필요는 없다.

```csharp
if (DataContext is not ReferenceExplorerViewModel viewModel)
{
    return;
}

if (TopLevel.GetTopLevel(this) is not Window owner)
{
    return;
}

if (viewModel.CurrentRepository == null)
{
    return;
}

if (viewModel.IsBusy)
{
    return;
}

if (_menuNode?.Target is not GitRemoteBranch branch)
{
    return;
}

await CheckoutRemoteBranchAsync(viewModel, branch, _menuRepositoryRoot);
```

비동기 결과를 적용하기 전에도 각각 분리한다.

```csharp
if (previewVersion != _previewVersion)
{
    return;
}

if (requestVersion != _requestVersion)
{
    return;
}
```

- 필요하지 않은 중간 변수나 미래 기능만을 위한 상태를 추가하지 않는다.

## 변경과 검증

- 요청을 해결하는 데 필요한 범위만 수정한다. 생성 코드를 직접 고쳐야 할 상황이면 원본과 변환 절차를 확인한다.
- 데이터 원본과 산출물, 도구 실행 위치는 [데이터 변환 도구 사용법](<데이터 변환 도구 사용법.md>)을 따른다.
- C#과 Markdown의 줄 끝은 CRLF로 유지한다.
- Visual Studio의 **일관성 없는 줄 끝** 경고를 띄우지 않으려면 현재 경고 창의 **이 대화 상자 항상 표시**를 해제하거나 `도구 > 옵션 > 환경 > 문서 > 로드 시 일관된 줄 끝 확인(Check for consistent line endings on load)`을 끈다. 이 설정은 개발자별 Visual Studio 설정이며 저장소 파일의 줄 끝 규칙은 위의 CRLF를 따른다.
- 변경 후 파일 경로와 상대 링크를 확인하고 `git diff --check`를 실행한다.
