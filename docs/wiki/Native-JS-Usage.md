# Native JS 사용하기

이 페이지는 JyunoSdk **0.5.0** (Jyuno **1.6.1**) 기준입니다. Native JS가 무엇인지는 [[소개|Native-JS]]를 먼저 보세요.

## 1. 명령어로 변환하기

```
jyuno <파일> --native-js [출력 파일]
```

| 예 | 결과 |
| --- | --- |
| `jyuno hello.jyuno --native-js` | `hello.js` 생성 |
| `jyuno hello.jyuno --native-js out.js` | `out.js` 생성 |
| `node hello.js` | 표준 입출력으로 실행 (`console.readline`은 표준 입력에서 읽음) |

- 변환할 때 코드를 **실행하지 않습니다.** 블록 구조 오류와 [[Native JS 제약|Native-JS-Restrictions]] 위반은 이때 줄 번호와 함께 알려주고, 종료 코드 1을 반환합니다.

```
$ jyuno bad.jyuno --native-js
bad.jyuno:2: 오류: Native JS 제약: 변수/함수 삭제('x =')를 쓸수 없습니다.
bad.jyuno:3: 오류: Native JS 제약: 내장 함수/상수 'add'을/를 대입할수 없습니다.
bad.jyuno:4: 오류: Native JS 제약: 내장 함수/상수 'mul'을/를 함수로 정의할수 없습니다.
bad.jyuno:10: 오류: Native JS 제약: goto로 블록(if, while, repeat, for) 안쪽으로 들어갈수 없습니다.
```

- `node hello.js`로 실행했을 때 문법 오류나 실행 오류가 나면 `hello.jyuno:3: 오류: ...` 형식으로 표준 오류에 쓰고 종료 코드 1로 끝납니다.
- `--commands`로 명령어 묶음을 고를수 있지만, `file.*`, `directory.*` 명령어는 Native JS에서 지원하지 않습니다. (경고를 보여줍니다)

## 2. 만들어진 파일 구조

만들어진 파일은 한 개의 독립된 스크립트입니다.

```js
// hello.jyuno → Jyuno Native JS (JyunoSdk 0.5.0, Jyuno 1.6.1)
// Node.js: node <이 파일>.js  ·  브라우저/모듈: JyunoProgram.run({ input, write, host })
var JyunoProgram = (function () {
  // ① Jyuno 런타임 (값 표현, 연산, 내장 명령어, 오류 메시지)
  // ② 변환된 프로그램
  // ③ 줄 번호 대응표
})();
if (typeof module === 'object' && module && module.exports) module.exports = JyunoProgram;
if (typeof require === 'function' && typeof module === 'object' && require.main === module) JyunoProgram.main();
```

- **전역 변수** `JyunoProgram`을 만들고, CommonJS 환경이면 `module.exports`로도 내보냅니다.
- `node hello.js`처럼 **직접 실행**하면 `main()`이 표준 입출력으로 실행합니다. `require`로 불러오면 실행하지 않습니다.
- 생성 코드는 `eval`이나 `new Function`을 쓰지 않는 평범한 스크립트입니다.

## 3. JavaScript에서 실행하기

### `program.run(options)`

```js
const program = require('./hello.js');

const result = program.run({
  input: 'jyuno\n',                              // console.readline / console.read가 읽을 문자열
  write: (text) => process.stdout.write(text),    // 생략하면 result.output으로 모아서 돌려줌
});
```

| 옵션 | 설명 |
| --- | --- |
| `input` | `console.readline`/`console.read`가 읽을 문자열 |
| `readLine()`, `read()` | `input` 대신 직접 입력을 주는 함수. 입력이 끝나면 `readLine`은 `null`, `read`는 `-1` |
| `write(text)` | 출력 함수. 생략하면 출력을 모아서 `result.output`에 담음 |
| `clear()`, `title(text)` | `console.clear`, `console.title`을 처리할 함수 (생략 가능) |
| `host` | 프로그램이 제공하는 함수/변수 ([아래](#4-호스트-함수와-변수)) |

### 결과

```js
{ status: 'ok',    value: 5, text: '5', type: 'int', output: '안녕, JYUNO!\n' }
{ status: 'error', message: "'text.upper'은/는 존재하지 않는 함수/변수/명령어 입니다.", line: 1, exceptionType: null, output: '' }
```

| 필드 | 설명 |
| --- | --- |
| `status` | `'ok'`(정상 종료), `'error'`(실행 오류), `'grammar'`(문법 오류) |
| `value`, `text`, `type` | `return`한 값, 그 값을 Jyuno 방식으로 문자열로 바꾼 것(`[1, 'a', 2.0]` 등), 타입 이름(`int`, `double`, `string` …) |
| `message` | 오류 메시지 (C# 인터프리터와 같은 문장) |
| `line` | 오류가 난 줄 (**0부터**, 알수 없으면 `-1`) |
| `exceptionType` | .NET이었다면 날 예외 타입 이름 (`JyunoException`이면 `null`) |
| `output` | `write`를 주지 않았을 때 모은 출력 |

### 브라우저에서

```html
<script src="hello.js"></script>
<script>
  const result = JyunoProgram.run({
    input: '',
    write: (text) => document.querySelector('#out').textContent += text,
  });
</script>
```

> 💡 **웹 워커 안에서 실행하세요.** Jyuno 코드에 무한 반복이 있으면 그 스레드가 멈춥니다. 워커에서 실행하면 화면은 그대로이고, 시간이 넘으면 `worker.terminate()`로 멈출수 있습니다. [JyunoWeb](https://github.com/jyunrcaea/JyunoWeb/blob/main/wwwroot/js/jyuno-worker.js)이 이렇게 합니다.

## 4. 호스트 함수와 변수

C# 프로그램이 `runtime.AddFunction`으로 넣는 함수는 JS로 다시 만들어야 합니다. 이때 두 단계가 필요합니다.

1. **변환할 때** 그 이름이 프로그램이 제공하는 이름이라는 것을 알려줍니다. (아래 [C# 라이브러리](#5-c-라이브러리로-변환하기)의 `Globals`)
   변환기는 모르는 이름을 "존재하지 않는 함수"로 변환하므로, 이 단계를 빠뜨리면 실행할 때 `host`에 넣어도 찾지 못합니다.
2. **실행할 때** `host` 옵션으로 실제 구현을 넘깁니다.

```js
const result = program.run({
  input: 'jyuno\n',
  host: {
    'text.upper': (args) => String(args[0]).toUpperCase(),          // 함수
    'game.score': { kind: 'var', get: () => score, set: (v) => { score = v; } }, // 변수
    'game.version': { kind: 'const', value: '1.0' },                 // 상수
  },
});
```

- 함수는 인자 **배열**을 받아 값을 돌려줍니다. 돌려줄 값이 없으면 `null`.
- Jyuno 값을 다룰 때는 `program.runtime`을 사용합니다.

| `program.runtime` | 용도 |
| --- | --- |
| `JyunoError` | `throw new JyunoError('메시지')` → Jyuno 실행 오류 |
| `JDouble` | 정수 값을 가진 실수(`3.0`)를 정수 `3`과 구별하는 상자 |
| `JList`, `newArray`, `newList` | Jyuno 배열/리스트 |
| `str`, `describe`, `typeName` | 값을 문자열로, 출력용 표현으로, 타입 이름으로 |
| `tryInt`, `tryReal`, `truthy` | 정수/실수로 읽기, 참거짓 판정 |
| `JStruct` | 구조체 값 (`value instanceof JStruct`). `typeName`(구조체 이름), `fieldNames`(필드 이름, 만든 순서), `field('이름')`으로 읽음. 호스트 함수가 받은 구조체 값은 복사본 |

값 표현은 [[동작 원리|Native-JS-Internals#값-표현]]에 정리되어 있습니다.

## 5. C# 라이브러리로 변환하기

```
dotnet add package JyunoSdk.NativeJs
```

```cs
using JyunoSdk.NativeJs;

var runtime = new Jyuno.Runtime();
runtime.AddFunction("text.upper", args => args[0]?.ToString()?.ToUpperInvariant());

var result = NativeJsCompiler.Compile(File.ReadAllText("hello.jyuno"), new NativeJsOptions {
    Globals = runtime.GetGlobals(),   // 프로그램이 추가한 이름도 알려줌 (재정의 검사에도 사용)
    Name = "hello.jyuno",             // 오류 메시지에 쓸 이름
});

if (result.Success)
    File.WriteAllText("hello.js", result.Code);
else
    foreach (var error in result.Errors)
        Console.WriteLine($"{error.Line + 1}: {error.Message}");
```

| API | 설명 |
| --- | --- |
| `NativeJsCompiler.Compile(string 또는 IReadOnlyList<string>, NativeJsOptions?)` | 변환. 코드를 실행하지 않음 |
| `NativeJsOptions.Globals` | 전역 이름 (기본: 콘솔·수학 명령어를 포함한 `Runtime`의 `GetGlobals()`) |
| `NativeJsOptions.BlockSubstitute` | 실행할 때 변수 대입을 막음 (`Interpreter.BlockSubstitute`와 같음) |
| `NativeJsOptions.Name` | 스크립트 이름 (기본 `script.jyuno`) |
| `NativeJsResult.Success` / `Code` / `Errors` | 성공 여부, 런타임을 포함한 JS 코드, 오류 목록(`GrammarError`, 줄은 0부터) |
| `NativeJsCompiler.RuntimeSource` | 생성 코드에 들어가는 JS 런타임 원본 |

변환에 쓰는 분석(구문 트리, 변수 영역, goto 대상, 제약 검사)은 Jyuno 라이브러리의 `Jyuno.Analysis`(`ScriptAnalysis.Analyze`, `CheckNativeJs`)에 있습니다. 다른 언어로의 변환기를 만들 때도 같은 분석을 쓸수 있습니다.

## 6. 인터프리터와 Native JS, 무엇을 쓸까요?

| 상황 | 추천 |
| --- | --- |
| C# 프로그램 안에서 플러그인 실행 | **인터프리터** (`Jyuno` 패키지) — 호스트 함수를 C#으로 그대로 씀 |
| 웹 페이지·웹 워커에서 실행 | **Native JS** — .NET 런타임 없이, 훨씬 빠르게 |
| Node.js 도구, 서버리스 함수 | **Native JS** |
| 삭제(`x =`), 내장 함수 재정의를 쓰는 코드 | **인터프리터** |
| 기준(정답) 동작이 필요할 때 | **인터프리터** — Native JS는 인터프리터와 같은 결과를 내도록 맞춘 구현 |
