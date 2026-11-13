# JyunoSdk

[Jyuno](https://github.com/jyunrcaea/Jyuno) 언어를 위한 SDK 입니다. (이전 이름: JyunoPrompt)

`jyuno` 명령어 하나로 다음을 제공합니다.

- **대화형 프롬프트(REPL)**: 한 줄씩 실행하고, `if`/`while`/`repeat`/`for`/`func` 블록도 여러 줄로 입력할수 있습니다.
- **스크립트 실행/검사/정리**: `jyuno run`, `jyuno check`, `jyuno format`
- **언어 서버(LSP)**: `jyuno lsp` — VS Code 등 LSP를 지원하는 편집기에서 진단, 자동 완성, 정의로 이동 등을 제공합니다.
- **Native JS**: `jyuno <파일> --native-js` — Jyuno 코드를 런타임이 포함된 JavaScript 파일로 변환합니다. 브라우저나 Node.js에서 .NET 없이 실행됩니다.

각 기능은 NuGet 라이브러리(`JyunoSdk.Analysis`, `JyunoSdk.LanguageServer`, `JyunoSdk.NativeJs`)로도 배포되므로, 다른 프로그램에서 패키지를 추가해서 쓸수 있습니다.

## 구성

| 프로젝트 | 설명 |
| --- | --- |
| `src/JyunoSdk.Analysis` | 실행하지 않고 Jyuno 코드를 분석하는 라이브러리 (위치가 있는 토큰, 블록 구조, 변수 영역, 이름 해석, 진단, 포맷팅, 내장 명령어 문서) |
| `src/JyunoSdk.LanguageServer` | Language Server Protocol 3.17 구현 (표준 입출력 JSON-RPC, 외부 의존성 없음) |
| `src/JyunoSdk.NativeJs` | Jyuno 코드를 JavaScript로 변환 (Native JS). 분석은 Jyuno 라이브러리의 `Jyuno.Analysis`를 사용 |
| `src/JyunoSdk.Cli` | `jyuno` 명령어 (REPL, run, check, format, lsp, --native-js) |
| `tests/JyunoSdk.Tests` | 테스트 |

Jyuno 인터프리터는 NuGet의 [`Jyuno`](https://www.nuget.org/packages/Jyuno) 패키지를 사용합니다. (버전은 `Directory.Build.props`의 `JyunoVersion`)

## 설치

### 실행 파일 (권장)
[Releases](https://github.com/jyunrcaea/JyunoSdk/releases)에서 운영체제에 맞는 `jyuno` 파일을 받으세요. .NET 설치 없이 실행됩니다.

### dotnet tool
```
dotnet tool install -g JyunoSdk
```

### 소스에서 빌드
.NET 8 SDK가 필요합니다.
```
dotnet run --project src/JyunoSdk.Cli
```

## 사용법

```
jyuno                          대화형 프롬프트(REPL)를 실행합니다.
jyuno <파일> [옵션]            스크립트 파일을 실행합니다.
jyuno run <파일> [옵션]        스크립트 파일을 실행합니다.
jyuno <파일> --native-js [출력 파일]
                               실행하지 않고 JavaScript(Native JS)로 변환합니다.
                               (출력 파일을 생략하면 스크립트 이름의 확장자를 .js로 바꾼 파일)
jyuno check <파일...> [옵션]   스크립트를 실행하지 않고 문법/이름 문제를 검사합니다.
jyuno format <파일...> [--write] [--spaces <n>]
                               블록 구조에 맞춰 들여쓰기를 정리합니다.
jyuno lsp                      Jyuno 언어 서버(LSP)를 표준 입출력으로 실행합니다.
jyuno --version                버전을 출력합니다.

옵션:
  --file, -file                파일/디렉터리를 다루는 명령어(file.*, directory.*)를 허용합니다.
  --commands <묶음,...>        사용할 명령어 묶음 (console, math, file, default, all)
  --host <이름,...>            (check) 프로그램이 따로 제공하는 함수/변수 이름
  --optimize, -O               (실행) 실험적 최적화(탈출 분석, 인라이닝)를 켭니다. (Jyuno 1.5.2부터)
  --native-js [출력 파일]      Native JS로 변환해서 저장합니다. (node 출력파일.js 로 실행)
```

### 대화형 프롬프트
```
>>> x = add 1 2
>>> x
3
>>> func double
... param n
... return (mul n 2)
... endfunc
>>> double 21
42
>>> help math.pow
```
- `if`, `while`, `repeat`, `for`, `func`로 시작하면 블록이 `end`/`endfunc`로 닫힐 때까지 입력받은 다음 한번에 실행합니다. 블록 입력 중 `.cancel`을 입력하면 취소합니다.
- 결과가 `null`이 아니라면 출력합니다. 배열/리스트는 `[1, 'a', 2.0]`처럼 보여줍니다.
- `goto`는 프롬프트에서 쓸수 없습니다. (파일로 저장한 다음 `execute "파일"`로 실행하세요)

프롬프트에서만 제공되는 명령어:
- `exit` : 프롬프트를 나갑니다.
- `help` / `help (이름)` : 도움말, 또는 함수/키워드 설명을 봅니다. (예: `help add`, `help for`)
- `execute (파일 경로)` : 파일을 실행합니다.
- `history` : 지금까지 입력한 줄을 반환합니다.
- `eval (문자열)` : 문자열을 한 줄의 코드로 실행합니다.
- `.input`, `.return`, `.error` (변수) : 입력 표시(`>>> `), 결과 서식(`{0}`), 오류 서식(`오류: {0}`)을 바꿀수 있습니다.
- `.remove`, `.substitute` (변수) : 삭제/대입 연산 허용 여부
- 프롬프트에서는 파일 명령어(`file.*`, `directory.*`)를 포함한 모든 명령어를 쓸수 있습니다.

### 검사
```
$ jyuno check palindrome.jyuno
palindrome.jyuno:6:21: warning JY2001: 'text.at'은/는 정의되지 않은 함수/변수입니다.
오류 0개, 경고 1개
```
스크립트를 실행할 프로그램이 따로 추가하는 함수가 있다면 `--host text.at,other`로 알려주세요. 오류가 있으면 종료 코드 1을 반환합니다.

## 언어 서버

`jyuno lsp`는 표준 입출력으로 [Language Server Protocol](https://microsoft.github.io/language-server-protocol/)을 주고받습니다. (`--stdio` 등 뒤에 붙는 인자는 무시합니다)

### 제공하는 기능
- **진단**: 문법 오류(닫히지 않은 문자열/괄호, end/endfunc 짝, else/break/param 위치, func/param/for/local 형식, 연산자 식), 정의되지 않은 이름, 괄호 없이 인자로 넘긴 함수, 무시되는 연산자/토큰, `goto`로 함수 안팎 이동 등
- **자동 완성**: 키워드, 블록 스니펫(`if … end`, `func … endfunc` 등), 지금 위치에서 보이는 변수/함수/파라미터, 내장 명령어(설명 포함)
- **hover**: 내장 명령어 설명, 사용자 함수 시그니처, 변수 종류(지역/파라미터/레이블/상수 등), 연산자(`+`, `&&`, `+=` 등) 설명
- **시그니처 도움말**: 내장 함수와 사용자 함수의 인자
- **정의로 이동, 참조 찾기, 같은 이름 강조, 이름 바꾸기**: Jyuno의 영역 규칙(함수 지역 변수, `local`, `for` 반복 변수, 바깥 변수 대입)을 따름
- **문서 심볼**(함수 안의 파라미터/지역 변수 포함), **접기**(블록), **포맷팅**(들여쓰기 정리)
- **의미 토큰**(semantic highlighting): `keyword`, `function`, `variable`, `parameter`, `string`, `number`, `operator`, `label` / 수식자 `declaration`, `readonly`, `defaultLibrary`
- **빠른 수정**: 괄호 없이 넘긴 함수 `add f 1` → `add (f) 1`, `return add 1 2` → `return (add 1 2)`

### 설정
`initializationOptions` 또는 `workspace/didChangeConfiguration`의 `jyuno` 항목으로 설정합니다.
```json
{
  "commands": ["console", "math"],
  "hostNames": ["text.at"],
  "undefinedNames": true
}
```
- `commands`: 스크립트를 실행할 프로그램이 제공하는 명령어 묶음. `"default"`(console, math), `"all"`, 또는 `["console", "math", "file"]` 같은 배열 (기본값 `"default"`)
- `hostNames`: 프로그램이 `Runtime.AddFunction`/`AddVariable`로 추가하는 이름들
- `undefinedNames`: 정의되지 않은 이름을 경고할지 (기본값 `true`)

### 편집기에서 사용하기
VS Code 확장은 [vscode-languageclient](https://www.npmjs.com/package/vscode-languageclient)로 `jyuno lsp`를 실행하면 됩니다.
```ts
const serverOptions = { command: "jyuno", args: ["lsp"] };
const clientOptions = { documentSelector: [{ language: "jyuno" }] };
new LanguageClient("jyuno", "Jyuno", serverOptions, clientOptions).start();
```
Neovim (0.11+):
```lua
vim.lsp.config('jyuno', { cmd = { 'jyuno', 'lsp' }, filetypes = { 'jyuno' } })
vim.lsp.enable('jyuno')
```

### 진단 코드
| 코드 | 심각도 | 설명 |
| --- | --- | --- |
| JY1001 | 오류 | 토큰화 오류 (닫히지 않은 문자열, long 범위를 넘는 정수) |
| JY1002 | 오류 | 블록 구조 오류 (end/endfunc/endstruct 짝, else, break, param 위치) |
| JY1003 | 오류 | 닫히지 않은 괄호 |
| JY1004 | 오류 | 키워드 형식 오류 (func/struct/param/for/local) |
| JY1005 | 오류 | 줄 맨 앞이 아닌 곳에 쓴 키워드 |
| JY1006 | 오류 | goto로 함수 안팎을 넘나듦 |
| JY1007 | 오류 | 연산자 식 문법 오류 (값이 없는 연산자, 구조체 밖이나 다시 정의할수 없는 연산자 함수 등, Jyuno 1.6.0부터) |
| JY2001 | 경고 | 정의되지 않은 함수/변수 |
| JY2002 | 경고 | 함수를 괄호 없이 인자로 넘김 (값을 읽을수 없음) |
| JY2003 | 경고 | 실행되지 않고 무시되는 토큰 |
| JY2004 | 경고/오류 | if/while/repeat/goto/return의 값이 없거나 둘 이상 |
| JY2005 | 경고 | 설정된 프로그램이 제공하지 않는 명령어 묶음 (예: file) |

## Native JS
> 소개, 사용법, 제약, 동작 원리, 연구 결과(성능과 호환성 검증)는 [위키](https://github.com/jyunrcaea/JyunoSdk/wiki/Native-JS)에 자세히 정리되어 있습니다.

Jyuno 코드를 JavaScript로 변환합니다. 만들어진 파일에는 Jyuno 런타임(값, 연산, console/math 명령어)이 들어 있어서, 다른 파일 없이 Node.js나 브라우저에서 실행됩니다.
인터프리터와 같은 결과(출력, 반환값, 오류 메시지와 줄)를 내도록 차등 테스트로 검증했고, 반복문이나 함수 호출이 많은 코드에서 훨씬 빠릅니다.
0.5.0부터 Jyuno 1.6.0의 연산자(`+`, `==`, `&&`, `?:`, `x += 1` 등)와 구조체의 연산자 함수(`func +`)도 변환합니다. `&&`, `||`, `??`, `?:`는 인터프리터처럼 필요한 쪽만 계산합니다.
```
jyuno hello.jyuno --native-js          # hello.js 생성
jyuno hello.jyuno --native-js out.js   # out.js 생성
node hello.js                          # 표준 입출력으로 실행
```

### 제약
변수와 함수를 미리 정해진 자리에 두고 직접 호출하도록 변환하기 때문에, 다음 코드는 변환하지 않고 오류로 알려줍니다.

| 제약 | 예 |
| --- | --- |
| 변수/함수 삭제를 쓸수 없음 | `x =` |
| 내장 함수/상수(`add`, `mul`, `console.writeline`, `true`, `math.pi` 등)와 프로그램이 제공하는 함수를 재정의/대입할수 없음 | `add = 1`, `func mul`, `5 = true`, `for add 'abc'` |
| goto로 블록(if, while, repeat, for) 안쪽으로 들어갈수 없음 | `goto inner` (inner가 if 안) |
| 블록 구조 오류(짝이 없는 end 등)는 실행 전에 알려줌 | |
| 구조체(`struct`) 생성자 안에서 다른 구조체를 정의할수 없음 | `struct a` 안의 `struct b` |
| 구조체 값을 담은 변수의 멤버 이름(`p.x`, `p.show`)을 `func`/`local`/`param`/`struct`로 다시 정의할수 없음 (실행 중 오류) | `p = point 1 2` 다음의 `func p.show` |

프로그램이 제공하는 변수(`console.title` 등)에 값을 대입하는건 허용됩니다. 계산된 goto 대상이 블록 안쪽이라면 실행 중에 `Native JS 제약` 오류가 납니다.
`file.*`, `directory.*` 명령어는 지원하지 않습니다.

### 만들어진 코드 사용하기
만들어진 파일은 전역 변수 `JyunoProgram`(CommonJS라면 `module.exports`)을 정의합니다.
```js
const program = require('./hello.js');
const result = program.run({
  input: '입력\n',                         // console.readline/console.read가 읽을 문자열
  write: (text) => process.stdout.write(text), // 생략하면 result.output으로 모아서 돌려줌
  host: { 'text.upper': (args) => String(args[0]).toUpperCase() }, // 프로그램이 제공하는 함수
});
// result: { status: 'ok' | 'error' | 'grammar', value, text, type, message, line, exceptionType, output }
```
호스트 함수는 변환할 때도 그 이름을 알려줘야 합니다. (아래 라이브러리 예처럼 `Globals`에 넣지 않으면 "존재하지 않는 함수"로 변환됩니다)
호스트 함수에서 Jyuno 값을 다룰때는 `program.runtime`(`JList`, `JDouble`, `JStruct`, `JyunoError`, `str`, `typeName` 등)을 사용합니다. 호스트 함수가 받은 구조체 값은 복사본이며, `typeName`, `fieldNames`, `field('이름')`으로 읽을수 있습니다.

### 라이브러리
```cs
using JyunoSdk.NativeJs;

var runtime = new Jyuno.Runtime();
runtime.AddFunction("text.upper", args => args[0]?.ToString()?.ToUpperInvariant());
var result = NativeJsCompiler.Compile(File.ReadAllText("hello.jyuno"), new NativeJsOptions {
    Globals = runtime.GetGlobals(),  // 프로그램이 추가한 함수도 재정의할수 없는 이름으로 검사
    Name = "hello.jyuno",
});
if (result.Success) File.WriteAllText("hello.js", result.Code);
else foreach (var error in result.Errors) Console.WriteLine($"{error.Line + 1}: {error.Message}");
```

## 라이브러리로 사용하기
`JyunoSdk.Analysis`는 Jyuno 코드를 실행하지 않고 분석합니다.
```cs
using JyunoSdk.Analysis;

var document = JyunoDocument.Parse(File.ReadAllText("code.jyuno"), new AnalyzerOptions {
    Commands = CommandGroups.Default,
    HostNames = new[] { "text.at" },
});
foreach (var diagnostic in document.Diagnostics)
    Console.WriteLine(diagnostic);
```
토큰화 결과는 Jyuno의 `Parser.Tokenizer`와 항상 같도록 테스트로 검증합니다.

## 개발
```
dotnet test tests/JyunoSdk.Tests
```
NuGet 패키지 대신 로컬에서 고치고 있는 Jyuno 소스로 빌드하려면:
```
dotnet test tests/JyunoSdk.Tests -p:JyunoSource=../Jyuno/Jyuno.csproj
```
Jyuno에 새 내장 명령어가 추가되면 `src/JyunoSdk.Analysis/Builtins.cs`에 설명을 추가해야 합니다. (`BuiltinTests`가 알려줍니다)

## 위키
[위키](https://github.com/jyunrcaea/JyunoSdk/wiki) 문서는 [`docs/wiki`](docs/wiki)에 있습니다. master에 푸시하면 Wiki 워크플로우([`wiki.yml`](.github/workflows/wiki.yml))가 위키에 그대로 올립니다. 위키를 직접 고치면 다음 동기화 때 덮어써지니 이 폴더에서 고쳐 주세요.

## 릴리즈
``v1.2.3`` 형식의 태그를 푸시하면 GitHub Actions가 테스트 후 운영체제별 `jyuno` 실행 파일(Windows/Linux/macOS, x64/arm64)과 NuGet 패키지를 GitHub 릴리즈에 올리고, NuGet에 `JyunoSdk`(dotnet tool), `JyunoSdk.Analysis`, `JyunoSdk.LanguageServer`, `JyunoSdk.NativeJs`를 배포합니다.
```
git tag v0.1.0
git push origin v0.1.0
```
태그 대신 Actions 탭의 Release 워크플로우를 직접 실행(Run workflow)하고 버전을 입력해도 됩니다. 이때는 선택한 브랜치의 커밋에 `v(버전)` 태그가 만들어집니다.
NuGet 배포는 [Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing)을 사용합니다. nuget.org의 Trusted Publishing 정책(저장소 ``jyunrcaea/JyunoSdk``, 워크플로우 ``release.yml``, 환경 ``production``)을 먼저 만들어야 합니다.
