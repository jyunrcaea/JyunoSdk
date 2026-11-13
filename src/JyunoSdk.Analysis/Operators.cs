namespace JyunoSdk.Analysis;

/// <summary>연산자 설명 (hover, Jyuno 1.6.0부터)</summary>
public static class Operators
{
    //기호 → (이름, 설명). 우선순위는 C#과 같음
    static readonly Dictionary<string , (string Name, string Description)> all = new() {
        ["+"] = ("덧셈 / 양수" , "a + b: add와 같은 계산 (숫자의 합, 문자열 이어붙이기, 배열/리스트에 원소 추가). +a: 숫자 그대로"),
        ["-"] = ("뺄셈 / 음수" , "a - b: sub와 같은 계산 (숫자의 차, 문자열에서 지우기, 배열/리스트의 차집합). -a: 부호 반전"),
        ["*"] = ("곱셈" , "mul과 같은 계산 (숫자의 곱, 문자열 반복)"),
        ["/"] = ("나눗셈" , "div와 같은 계산 (정수끼리는 몫, 문자열은 구분자로 자르기)"),
        ["%"] = ("나머지" , "mod와 같은 계산"),
        ["<<"] = ("왼쪽 시프트" , "정수끼리만 (이동 횟수는 하위 6비트)"),
        [">>"] = ("오른쪽 시프트" , "정수끼리만 (부호를 유지, 이동 횟수는 하위 6비트)"),
        ["<"] = ("작음" , "less와 같은 기준으로 비교"),
        [">"] = ("큼" , "greater와 같은 기준으로 비교"),
        ["<="] = ("작거나 같음" , "less/greater와 같은 기준으로 비교"),
        [">="] = ("크거나 같음" , "less/greater와 같은 기준으로 비교"),
        ["=="] = ("같음" , "equal과 같은 기준으로 비교 (1과 1.0은 같음)"),
        ["!="] = ("다름" , "== 의 반대"),
        ["&"] = ("비트 AND" , "and와 같은 계산 (정수끼리는 비트 연산, 그 외에는 논리 연산이며 양쪽을 모두 계산)"),
        ["^"] = ("비트 XOR" , "정수끼리는 비트 XOR, 그 외에는 참/거짓이 다른지"),
        ["|"] = ("비트 OR" , "or와 같은 계산 (정수끼리는 비트 연산, 그 외에는 논리 연산이며 양쪽을 모두 계산)"),
        ["&&"] = ("논리 AND" , "왼쪽이 거짓이면 오른쪽을 계산하지 않음 (단락 평가), 결과는 bool"),
        ["||"] = ("논리 OR" , "왼쪽이 참이면 오른쪽을 계산하지 않음 (단락 평가), 결과는 bool"),
        ["??"] = ("null 병합" , "왼쪽이 null이 아니면 왼쪽, null이면 오른쪽 (단락 평가)"),
        ["?"] = ("조건" , "조건 ? 참일때 : 거짓일때 (고른 쪽만 계산)"),
        ["!"] = ("논리 부정" , "!a: a가 거짓이면 true (정수도 참/거짓으로 봄, 비트 반전은 ~)"),
        ["~"] = ("비트 반전" , "~a: 정수의 비트 반전"),
    };

    /// <summary>연산자의 hover 설명 (Markdown), 모르는 기호라면 null</summary>
    public static string? Describe(string symbol)
    {
        if (all.TryGetValue(symbol , out var info))
            return $"**{info.Name}** `{symbol}`\n\n{info.Description}\n\n*연산자*{(Jyuno.Language.Grammar.IsOverloadable(symbol) ? " · 구조체에서 `func " + symbol + "`로 다시 정의할수 있음" : "")}";
        //복합 대입 (x += 1)
        if (Jyuno.Language.Grammar.CompoundAssignment(symbol) is string op && all.TryGetValue(op , out var basic))
            return $"**복합 대입** `{symbol}`\n\nx {symbol} y는 x = x {op} y ({basic.Name})\n\n*연산자*";
        return null;
    }
}
