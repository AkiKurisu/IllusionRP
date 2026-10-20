using System.Collections.Generic;
using System.Text;
using Illusion.ShaderTools.Syntax.Common;

namespace Illusion.ShaderTools.Syntax.ShaderLab
{
    using SLToken = Token<TokenKind>;

    public class ShaderLabLexer : BaseLexer<TokenKind>
    {
        protected override ParserStage Stage => ParserStage.ShaderLabLexing;
        protected override TokenKind EndOfFileTokenKind => TokenKind.EndOfFileToken;

        public ShaderLabLexer(string source, string basePath, string fileName, bool throwExceptionOnError)
            : base(source, basePath, fileName, throwExceptionOnError, new SourceLocation(1, 1, 0)) { }

        public static List<SLToken> Lex(string source, string basePath, string fileName, bool throwExceptionOnError, out List<Diagnostic> diagnostics)
        {
            ShaderLabLexer lexer = new ShaderLabLexer(source, basePath, fileName, throwExceptionOnError);

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

                case '2' when LookAhead('D') || LookAhead('d'):
                case '3' when LookAhead('D') || LookAhead('d'):
                    LexDimensionalTextureType();
                    break;

                case char c when char.IsDigit(c) || ((c == '.' || c == '-') && char.IsDigit(LookAhead())):
                    string num = EatNumber(out bool isFloat);
                    TokenKind kind = isFloat ? TokenKind.FloatLiteralToken : TokenKind.IntegerLiteralToken;
                    Add(num, kind);
                    break;

                case '"':
                    Add(EatStringLiteral('"', '"'), TokenKind.StringLiteralToken);
                    break;

                case '[' when IsAlphaNumericOrUnderscore(LookAhead()):
                    Add(EatStringLiteral('[', ']'), TokenKind.BracketedStringLiteralToken);
                    break;

                case ' ':
                case '\t':
                case '\r':
                case '\n':
                    Advance();
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

                case char c:
                    Advance();
                    Error(DiagnosticFlags.SyntaxError, $"Unexpected token '{c}'.");
                    break;
            }
        }

        private string SkipProgramBody(string expectedEnd)
        {
            StringBuilder builder = new StringBuilder();
            while (true)
            {
                // If there is still space for the terminator
                if (!IsAtEnd(expectedEnd.Length))
                {
                    // And we have reached the terminator, stop
                    if (source.Substring(position, expectedEnd.Length) == expectedEnd)
                    {
                        Advance(expectedEnd.Length);
                        break;
                    }

                    // Otherwise advance
                    builder.Append(Advance());
                }
                // No space for terminator, error
                else
                {
                    Error(DiagnosticFlags.SyntaxError, $"Unterminated program block.");
                    break;
                }
            }

            return builder.ToString();
        }

        private void LexDimensionalTextureType()
        {
            StringBuilder builder = new StringBuilder();
            builder.Append(Advance());
            while (char.IsLetter(Peek()))
            {
                builder.Append(Advance());
            }

            switch (builder.ToString().ToLower())
            {
                case "2darray": Add(TokenKind._2DArrayKeyword); break;
                case "3darray": Add(TokenKind._3DArrayKeyword); break;
                case "2d": Add(TokenKind._2DKeyword); break;
                case "3d": Add(TokenKind._3DKeyword); break;
            }
        }

        private void LexIdentifier()
        {
            string identifier = EatIdentifier();
            if (ShaderLabSyntaxFacts.TryParseShaderLabKeyword(identifier, out TokenKind token))
            {
                if (token == TokenKind.CgProgramKeyword)
                {
                    string body = SkipProgramBody("ENDCG");
                    Add(body, TokenKind.CgProgramBlock);
                }
                else if (token == TokenKind.CgIncludeKeyword)
                {
                    string body = SkipProgramBody("ENDCG");
                    Add(body, TokenKind.CgIncludeBlock);
                }
                else if (token == TokenKind.HlslProgramKeyword)
                {
                    string body = SkipProgramBody("ENDHLSL");
                    Add(body, TokenKind.HlslProgramBlock);
                }
                else if (token == TokenKind.HlslIncludeKeyword)
                {
                    string body = SkipProgramBody("ENDHLSL");
                    Add(body, TokenKind.HlslIncludeBlock);
                }
                else if (token == TokenKind.GlslProgramKeyword)
                {
                    string body = SkipProgramBody("ENDGLSL");
                    Add(body, TokenKind.GlslProgramBlock);
                }
                else if (token == TokenKind.GlslIncludeKeyword)
                {
                    string body = SkipProgramBody("ENDGLSL");
                    Add(body, TokenKind.GlslIncludeBlock);
                }
                else
                {
                    Add(token);
                }
            }
            else
            {
                Add(identifier, TokenKind.IdentifierToken);
            }
        }
    }
}
