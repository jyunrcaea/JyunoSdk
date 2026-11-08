using Jyuno.Language;

namespace JyunoSdk.Analysis;

/// <summary>키워드 설명 (hover, 자동 완성)</summary>
public sealed record KeywordInfo(string Name , KeywordType Type , string Syntax , string Description , string? Snippet = null);

public static class Keywords
{
    public static IReadOnlyList<KeywordInfo> All { get; } = new KeywordInfo[] {
        new("if" , KeywordType.If , "if (값)\n\t...\nelse\n\t...\nend" , "(값)이 null, false, 0, 빈 문자열이 아니라면 if와 end(또는 else) 사이를 실행합니다." , "if ${1:조건}\n\t$0\nend"),
        new("else" , KeywordType.Else , "else" , "if의 값이 거짓일 때 else와 end 사이를 실행합니다."),
        new("end" , KeywordType.End , "end" , "if, while, repeat, for 블록을 닫습니다."),
        new("while" , KeywordType.While , "while (값)\n\t...\nend" , "(값)이 참인 동안 while과 end 사이를 반복합니다." , "while ${1:조건}\n\t$0\nend"),
        new("repeat" , KeywordType.Repeat , "repeat (값:정수)\n\t...\nend" , "(값) 횟수만큼 repeat와 end 사이를 반복합니다." , "repeat ${1:횟수}\n\t$0\nend"),
        new("for" , KeywordType.For , "for (변수명) (값:배열|리스트|문자열)\n\t...\nend" , "(값)의 원소를 차례대로 (변수명)에 넣고 반복합니다. (변수명)과 for 안에서 처음 만든 변수는 반복 한번에서만 쓰이는 지역 변수입니다." , "for ${1:x} ${2:목록}\n\t$0\nend"),
        new("break" , KeywordType.Break , "break" , "가장 안쪽 반복문(while, repeat, for)을 빠져나옵니다."),
        new("goto" , KeywordType.Goto , "goto (값:정수|레이블)" , "(값)번째 줄(첫 줄은 0) 또는 레이블로 이동합니다. 함수 안팎으로는 이동할수 없습니다."),
        new("func" , KeywordType.Func , "func (함수명)\n\tparam (파라미터)\n\t...\nendfunc" , "함수를 만듭니다. 본문은 함수를 호출할 때 실행됩니다." , "func ${1:이름}\n\tparam ${2:인자}\n\t$0\nendfunc"),
        new("param" , KeywordType.Param , "param (파라미터명)" , "함수 본문에서 쓴 순서대로 첫번째, 두번째, ... 인자를 받습니다. 인자가 부족하면 null입니다."),
        new("endfunc" , KeywordType.EndFunc , "endfunc" , "func 블록을 닫습니다. return 없이 여기에 도달하면 null을 반환합니다."),
        new("return" , KeywordType.Return , "return (값)" , "값을 반환합니다. 함수를 호출한 결과를 반환하려면 return (함수 인자...)처럼 괄호로 감싸세요."),
        new("local" , KeywordType.Local , "local (변수명) = (값)" , "바깥에 같은 이름의 변수가 있더라도 지금 함수(또는 for 반복)에서만 쓰는 지역 변수를 만듭니다."),
    };

    public static KeywordInfo? Get(KeywordType type) => All.FirstOrDefault(k => k.Type == type);

    public static string ToMarkdown(this KeywordInfo keyword) => $"```jyuno\n{keyword.Syntax}\n```\n{keyword.Description}\n\n*키워드*";
}
