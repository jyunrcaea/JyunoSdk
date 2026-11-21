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
        new("param" , KeywordType.Param , "param (파라미터명)" , "함수(또는 구조체 생성자) 본문에서 쓴 순서대로 첫번째, 두번째, ... 인자를 받습니다. 인자가 부족하면 null입니다."),
        new("endfunc" , KeywordType.EndFunc , "endfunc" , "func 블록을 닫습니다. return 없이 여기에 도달하면 null을 반환합니다."),
        new("return" , KeywordType.Return , "return (식)" , "식의 값을 반환합니다. 키워드 다음은 연산자 식이므로 return f x처럼 함수를 호출한 결과나 return a + b를 그대로 반환할수 있습니다."),
        new("struct" , KeywordType.Struct , "struct (구조체명)\n\tparam (필드)\n\tfunc (구조체 함수)\n\t\t...\n\tendfunc\nendstruct" , "구조체를 만듭니다. 본문은 생성자이며, 구조체를 호출할 때 실행됩니다. 본문에서 만든 변수(param 포함)는 필드, 함수는 구조체 함수가 됩니다. 구조체 값을 변수에 넣으면 (변수명).(필드)와 (변수명).(구조체 함수) 이름이 선언되고, 대입/인자/반환할 때 깊은 복사됩니다. 본문에서 func +처럼 연산자 이름의 함수를 만들면 그 연산자를 다시 정의합니다." , "struct ${1:이름}\n\tparam ${2:필드}\n\t$0\nendstruct"),
        new("endstruct" , KeywordType.EndStruct , "endstruct" , "struct 블록(생성자)을 닫습니다."),
        new("local" , KeywordType.Local , "local (변수명) = (값)" , "바깥에 같은 이름의 변수가 있더라도 지금 함수(또는 for 반복)에서만 쓰는 지역 변수를 만듭니다."),
        new("ttl" , KeywordType.Ttl , "ttl (값:초)\n\t...\nendttl" , "ttl 줄을 실행한 때부터 (값)초 이상이 지나면, 실행중이던 줄이 끝난 뒤 endttl 다음 줄로 빠져나갑니다. 오류가 아니며, 블록 안에서 호출한 함수도 시간 제한을 받습니다. 새 영역을 만들지 않습니다. (Jyuno 1.7.0부터)" , "ttl ${1:초}\n\t$0\nendttl"),
        new("endttl" , KeywordType.EndTtl , "endttl" , "ttl 블록을 닫습니다."),
        new("try" , KeywordType.Try , "try\n\t...\ncatch (변수명)\n\t...\ntried" , "try와 catch 사이에서 오류가 나거나 throw로 값을 던지면, 그 값(내부 오류라면 메시지 문자열)을 (변수명)에 넣고 catch와 tried 사이를 실행합니다. 메모리 한도, 호출 깊이, 취소, ttl의 시간 제한은 잡을수 없습니다. 새 영역을 만들지 않습니다. (Jyuno 1.7.0부터)" , "try\n\t$0\ncatch ${1:e}\n\t\ntried"),
        new("catch" , KeywordType.Catch , "catch (변수명)" , "try 본문에서 난 오류나 던진 값을 (변수명)에 넣고 tried까지 실행합니다. (변수명)은 생략할수 있습니다."),
        new("tried" , KeywordType.Tried , "tried" , "try 블록을 닫습니다."),
        new("throw" , KeywordType.Throw , "throw (값)" , "(값)을 던집니다. 아무 값이나 던질수 있으며, 감싸는 try가 없다면 실행이 오류로 끝납니다. catch와 tried 사이에서 값 없이 throw를 쓰면 처리중인 오류를 그대로 다시 던집니다. (Jyuno 1.7.0부터)"),
    };

    public static KeywordInfo? Get(KeywordType type) => All.FirstOrDefault(k => k.Type == type);

    public static string ToMarkdown(this KeywordInfo keyword) => $"```jyuno\n{keyword.Syntax}\n```\n{keyword.Description}\n\n*키워드*";
}
