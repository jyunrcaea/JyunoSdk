# JyunoSdk 위키

**JyunoSdk**는 [Jyuno](https://github.com/jyunrcaea/Jyuno) 언어를 위한 개발 도구 모음입니다. `jyuno` 명령어 하나로 대화형 프롬프트, 스크립트 실행·검사·정리, 언어 서버(LSP), 그리고 **Native JS**를 제공합니다.

> ⚡ **새 기능: Native JS** — Jyuno 코드를 JavaScript로 변환해서 브라우저와 Node.js에서 .NET 없이 실행합니다. 연구 단계에서 웹의 WASM 인터프리터보다 **기하평균 약 190배** 빨랐고, 런타임은 **압축 후 약 16 KB**입니다.
> 👉 [[Native JS 소개|Native-JS]]

## 무엇이 들어 있나요?

| 기능 | 명령어 | 라이브러리 (NuGet) |
| --- | --- | --- |
| 대화형 프롬프트 (REPL) | `jyuno` | — |
| 스크립트 실행 | `jyuno <파일>`, `jyuno run <파일>` | — |
| 실행하지 않고 검사 | `jyuno check <파일...>` | `JyunoSdk.Analysis` |
| 들여쓰기 정리 | `jyuno format <파일...> [--write]` | `JyunoSdk.Analysis` |
| 언어 서버 (LSP) | `jyuno lsp` | `JyunoSdk.LanguageServer` |
| **JavaScript로 변환 (Native JS)** | `jyuno <파일> --native-js [출력 파일]` | `JyunoSdk.NativeJs` |

## 설치

- **실행 파일**: [Releases](https://github.com/jyunrcaea/JyunoSdk/releases)에서 운영체제에 맞는 `jyuno`를 받으세요. .NET 설치가 필요 없습니다.
- **dotnet tool**: `dotnet tool install -g JyunoSdk`
- **라이브러리**: `dotnet add package JyunoSdk.NativeJs` (또는 `JyunoSdk.Analysis`, `JyunoSdk.LanguageServer`)

```
$ jyuno --version
JyunoSdk 0.4.1 (Jyuno 1.5.2)
```

## Native JS 문서

| 페이지 | 내용 |
| --- | --- |
| [[Native JS 소개|Native-JS]] | Native JS가 무엇이고 왜 만들었는지, 3분 만에 써보기 |
| [[사용하기|Native-JS-Usage]] | 명령어, 만들어진 JS를 Node.js/브라우저에서 쓰는 법, 호스트 함수, C# 라이브러리 |
| [[제약과 차이점|Native-JS-Restrictions]] | 변환할수 없는 코드 세 가지와 인터프리터와의 작은 차이 |
| [[동작 원리|Native-JS-Internals]] | 값 표현, 변수 영역, goto, 오류 줄 번호를 JS로 옮기는 방법 |
| [[연구 결과|Native-JS-Research]] | 성능 측정, 2만 개 무작위 프로그램 차등 테스트, 다른 방안과의 비교 |

## 더 보기

- [JyunoSdk README](https://github.com/jyunrcaea/JyunoSdk#readme) — 명령어와 옵션, LSP 설정, 진단 코드 전체 목록
- [Jyuno](https://github.com/jyunrcaea/Jyuno) — 언어와 인터프리터 (NuGet `Jyuno`)
- [JyunoWeb](https://jyunrcaea.github.io/JyunoWeb/) — 브라우저 플레이그라운드와 문제 풀기
