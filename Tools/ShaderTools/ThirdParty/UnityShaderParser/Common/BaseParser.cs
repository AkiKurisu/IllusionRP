using System;
using System.Collections.Generic;
using System.Linq;

namespace Illusion.ShaderTools.Syntax.Common
{
    public abstract class BaseParser<T>
        where T : struct, Enum
    {
        // Require token kinds
        protected abstract T StringLiteralTokenKind { get; }
        protected abstract T IntegerLiteralTokenKind { get; }
        protected abstract T FloatLiteralTokenKind { get; }
        protected abstract T IdentifierTokenKind { get; }
        protected abstract T InvalidTokenKind { get; }
        protected abstract ParserStage Stage { get; }

        protected Token<T> InvalidToken => new Token<T>(InvalidTokenKind, null, anchorSpan, position);

        protected List<Token<T>> tokens = new List<Token<T>>();
        protected int position = 0;
        protected SourceSpan anchorSpan = default;
        protected List<Diagnostic> diagnostics = new List<Diagnostic>();
        protected bool isRecovering = false;

        protected bool throwExceptionOnError = false;
        protected DiagnosticFlags diagnosticFilter = DiagnosticFlags.All;
        public List<Diagnostic> Diagnostics => diagnostics;

        public BaseParser(List<Token<T>> tokens, bool throwExceptionOnError, DiagnosticFlags diagnosticFilter)
        {
            // Need to copy since the parser might want to modify tokens in place
            this.tokens = new List<Token<T>>(tokens);
            this.throwExceptionOnError = throwExceptionOnError;
            this.diagnosticFilter = diagnosticFilter;
        }

        protected Stack<(int position, bool isRecovering, SourceSpan span, int diagnosticCount)> snapshots = new Stack<(int position, bool isRecovering, SourceSpan span, int diagnosticCount)>();

        protected void SnapshotState()
        {
            snapshots.Push((position, isRecovering, anchorSpan, diagnostics.Count));
        }

        protected void RestoreState()
        {
            var snapshot = snapshots.Pop();
            position = snapshot.position;
            isRecovering = snapshot.isRecovering;
            anchorSpan = snapshot.span;
            diagnostics.RemoveRange(snapshot.diagnosticCount, diagnostics.Count - snapshot.diagnosticCount);
        }

        protected void DropState()
        {
            snapshots.Pop();
        }

        protected bool Speculate(Func<bool> parser)
        {
            SnapshotState();

            try
            {
                // Try the parser
                bool result = parser();

                // If we encountered any errors, report false
                if (diagnostics.Count > snapshots.Peek().diagnosticCount)
                {
                    return false;
                }

                // Otherwise report whatever the parser got
                return result;
            }
            finally
            {
                RestoreState();   
            }
        }

        protected bool TryParse<P>(Func<P> parser, out P parsed)
        {
            SnapshotState();

            // Try the parser
            parsed = parser();

            // If we encountered any errors, report false
            if (diagnostics.Count > snapshots.Peek().diagnosticCount)
            {
                RestoreState();
                parsed = default;
                return false;
            }

            // Otherwise return whatever the parser got
            DropState();
            return true;
        }

        protected Token<T> LookAhead(int offset = 1)
        {
            if (IsAtEnd(offset))
                return InvalidToken;
            // If we are currently recovering from an error, return an error token.
            else if (isRecovering)
                return InvalidToken;
            else
                return tokens[position + offset];
        }
        protected Token<T> Peek() => LookAhead(0);
        protected bool Match(Func<Token<T>, bool> predicate) => predicate(Peek());
        protected bool Match(Func<T, bool> predicate) => predicate(Peek().Kind);
        protected bool Match(T kind) => EqualityComparer<T>.Default.Equals(Peek().Kind, kind);
        protected bool Match(params T[] alternatives)
        {
            T kind = Peek().Kind;
            foreach (T alternative in alternatives)
                if (EqualityComparer<T>.Default.Equals(kind, alternative)) return true;
            return false;
        }
        protected bool IsAtEnd(int offset = 0) => position + offset >= tokens.Count;
        protected bool LoopShouldContinue() => !IsAtEnd() && !isRecovering;
        protected Token<T> Eat(Func<T, bool> predicate)
        {
            if (!Match(predicate))
                Error(DiagnosticFlags.SyntaxError, $"Unexpected token '{Peek()}'.");
            return Advance();
        }
        protected Token<T> Eat(T kind)
        {
            if (!Match(kind))
                Error(DiagnosticFlags.SyntaxError, $"Expected token type '{kind}', got '{Peek().Kind}'.");
            return Advance();
        }
        protected Token<T> Eat(params T[] alternatives)
        {
            if (!Match(alternatives))
            {
                string allowed = string.Join(", ", alternatives);
                Error(DiagnosticFlags.SyntaxError, $"Unexpected token '{Peek()}', expected one of the following token types: {allowed}.");
            }
            return Advance();
        }
        protected Token<T> Advance(int amount = 1)
        {
            if (IsAtEnd(amount - 1))
                return InvalidToken; 
            // If we are currently recovering from an error, don't keep eating tokens, and instead return an error token.
            else if (isRecovering)
                return InvalidToken;
            Token<T> result = tokens[position];
            position += amount;
            anchorSpan = Peek().Span;
            return result;
        }
        protected Token<T> Previous() => LookAhead(-1);

        protected void Error(DiagnosticFlags kind, string msg)
        {
            // If we don't care about this kind of warning, or we are currently recovering from an error, bail
            if (!diagnosticFilter.HasFlag(kind) || isRecovering)
                return;

            // If we have hit an actual error, start error recovery mode
            if (kind != DiagnosticFlags.Warning)
            {
                isRecovering = true;
            }

            // Throw an exception if requested and while we aren't speculating
            if (throwExceptionOnError && snapshots.Count == 0 && kind != DiagnosticFlags.Warning)
            {
                throw new Exception(PrintingUtil.FormatError(anchorSpan, msg));
            }

            // Log the diagnostic
            diagnostics.Add(new Diagnostic(anchorSpan, kind, Stage, msg));
        }

        protected void Error(DiagnosticFlags kind, string msg, SourceSpan span)
        {
            anchorSpan = span;
            Error(kind, msg);
        }

        protected void Error(string expected, Token<T> token)
        {
            Error(DiagnosticFlags.SyntaxError, $"Expected {expected}, got token ({token})", token.Span);
        }

        protected List<Token<T>> Range(Token<T> first, Token<T> last)
        {
            if (first == null || last == null) return new List<Token<T>>();
            int count = last.Position - first.Position + 1;
            if (count < 0) count = 0;
            if (first.Position + count > tokens.Count) count = tokens.Count - first.Position;
            return tokens.GetRange(first.Position, count);
        }

        protected string ParseIdentifier()
        {
            Token<T> identifierToken = Eat(IdentifierTokenKind);
            string identifier = identifierToken.Identifier ?? string.Empty;
            if (string.IsNullOrEmpty(identifier))
                Error("a valid identifier", identifierToken);
            return identifier;
        }

        protected string ParseStringLiteral()
        {
            Token<T> literalToken = Eat(StringLiteralTokenKind);
            return literalToken.Identifier ?? string.Empty;
        }

        protected float ParseNumericLiteral()
        {
            Token<T> literalToken = Eat(FloatLiteralTokenKind, IntegerLiteralTokenKind);
            string literal = literalToken.Identifier ?? string.Empty;
            if (string.IsNullOrEmpty(literal))
                Error("a valid numeric literal", literalToken);
            return float.Parse(literal, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture);
        }

        protected int ParseIntegerLiteral()
        {
            return (int)ParseNumericLiteral();
        }

        protected byte ParseByteLiteral()
        {
            return (byte)ParseNumericLiteral();
        }

        protected TEnum ParseEnum<TEnum>(string expected)
            where TEnum : struct
        {
            Token<T> next = Advance();
            // ShaderLab has a lot of ambiguous syntax, many keywords are reused in multiple places as regular identifiers.
            // If we fail to use the identifier directly, it might be an overlapping keyword, so try that instead.
            string identifier = next.Identifier ?? next.Kind.ToString()?.Replace("Keyword", "") ?? String.Empty;
            if (Enum.TryParse(identifier, true, out TEnum result))
            {
                return result;
            }
            else
            {
                Error(expected, next);
                return default;
            }
        }

        #region Parser combinators
        protected List<P> ParseSeparatedList0<P>(T end, T separator, Func<P> parser, bool allowTrailingSeparator = false)
        {
            if (Match(end))
                return new List<P>();

            List<P> result = new List<P>();

            result.Add(parser());

            if (isRecovering)
                return result;

            while (Match(separator))
            {
                int lastPosition = position;

                Advance();
                if (!allowTrailingSeparator || !Match(end))
                {
                    result.Add(parser());
                }

                if (isRecovering)
                    return result;

                if (lastPosition == position)
                {
#if DEBUG
                    throw new Exception($"Parser got stuck parsing {Peek()}. Please file a bug report.");
#else
                    return result;
#endif
                }
            }

            return result;
        }

        protected List<P> ParseSeparatedList1<P>(T seperator, Func<P> parser)
        {
            List<P> result = new List<P>();

            result.Add(parser());

            if (isRecovering)
                return result;

            while (Match(seperator))
            {
                int lastPosition = position;

                Eat(seperator);
                result.Add(parser());

                if (isRecovering)
                    return result;

                if (lastPosition == position)
                {
#if DEBUG
                    throw new Exception($"Parser got stuck parsing {Peek()}. Please file a bug report.");
#else
                    return result;
#endif
                }
            }

            return result;
        }

        protected List<P> ParseMany1<P>(T first, Func<P> parser)
        {
            List<P> result = new List<P>();

            result.Add(parser());

            if (isRecovering)
                return result;

            while (Match(first))
            {
                int lastPosition = position;

                result.Add(parser());

                if (isRecovering)
                    return result;

                if (lastPosition == position)
                {
#if DEBUG
                    throw new Exception($"Parser got stuck parsing {Peek()}. Please file a bug report.");
#else
                    return result;
#endif
                }
            }

            return result;
        }

        protected List<P> ParseMany0<P>(T first, Func<P> parser)
        {
            if (!Match(first))
                return new List<P>();

            return ParseMany1(first, parser);
        }

        protected List<P> ParseMany1<P>(Func<bool> first, Func<P> parser)
        {
            List<P> result = new List<P>();

            result.Add(parser());

            if (isRecovering)
                return result;

            while (first())
            {
                int lastPosition = position;

                result.Add(parser());

                if (isRecovering)
                    return result;

                if (lastPosition == position)
                {
#if DEBUG
                    throw new Exception($"Parser got stuck parsing {Peek()}. Please file a bug report.");
#else
                    return result;
#endif
                }
            }

            return result;
        }

        protected List<P> ParseMany0<P>(Func<bool> first, Func<P> parser)
        {
            if (!first())
                return new List<P>();

            return ParseMany1(first, parser);
        }

        protected P ParseOptional<P>(T first, Func<P> parser)
        {
            if (Match(first))
                return parser();
            return default;
        }

        protected P ParseOptional<P>(Func<bool> first, Func<P> parser)
        {
            if (first())
                return parser();
            return default;
        }

        protected void RecoverTo(T sync, bool inclusive = true)
        {
            // If not recovering, nothing to do
            if (!isRecovering)
                return;

            // Otherwise advance until the sync token
            isRecovering = false;
            while (!IsAtEnd() && !Match(sync)) Advance();
            if (inclusive && Match(sync)) Advance();
        }

        protected void RecoverTo(Func<T, bool> predicate, bool inclusive = true)
        {
            // If not recovering, nothing to do
            if (!isRecovering)
                return;

            // Otherwise advance until the sync token
            isRecovering = false;
            while (!IsAtEnd() && !predicate(Peek().Kind)) Advance();
            if (inclusive && predicate(Peek().Kind)) Advance();
        }

        protected void RecoverTo(params T[] syncs)
        {
            // If not recovering, nothing to do
            if (!isRecovering)
                return;

            // Otherwise advance until the sync token
            isRecovering = false;
            while (!IsAtEnd() && !Match(syncs)) Advance();
            if (Match(syncs)) Advance();
        }
        #endregion
    }
}
