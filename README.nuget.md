# JyunoSdk

The SDK for the [Jyuno](https://github.com/jyunrcaea/Jyuno) language. (Formerly JyunoPrompt)  
[GitHub](https://github.com/jyunrcaea/JyunoSdk) · [Wiki](https://github.com/jyunrcaea/JyunoSdk/wiki) · [Website & Playground](https://jyunrcaea.github.io/JyunoWeb)

A single `jyuno` command provides:

- **Interactive prompt (REPL)**: run code one line at a time; `if`/`while`/`repeat`/`for`/`func` blocks can be entered over multiple lines.
- **Run/check/format scripts**: `jyuno run`, `jyuno check`, `jyuno format`
- **Language server (LSP)**: `jyuno lsp` provides diagnostics, completion, go to definition and more in VS Code and other editors that support LSP.
- **Native JS**: `jyuno <file> --native-js` translates Jyuno code into a JavaScript file that includes the runtime. It runs in a browser or Node.js without .NET.

Each feature is also published as a NuGet library (`JyunoSdk.Analysis`, `JyunoSdk.LanguageServer`, `JyunoSdk.NativeJs`), so other programs can add the package and use it.

## Packages

| Package | Description |
| --- | --- |
| `JyunoSdk` | The `jyuno` command as a dotnet tool (REPL, run, check, format, lsp, --native-js) |
| `JyunoSdk.Analysis` | Library that analyzes Jyuno code without running it (tokens with positions, block structure, variable scopes, name resolution, diagnostics, formatting, built-in command docs) |
| `JyunoSdk.LanguageServer` | Language Server Protocol 3.17 implementation (JSON-RPC over standard I/O, no external dependencies) |
| `JyunoSdk.NativeJs` | Translates Jyuno code to JavaScript (Native JS). Analysis uses `Jyuno.Analysis` from the Jyuno library |

The Jyuno interpreter comes from the [`Jyuno`](https://www.nuget.org/packages/Jyuno) NuGet package.

## Installation

### Executable (recommended)
Download the `jyuno` file for your operating system from [Releases](https://github.com/jyunrcaea/JyunoSdk/releases). It runs without installing .NET.

### dotnet tool
```
dotnet tool install -g JyunoSdk
```

### Build from source
Clone the [repository](https://github.com/jyunrcaea/JyunoSdk) and run (requires the .NET 8 SDK):
```
dotnet run --project src/JyunoSdk.Cli
```

## Usage

```
jyuno                          Start the interactive prompt (REPL).
jyuno <file> [options]         Run a script file.
jyuno run <file> [options]     Run a script file.
jyuno <file> --native-js [output file]
                               Translate to JavaScript (Native JS) without running.
                               (If the output file is omitted, the script name with its extension changed to .js)
jyuno check <file...> [options]
                               Check a script for syntax/name problems without running it.
jyuno format <file...> [--write] [--spaces <n>]
                               Fix indentation to match the block structure.
jyuno lsp                      Run the Jyuno language server (LSP) over standard I/O.
jyuno --version                Print the version.

Options:
  --file, -file                Allow commands that work with files/directories (file.*, directory.*).
  --commands <group,...>       Command groups to use (console, math, file, default, all)
  --host <name,...>            (check) Names of functions/variables provided separately by the program
  --native-js [output file]    Translate to Native JS and save. (Run it with node <output file>.js)
```

### Interactive prompt
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
- Input that starts with `if`, `while`, `repeat`, `for` or `func` is collected until the block is closed with `end`/`endfunc`, then run all at once. Enter `.cancel` while entering a block to cancel it.
- Results that are not `null` are printed. Arrays/lists are shown like `[1, 'a', 2.0]`.
- `goto` can't be used in the prompt. (Save the code to a file and run it with `execute "file"`.)

Commands available only in the prompt:
- `exit`: leave the prompt.
- `help` / `help (name)`: show help, or the description of a function/keyword. (e.g. `help add`, `help for`)
- `execute (file path)`: run a file.
- `history`: return the lines entered so far.
- `eval (string)`: run a string as one line of code.
- `.input`, `.return`, `.error` (variables): change the input prompt (`>>> `), result format (`{0}`) and error format (`오류: {0}`).
- `.remove`, `.substitute` (variables): whether remove/assign operations are allowed
- All commands, including file commands (`file.*`, `directory.*`), can be used in the prompt.

### Check
```
$ jyuno check palindrome.jyuno
palindrome.jyuno:6:21: warning JY2001: 'text.at'은/는 정의되지 않은 함수/변수입니다.
오류 0개, 경고 1개
```
(Messages are currently printed in Korean. The example says `'text.at'` is an undefined function/variable; 0 errors, 1 warning.)  
If the program that runs the script adds its own functions, pass them with `--host text.at,other`. The exit code is 1 when there are errors.

## Language server

`jyuno lsp` speaks the [Language Server Protocol](https://microsoft.github.io/language-server-protocol/) over standard I/O. (Trailing arguments such as `--stdio` are ignored.)

### Features
- **Diagnostics**: syntax errors (unclosed strings/parentheses, unmatched end/endfunc, else/break/param placement, func/param/for/local format), undefined names, functions passed as arguments without parentheses, ignored operators/tokens, `goto` jumping into or out of a function, etc.
- **Completion**: keywords, block snippets (`if … end`, `func … endfunc`, etc.), variables/functions/parameters visible at the current position, built-in commands (with descriptions)
- **Hover**: built-in command descriptions, user function signatures, variable kinds (local/parameter/label/constant, etc.)
- **Signature help**: arguments of built-in and user functions
- **Go to definition, find references, highlight same name, rename**: follows Jyuno's scope rules (function locals, `local`, `for` loop variables, assigning to outer variables)
- **Document symbols** (including parameters/locals inside functions), **folding** (blocks), **formatting** (fix indentation)
- **Semantic tokens** (semantic highlighting): `keyword`, `function`, `variable`, `parameter`, `string`, `number`, `operator`, `label` / modifiers `declaration`, `readonly`, `defaultLibrary`
- **Quick fixes**: function passed without parentheses `add f 1` → `add (f) 1`, `return add 1 2` → `return (add 1 2)`

### Settings
Configure with the `jyuno` section of `initializationOptions` or `workspace/didChangeConfiguration`.
```json
{
  "commands": ["console", "math"],
  "hostNames": ["text.at"],
  "undefinedNames": true
}
```
- `commands`: command groups provided by the program that runs the script. `"default"` (console, math), `"all"`, or an array such as `["console", "math", "file"]` (default `"default"`)
- `hostNames`: names the program adds with `Runtime.AddFunction`/`AddVariable`
- `undefinedNames`: whether to warn about undefined names (default `true`)

### Using it in an editor
A VS Code extension can run `jyuno lsp` with [vscode-languageclient](https://www.npmjs.com/package/vscode-languageclient).
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

### Diagnostic codes
| Code | Severity | Description |
| --- | --- | --- |
| JY1001 | Error | Tokenization error (unclosed string, integer out of long range) |
| JY1002 | Error | Block structure error (end/endfunc pairs, else, break, param placement) |
| JY1003 | Error | Unclosed parenthesis |
| JY1004 | Error | Keyword format error (func/param/for/local) |
| JY1005 | Error | Keyword used somewhere other than the start of a line |
| JY1006 | Error | goto jumps into or out of a function |
| JY2001 | Warning | Undefined function/variable |
| JY2002 | Warning | Function passed as an argument without parentheses (its value can't be read) |
| JY2003 | Warning | Token that is ignored and never run |
| JY2004 | Warning/Error | Missing or more than one value for if/while/repeat/goto/return |
| JY2005 | Warning | Command group not provided by the configured program (e.g. file) |

## Native JS
> The introduction, usage, restrictions, internals and research results (performance and compatibility verification) are covered in detail in the [wiki](https://github.com/jyunrcaea/JyunoSdk/wiki/Native-JS) (in Korean).

Translates Jyuno code to JavaScript. The generated file contains the Jyuno runtime (values, operations, console/math commands), so it runs in Node.js or a browser with no other files.
Differential tests verify that it produces the same results as the interpreter (output, return value, error messages and lines), and it is much faster for code with many loops or function calls.
```
jyuno hello.jyuno --native-js          # creates hello.js
jyuno hello.jyuno --native-js out.js   # creates out.js
node hello.js                          # runs with standard I/O
```

### Restrictions
Because variables and functions are translated into fixed slots and called directly, the following code is not translated and is reported as an error.

| Restriction | Example |
| --- | --- |
| Removing variables/functions is not allowed | `x =` |
| Built-in functions/constants (`add`, `mul`, `console.writeline`, `true`, `math.pi`, etc.) and functions provided by the program can't be redefined/assigned to | `add = 1`, `func mul`, `5 = true`, `for add 'abc'` |
| goto can't jump into a block (if, while, repeat, for) | `goto inner` (inner is inside an if) |
| Block structure errors (an unmatched end, etc.) are reported before running | |

Assigning a value to a variable provided by the program (such as `console.title`) is allowed. If a computed goto target is inside a block, a `Native JS 제약` (Native JS restriction) error is raised at run time.
The `file.*` and `directory.*` commands are not supported.

### Using the generated code
The generated file defines the global variable `JyunoProgram` (or `module.exports` under CommonJS).
```js
const program = require('./hello.js');
const result = program.run({
  input: 'input\n',                        // string read by console.readline/console.read
  write: (text) => process.stdout.write(text), // if omitted, output is collected into result.output
  host: { 'text.upper': (args) => String(args[0]).toUpperCase() }, // functions provided by the program
});
// result: { status: 'ok' | 'error' | 'grammar', value, text, type, message, line, exceptionType, output }
```
Host functions must also be named at translation time. (If they are not put in `Globals` as in the library example below, they are translated as "nonexistent functions".)
To work with Jyuno values in host functions, use `program.runtime` (`JList`, `JDouble`, `JyunoError`, `str`, `typeName`, etc.).

### Library
```cs
using JyunoSdk.NativeJs;

var runtime = new Jyuno.Runtime();
runtime.AddFunction("text.upper", args => args[0]?.ToString()?.ToUpperInvariant());
var result = NativeJsCompiler.Compile(File.ReadAllText("hello.jyuno"), new NativeJsOptions {
    Globals = runtime.GetGlobals(),  // also treat functions added by the program as names that can't be redefined
    Name = "hello.jyuno",
});
if (result.Success) File.WriteAllText("hello.js", result.Code);
else foreach (var error in result.Errors) Console.WriteLine($"{error.Line + 1}: {error.Message}");
```

## Using it as a library
`JyunoSdk.Analysis` analyzes Jyuno code without running it.
```cs
using JyunoSdk.Analysis;

var document = JyunoDocument.Parse(File.ReadAllText("code.jyuno"), new AnalyzerOptions {
    Commands = CommandGroups.Default,
    HostNames = new[] { "text.at" },
});
foreach (var diagnostic in document.Diagnostics)
    Console.WriteLine(diagnostic);
```
Tests verify that tokenization always matches Jyuno's `Parser.Tokenizer`.

## License
MIT
