using System;
using System.Collections.Generic;
using System.Linq;
using Illusion.ShaderTools.Syntax.Common;

namespace Illusion.ShaderTools.Syntax.HLSL
{
    public abstract class HLSLEditor : HLSLSyntaxVisitor
    {
        public string Source { get; private set; }
        public List<Token<TokenKind>> Tokens { get; private set; }

        public HLSLEditor(string source, List<Token<TokenKind>> tokens)
        {
            Source = source;
            Tokens = tokens;
        }

        protected HashSet<(SourceSpan span, string newText)> Edits = new HashSet<(SourceSpan, string)>();

        protected void Edit(SourceSpan span, string newText) => Edits.Add((span, newText));
        protected void Edit(Token<TokenKind> token, string newText) => Edit(token.Span, newText);
        protected void Edit(HLSLSyntaxNode node, string newText) => Edit(node.Span, newText);
        protected void AddBefore(SourceSpan span, string newText) => Edit(new SourceSpan(span.BasePath, span.FileName, span.Start, span.Start), newText);
        protected void AddBefore(Token<TokenKind> token, string newText) => Edit(new SourceSpan(token.Span.BasePath, token.Span.FileName, token.Span.Start, token.Span.Start), newText);
        protected void AddBefore(HLSLSyntaxNode node, string newText) => Edit(new SourceSpan(node.Span.BasePath, node.Span.FileName, node.Span.Start, node.Span.Start), newText);
        protected void AddAfter(SourceSpan span, string newText) => Edit(new SourceSpan(span.BasePath, span.FileName, span.End, span.End), newText);
        protected void AddAfter(Token<TokenKind> token, string newText) => Edit(new SourceSpan(token.Span.BasePath, token.Span.FileName, token.Span.End, token.Span.End), newText);
        protected void AddAfter(HLSLSyntaxNode node, string newText) => Edit(new SourceSpan(node.Span.BasePath, node.Span.FileName, node.Span.End, node.Span.End), newText);

        public string ApplyCurrentEdits() => PrintingUtil.ApplyEditsToSourceText(Edits, Source);

        public string ApplyEdits(HLSLSyntaxNode node)
        {
            Visit(node);
            return ApplyCurrentEdits();
        }

        public string ApplyEdits(IEnumerable<HLSLSyntaxNode> nodes)
        {
            VisitMany(nodes);
            return ApplyCurrentEdits();
        }

        public static string RunEditor<T>(string source, HLSLSyntaxNode node)
            where T : HLSLEditor
        {
            var editor = (HLSLEditor)Activator.CreateInstance(typeof(T), source, node.Tokens);
            return editor.ApplyEdits(node);
        }

        public static string RunEditor<T>(string source, IEnumerable<HLSLSyntaxNode> node)
            where T : HLSLEditor
        {
            var editor = (HLSLEditor)Activator.CreateInstance(typeof(T), source, node.SelectMany(x => x.Tokens).ToList());
            return editor.ApplyEdits(node);
        }
    }
}
