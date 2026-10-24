using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Illusion.ShaderTools.Syntax.Common;
using Illusion.ShaderTools.Syntax.HLSL;
using Illusion.ShaderTools.Syntax.HLSL.PreProcessor;
using Illusion.ShaderTools.Syntax.ShaderLab;
using HlslToken = Illusion.ShaderTools.Syntax.HLSL.TokenKind;

namespace Illusion.ShaderTools
{
    public static class ShaderSource
    {
        private static readonly string[] TokenPrefixes = Enumerable.Range(0,
            Enum.GetValues(typeof(HlslToken)).Cast<HlslToken>().Max(t => (int)t) + 1).Select(i => i + ":").ToArray();

        public static ShaderNode ParseShaderLab(string source, string fileName = null) =>
            ShaderParser.ParseUnityShader(source, new ShaderLabParserConfig
            {
                FileName = fileName,
                ParseEmbeddedHLSL = false,
                IncludeProgramBlockPreamble = false,
                ThrowExceptionOnError = true
            });

        public static HlslDocument ParseHlsl(string source, string fileName = null)
        {
            var chars = source.ToCharArray();
            var locations = new List<(int Offset, int Line, string File)>();
            int line = 1;
            for (int offset = 0; offset < chars.Length;)
            {
                int end = source.IndexOf('\n', offset);
                if (end < 0) end = source.Length;
                int first = offset;
                while (first < end && char.IsWhiteSpace(source[first])) first++;
                locations.Add((offset, line, fileName));
                if (first < end && source[first] == '#')
                {
                    string text = source.Substring(first, end - first);
                    if (text.StartsWith("#line ", StringComparison.Ordinal))
                    {
                        string value = text.Substring(6).TrimStart();
                        int space = value.IndexOf(' ');
                        if (int.TryParse(space < 0 ? value : value.Substring(0, space), out int next))
                            line = next - 1;
                        int quote = value.IndexOf('"');
                        int lastQuote = value.LastIndexOf('"');
                        if (quote >= 0 && lastQuote > quote)
                            fileName = value.Substring(quote + 1, lastQuote - quote - 1);
                    }
                    for (int i = offset; i < end; i++) chars[i] = ' ';
                }
                offset = end + 1;
                line++;
            }

            if (locations.Count == 0) locations.Add((0, 1, fileName));
            var tokens = HLSLLexer.Lex(new string(chars), null, fileName, true, out _);
            for (int i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];
                var start = locations[Math.Min(token.Span.Start.Line - 1, locations.Count - 1)];
                var end = locations[Math.Min(token.Span.End.Line - 1, locations.Count - 1)];
                var span = new SourceSpan(null, start.File,
                    new SourceLocation(start.Line, token.Span.Start.Column, token.Span.StartIndex),
                    new SourceLocation(end.Line, token.Span.End.Column, token.Span.EndIndex));
                token.RemapSpan(span);
            }
            var nodes = HLSLParser.ParseTopLevelDeclarations(tokens, new HLSLParserConfig
            {
                PreProcessorMode = PreProcessorMode.DoNothing,
                ThrowExceptionOnError = true
            }, out _, out _);
            return new HlslDocument(source, nodes);
        }

        public static string Fingerprint(HLSLSyntaxNode node)
        {
            var result = new StringBuilder();
            for (int i = 0; i < node.Tokens.Count; i++)
            {
                var token = node.Tokens[i];
                if (i != 0) result.Append(' ');
                result.Append(TokenPrefixes[(int)token.Kind]).Append(token.Identifier);
            }
            return result.ToString();
        }

        public static string Fingerprint(string preprocessed) =>
            string.Join("\n", ParseHlsl(preprocessed).Declarations.Select(d => Fingerprint(d.Node)));

        public static string Code(string source, HLSLSyntaxNode node) =>
            source.Substring(node.Span.StartIndex, node.Span.Length);

        public static string LocatedCode(string source, HLSLSyntaxNode node)
        {
            string code = Code(source, node);
            return string.IsNullOrEmpty(node.Span.FileName) ? code :
                $"#line {node.Span.Start.Line} \"{node.Span.FileName}\"\n{code}";
        }

        public static string Edit(string source, IEnumerable<(int Start, int Length, string Text)> edits)
        {
            var result = new StringBuilder(source);
            foreach (var edit in edits.OrderByDescending(e => e.Start))
                result.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Text);
            return result.ToString();
        }
    }
}
