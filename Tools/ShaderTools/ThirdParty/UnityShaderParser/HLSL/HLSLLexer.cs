using System.Collections.Generic;
using System.Text;
using Illusion.ShaderTools.Syntax.Common;

namespace Illusion.ShaderTools.Syntax.HLSL
{
    using HLSLToken = Token<TokenKind>;

    public class HLSLLexer : BaseLexer<TokenKind>
    {
        protected override ParserStage Stage => ParserStage.HLSLLexing;
        protected override TokenKind EndOfFileTokenKind => TokenKind.EndOfFileToken;

        public HLSLLexer(string source, string basePath, string fileName, bool throwExceptionOnError, SourceLocation offset)
            : base(source, basePath, fileName, throwExceptionOnError, offset) { }

        public static List<HLSLToken> Lex(string source, string basePath, string fileName, bool throwExceptionOnError, out List<Diagnostic> diagnostics)
        {
            return Lex(source, basePath, fileName, throwExceptionOnError, new SourceLocation(1, 1, 0), out diagnostics);
        }

        public static List<HLSLToken> Lex(string source, string basePath, string fileName, bool throwExceptionOnError, SourceLocation offset, out List<Diagnostic> diagnostics)
        {
            HLSLLexer lexer = new HLSLLexer(source, basePath, fileName, throwExceptionOnError, offset);

            lexer.Lex();

            diagnostics = lexer.diagnostics;
            return lexer.tokens;
        }

        protected override void ProcessChar(char nextChar)
        {
            switch (nextChar)
            {
                case char c when char.IsLetter(c) || c == '_':
                    LexIdentifier();
                    break;

                case '0' when LookAhead('x'):
                    Advance(1);
                    string hexNum = EatIdentifier().Substring(1);
                    string origHexNum = hexNum;
                    if (hexNum.EndsWith("u") || hexNum.EndsWith("U"))
                        hexNum = hexNum.Substring(0, hexNum.Length - 1);
                    if (!uint.TryParse(hexNum, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out uint hexVal))
                        Error(DiagnosticFlags.SyntaxError, $"Invalid hex literal 0x{hexNum}");
                    Add($"0x{origHexNum}", TokenKind.IntegerLiteralToken);
                    break;

                case char c when char.IsDigit(c) || (c == '.' && char.IsDigit(LookAhead())):
                    string num = EatNumber(out bool isFloat);
                    TokenKind kind = isFloat ? TokenKind.FloatLiteralToken : TokenKind.IntegerLiteralToken;
                    Add(num, kind);
                    break;

                case '\'':
                    Add(EatStringLiteral('\'', '\''), TokenKind.CharacterLiteralToken);
                    break;

                case '"':
                    Add(EatStringLiteral('"', '"'), TokenKind.StringLiteralToken);
                    break;

                case ' ':
                case '\t':
                case '\r':
                    SkipWhitespace();
                    break;
                case '\n':
                case '\\':
                    Advance(); // Only consume 1 (preprocessor might care about the newlines)
                    break;

                case '/' when LookAhead('/'):
                {
                    StringBuilder builder = new StringBuilder();
                    builder.Append(Advance());
                    builder.Append(Advance());
                    while (!Match('\n'))
                    {
                        builder.Append(Advance());
                        if (IsAtEnd())
                            break;
                    }
                    AddTrivia(SyntaxTriviaKind.SingleLineComment, builder.ToString());
                    break;
                }

                case '/' when LookAhead('*'):
                {
                    StringBuilder builder = new StringBuilder();
                    builder.Append(Advance());
                    builder.Append(Advance());
                    while (!(Match('*') && LookAhead('/')))
                    {
                        builder.Append(Advance());
                        if (IsAtEnd())
                        {
                            Error(DiagnosticFlags.SyntaxError, $"Unterminated comment.");
                            break;
                        }
                    }
                    builder.Append(Advance());
                    builder.Append(Advance());
                    AddTrivia(SyntaxTriviaKind.MultiLineComment, builder.ToString());
                    break;
                }

                case '(': Advance(); Add(TokenKind.OpenParenToken); break;
                case ')': Advance(); Add(TokenKind.CloseParenToken); break;
                case '[': Advance(); Add(TokenKind.OpenBracketToken); break;
                case ']': Advance(); Add(TokenKind.CloseBracketToken); break;
                case '{': Advance(); Add(TokenKind.OpenBraceToken); break;
                case '}': Advance(); Add(TokenKind.CloseBraceToken); break;
                case ';': Advance(); Add(TokenKind.SemiToken); break;
                case ',': Advance(); Add(TokenKind.CommaToken); break;
                case '.': Advance(); Add(TokenKind.DotToken); break;
                case '~': Advance(); Add(TokenKind.TildeToken); break;
                case '?': Advance(); Add(TokenKind.QuestionToken); break;

                case '<' when LookAhead('='): Advance(2); Add(TokenKind.LessThanEqualsToken); break;
                case '<' when LookAhead('<') && LookAhead('=', 2): Advance(3); Add(TokenKind.LessThanLessThanEqualsToken); break;
                case '<' when LookAhead('<'): Advance(2); Add(TokenKind.LessThanLessThanToken); break;
                case '<': Advance(); Add(TokenKind.LessThanToken); break;

                case '>' when LookAhead('='): Advance(2); Add(TokenKind.GreaterThanEqualsToken); break;
                case '>' when LookAhead('>') && LookAhead('=', 2): Advance(3); Add(TokenKind.GreaterThanGreaterThanEqualsToken); break;
                case '>' when LookAhead('>'): Advance(2); Add(TokenKind.GreaterThanGreaterThanToken); break;
                case '>': Advance(); Add(TokenKind.GreaterThanToken); break;

                case '+' when LookAhead('+'): Advance(2); Add(TokenKind.PlusPlusToken); break;
                case '+' when LookAhead('='): Advance(2); Add(TokenKind.PlusEqualsToken); break;
                case '+': Advance(); Add(TokenKind.PlusToken); break;

                case '-' when LookAhead('-'): Advance(2); Add(TokenKind.MinusMinusToken); break;
                case '-' when LookAhead('='): Advance(2); Add(TokenKind.MinusEqualsToken); break;
                case '-': Advance(); Add(TokenKind.MinusToken); break;

                case '*' when LookAhead('='): Advance(2); Add(TokenKind.AsteriskEqualsToken); break;
                case '*': Advance(); Add(TokenKind.AsteriskToken); break;

                case '/' when LookAhead('='): Advance(2); Add(TokenKind.SlashEqualsToken); break;
                case '/': Advance(); Add(TokenKind.SlashToken); break;

                case '%' when LookAhead('='): Advance(2); Add(TokenKind.PercentEqualsToken); break;
                case '%': Advance(); Add(TokenKind.PercentToken); break;

                case '&' when LookAhead('&'): Advance(2); Add(TokenKind.AmpersandAmpersandToken); break;
                case '&' when LookAhead('='): Advance(2); Add(TokenKind.AmpersandEqualsToken); break;
                case '&': Advance(); Add(TokenKind.AmpersandToken); break;

                case '|' when LookAhead('|'): Advance(2); Add(TokenKind.BarBarToken); break;
                case '|' when LookAhead('='): Advance(2); Add(TokenKind.BarEqualsToken); break;
                case '|': Advance(); Add(TokenKind.BarToken); break;

                case '^' when LookAhead('='): Advance(2); Add(TokenKind.CaretEqualsToken); break;
                case '^': Advance(); Add(TokenKind.CaretToken); break;

                case ':' when LookAhead(':'): Advance(2); Add(TokenKind.ColonColonToken); break;
                case ':': Advance(); Add(TokenKind.ColonToken); break;

                case '=' when LookAhead('='): Advance(2); Add(TokenKind.EqualsEqualsToken); break;
                case '=': Advance(); Add(TokenKind.EqualsToken); break;

                case '!' when LookAhead('='): Advance(2); Add(TokenKind.ExclamationEqualsToken); break;
                case '!': Advance(); Add(TokenKind.NotToken); break;

                case '#' when LookAhead('#'): Advance(2); Add(TokenKind.HashHashToken); break;

                case '#':
                    LexPreProcessorDirective();
                    break;

                case char c:
                    Advance();
                    Error(DiagnosticFlags.SyntaxError, $"Unexpected token '{c}'.");
                    break;
            }
        }

        private void LexIdentifier()
        {
            string identifier = EatIdentifier();
            if (HLSLSyntaxFacts.TryParseHLSLKeyword(identifier, out TokenKind token))
            {
                Add(token);
            }
            else
            {
                Add(identifier, TokenKind.IdentifierToken);
            }
        }

        private void LexPreProcessorDirective()
        {
            Eat('#');
            SkipWhitespace();
            string keyword = EatIdentifier();
            switch (keyword)
            {
                case "define":
                    Add(TokenKind.DefineDirectiveKeyword);
                    SkipWhitespace();
                    Add(EatIdentifier(), TokenKind.IdentifierToken);
                    if (Match('(')) // No whitespace
                    {
                        // In order to distinguish function like macros and regular macros, one must inspect whitespace
                        Advance();
                        Add(TokenKind.OpenFunctionLikeMacroParenToken);
                    }
                    break;

                case "line": Add(TokenKind.LineDirectiveKeyword); break;
                case "undef": Add(TokenKind.UndefDirectiveKeyword); break;
                case "error": Add(TokenKind.ErrorDirectiveKeyword); break;
                case "pragma": Add(TokenKind.PragmaDirectiveKeyword); break;
                case "include": case "include_with_pragmas": Add(TokenKind.IncludeDirectiveKeyword);
                    SkipWhitespace();
                    // Handle system includes
                    if (Match('<'))
                    {
                        Eat('<');
                        var sb = new StringBuilder();
                        while (!IsAtEnd() && !Match('>'))
                        {
                            sb.Append(Advance());
                        }
                        Eat('>');
                        Add(sb.ToString(), TokenKind.SystemIncludeLiteralToken);
                    }
                    break;

                case "if": Add(TokenKind.IfDirectiveKeyword); break;
                case "ifdef": Add(TokenKind.IfdefDirectiveKeyword); break;
                case "ifndef": Add(TokenKind.IfndefDirectiveKeyword); break;
                case "elif": Add(TokenKind.ElifDirectiveKeyword); break;
                case "else": Add(TokenKind.ElseDirectiveKeyword); break;
                case "endif": Add(TokenKind.EndifDirectiveKeyword); break;

                default:
                    Add(TokenKind.HashToken);
                    Add(keyword, TokenKind.IdentifierToken);
                    return;
            }

            // Go to end of line
            while (!IsAtEnd() && !Match('\n'))
            {
                // Skip multiline macro line breaks
                if (Match('\\'))
                {
                    Advance();
                    SkipWhitespace();
                    if (Match('\n'))
                    {
                        Advance();
                    }
                }

                // Process char
                StartCurrentSpan();
                ProcessChar(Peek());
            }
            Add(TokenKind.EndDirectiveToken);
        }
    }
}
