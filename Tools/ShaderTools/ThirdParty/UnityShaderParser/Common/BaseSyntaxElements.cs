using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Illusion.ShaderTools.Syntax.Common
{
    public enum ParserStage
    {
        HLSLLexing,
        HLSLPreProcessing,
        HLSLParsing,
        ShaderLabLexing,
        ShaderLabParsing,
    }

    public struct SourceLocation
    {
        // For better diagnostics
        public int Line { get; }
        public int Column { get; }

        // For analysis
        public int Index { get; }

        public SourceLocation(int line, int column, int index)
        {
            Line = line;
            Column = column;
            Index = index;
        }

        public override string ToString() => $"({Line}, {Column})";
    }

    [Flags]
    public enum DiagnosticFlags
    {
        None = 0,
        SyntaxError = 1 << 0,       // Ill-formed source code
        SemanticError = 1 << 1,     // Well-formed source code, but incorrect meaning
        PreProcessorError = 1 << 2, // Error during preprocessing
        Warning = 1 << 3,           // Well-formed source code, but probably not what was intended

        OnlyErrors = SyntaxError | SemanticError | PreProcessorError,
        All = OnlyErrors | Warning
    }

    public struct Diagnostic
    {
        public SourceLocation Location => Span.Start;
        public SourceSpan Span { get; }
        public DiagnosticFlags Kind { get; }
        public ParserStage Stage { get; }
        public string Text { get; }

        public Diagnostic(SourceSpan span, DiagnosticFlags kind, ParserStage stage, string text)
        {
            Span = span;
            Kind = kind;
            Stage = stage;
            Text = text;
        }

        public override string ToString()
        {
            return (Kind & DiagnosticFlags.Warning) != 0
                ? PrintingUtil.FormatWarning(Span, Text)
                : PrintingUtil.FormatError(Span, Text);
        }
    }

    public struct SourceSpan
    {
        public string BasePath { get; }
        public string FileName { get; }
        public SourceLocation Start { get; }
        public SourceLocation End { get; }

        public int StartIndex => Start.Index;
        public int EndIndex => End.Index;
        public int Length => EndIndex - StartIndex;

        public SourceSpan(string basePath, string fileName, SourceLocation start, SourceLocation end)
        {
            BasePath = basePath;
            FileName = fileName;
            Start = start;
            End = end;
        }

        public override string ToString() => $"({Start.Line}:{Start.Column} - {End.Line}:{End.Column})";

        public string GetCodeInSourceText(string sourceText) => sourceText.Substring(StartIndex, Length);

        public static SourceSpan FromTokens<T>(IEnumerable<Token<T>> tokens)
            where T : struct
        {
            if (tokens == null || !tokens.Any())
                throw new ArgumentException(nameof(tokens));
            var ordered = tokens.OrderBy(x => x.Span.StartIndex);
            var first = ordered.First();
            var last = ordered.Last();
            return BetweenTokens(first, last);
        }

        public static SourceSpan BetweenTokens<T>(Token<T> first, Token<T> last)
            where T : struct => new SourceSpan(first.Span.BasePath, first.Span.FileName, first.Span.Start, last.Span.End);

        public static SourceSpan Between(SourceSpan first, SourceSpan last)
            => new SourceSpan(first.BasePath, first.FileName, first.Start, last.End);
    }

    public enum SyntaxTriviaKind
    {
        SingleLineComment,
        MultiLineComment,
    }

    public class SyntaxTrivia
    {
        public SyntaxTriviaKind Kind { get; private set; }
        public string Text { get; private set; }
        public SourceSpan Span { get; private set; }

        public SyntaxTrivia(SyntaxTriviaKind kind, string text, SourceSpan span)
        {
            Kind = kind;
            Text = text;
            Span = span;
        }

        public override string ToString() => $"{Kind}({Text})";
    }

    public class Token<T>
        where T : struct
    {
        public T Kind { get; private set; }
        public string Identifier { get; private set; }       // Optional
        public SourceSpan Span { get; private set; }         // Location in source code
        public SourceSpan OriginalSpan { get; private set; }
        public int Position { get; private set; }            // Location in token stream

        internal void RemapSpan(SourceSpan span)
        {
            Span = span;
            OriginalSpan = span;
        }

        public List<SyntaxTrivia> RawLeadingTrivia;  // Null if no trivia

        public bool HasLeadingTrivia => RawLeadingTrivia != null;

        public IReadOnlyList<SyntaxTrivia> LeadingTrivia => RawLeadingTrivia ?? (IReadOnlyList<SyntaxTrivia>)Array.Empty<SyntaxTrivia>();

        public void AddLeadingTrivia(SyntaxTrivia trivia)
        {
            if (RawLeadingTrivia == null) RawLeadingTrivia = new List<SyntaxTrivia>();
            RawLeadingTrivia.Add(trivia);
        }

        public void AddLeadingTrivia(List<SyntaxTrivia> trivias)
        {
            if (trivias.Count == 0) return;
            if (RawLeadingTrivia == null) RawLeadingTrivia = new List<SyntaxTrivia>(trivias);
            else RawLeadingTrivia.AddRange(trivias);
        }

        public Token(T kind, string identifier, SourceSpan span, int position)
        {
            Kind = kind;
            Identifier = identifier;
            Span = span;
            OriginalSpan = span;
            Position = position;
        }

        public Token(T kind, string identifier, SourceSpan span, SourceSpan originalSpan, int position)
        {
            Kind = kind;
            Identifier = identifier;
            Span = span;
            OriginalSpan = originalSpan;
            Position = position;
        }

        public Token(T kind, string identifier, SourceSpan span, SourceSpan originalSpan, int position, List<SyntaxTrivia> leadingTrivia)
        {
            Kind = kind;
            Identifier = identifier;
            Span = span;
            OriginalSpan = originalSpan;
            Position = position;
            RawLeadingTrivia = leadingTrivia;
        }

        public override string ToString()
        {
            if (Identifier == null)
                return Kind.ToString() ?? string.Empty;
            else
                return $"{Kind}({Identifier})";
        }

        public string GetCodeInSourceText(string sourceText) => Span.GetCodeInSourceText(sourceText);
    }

    public abstract class SyntaxNode<TSelf>
        where TSelf : SyntaxNode<TSelf>
    {
        // Helpers
        protected static IEnumerable<TSelf> MergeChildren(params IEnumerable<TSelf>[] children)
            => children.SelectMany(x => x);
        protected static IEnumerable<TSelf> OptionalChild(TSelf child)
            => child == null ? Enumerable.Empty<TSelf>() : new[] { child };
        protected static IEnumerable<TSelf> Child(TSelf child)
            => new[] { child };

        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        protected abstract IEnumerable<TSelf> GetChildren { get; }

        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private TSelf parent;
        internal void ComputeParents()
        {
            foreach (var child in GetChildren)
            {
                if (child == null) continue;
                child.parent = (TSelf)this;
                child.ComputeParents();
            }
        }

        // Public API
        public List<TSelf> Children => GetChildren.Where(x => x != null).ToList();
        public IEnumerable<TSelf> ChildNodes => GetChildren;
        public TSelf Parent => parent;
        public abstract SourceSpan Span { get; }
        public abstract SourceSpan OriginalSpan { get; }
    }

    public enum PrettyEnumStyle
    {
        AllLowerCase,
        AllUpperCase,
        CamelCase,
        PascalCase,
    }

    public class PrettyEnumAttribute : Attribute
    {
        public PrettyEnumStyle Style { get; set; }
        public PrettyEnumAttribute(PrettyEnumStyle firstIsLowerCase)
        {
            Style = firstIsLowerCase;
        }
    }

    public class PrettyNameAttribute : Attribute
    {
        public string Name { get; set; }

        public PrettyNameAttribute(string name)
        {
            Name = name;
        }
    }

    public static class PrintingUtil
    {
        public static string FormatSpanForDiagnostic(SourceSpan span)
        {
            string file = span.FileName ?? string.Empty;
            SourceLocation start = span.Start;
            SourceLocation end = span.End;
            if (end.Line == start.Line && end.Column > start.Column)
                return $"{file}({start.Line}, {start.Column}-{end.Column})";
            return $"{file}({start.Line}, {start.Column})";
        }

        public static string FormatError(SourceSpan span, string message) =>
            $"Error at {FormatSpanForDiagnostic(span)}: {message}";

        public static string FormatWarning(SourceSpan span, string message) =>
            $"Warning at {FormatSpanForDiagnostic(span)}: {message}";

        // Slower type-erased version. Need to duplicate code to avoid another reflection call :(
        public static string GetEnumNameTypeErased(object val)
        {
            string name;
            PrettyEnumAttribute[] enumAttrs = val.GetType().GetCustomAttributes<PrettyEnumAttribute>().ToArray();
            if (enumAttrs == null || enumAttrs.Length == 0)
            {
                name = Enum.GetName(val.GetType(), val);
            }
            else
            {
                MemberInfo[] memberInfo = val.GetType().GetMember(val.ToString());
                if (memberInfo != null && memberInfo.Length > 0)
                {
                    foreach (MemberInfo member in memberInfo)
                    {
                        PrettyNameAttribute[] attrs = member.GetCustomAttributes<PrettyNameAttribute>().ToArray();

                        if (attrs != null && attrs.Length > 0)
                        {
                            //Pull out the description value
                            return attrs[0].Name;
                        }
                    }
                }
                name = Enum.GetName(val.GetType(), val);
            }

            switch (enumAttrs[0].Style)
            {
                case PrettyEnumStyle.AllLowerCase: return name.ToLower();
                case PrettyEnumStyle.AllUpperCase: return name.ToUpper();
                case PrettyEnumStyle.CamelCase:
                    if (name.Length > 0)
                    {
                        name = $"{char.ToLower(name[0])}{name.Substring(1)}";
                    }
                    return name;
                default:
                    return name;
            }
        }

        public static string GetEnumName<T>(T val)
            where T : Enum
        {
            string name;
            PrettyEnumAttribute[] enumAttrs = typeof(T).GetCustomAttributes<PrettyEnumAttribute>().ToArray();
            if (enumAttrs == null || enumAttrs.Length == 0)
            {
                name = Enum.GetName(typeof(T), val);
            }
            else
            {
                MemberInfo[] memberInfo = typeof(T).GetMember(val.ToString());
                if (memberInfo != null && memberInfo.Length > 0)
                {
                    foreach (MemberInfo member in memberInfo)
                    {
                        PrettyNameAttribute[] attrs = member.GetCustomAttributes<PrettyNameAttribute>().ToArray();

                        if (attrs != null && attrs.Length > 0)
                        {
                            //Pull out the description value
                            return attrs[0].Name;
                        }
                    }
                }
                name = Enum.GetName(typeof(T), val);
            }

            switch (enumAttrs[0].Style)
            {
                case PrettyEnumStyle.AllLowerCase: return name.ToLower();
                case PrettyEnumStyle.AllUpperCase: return name.ToUpper();
                case PrettyEnumStyle.CamelCase:
                    if (name.Length > 0)
                    {
                        name = $"{char.ToLower(name[0])}{name.Substring(1)}";
                    }
                    return name;
                default:
                    return name;
            }
        }

        // TODO: Edits across macro boundaries
        public static string ApplyEditsToSourceText(IEnumerable<(SourceSpan span, string newText)> edits, string source)
        {
            var orderedEdits = edits.OrderBy(x => x.span.Start.Index);
            var editedSourced = new StringBuilder(source);
            int offset = 0;
            foreach ((SourceSpan span, string newText) in orderedEdits)
            {
                editedSourced.Remove(span.Start.Index + offset, span.Length);
                editedSourced.Insert(span.Start.Index + offset, newText);
                offset -= span.Length;
                offset += newText.Length;
            }
            return editedSourced.ToString();
        }
    }
}
