namespace JyunoSdk.Tests;

static class Script
{
    //테스트 코드 안의 여러 줄 문자열 → 줄 배열 (첫 빈 줄 제거)
    public static string[] Lines(string text) => JyunoSdk.Analysis.Lexer.SplitLines(text.TrimStart('\r' , '\n'));
}
