using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Illusion.ShaderTools.Syntax.Common;
using Illusion.ShaderTools.Syntax.HLSL;

namespace Illusion.ShaderTools.Syntax.ShaderLab
{
    using SLToken = Token<TokenKind>;
    using HLSLToken = Token<HLSL.TokenKind>;

    public class ShaderLabParserConfig : HLSLParserConfig
    {
        /// <summary>
        /// Whether to parse HLSL code embedded in CGPROGRAM or HLSLPROGRAM blocks into syntax trees, or to leave it as raw text.
        /// </summary>
        public bool ParseEmbeddedHLSL { get; set; }

        /// <summary>
        /// Whether to automatically include preambles for CGPROGRAM or HLSLPROGRAM blocks.
        /// For example, CGPROGRAM blocks automatically include 'UnityCG.cginc' in Unity.
        /// If that is undesired, set this field to false.
        /// </summary>
        public bool IncludeProgramBlockPreamble { get; set; }

        public ShaderLabParserConfig()
            : base()
        {
            ParseEmbeddedHLSL = true;
            IncludeProgramBlockPreamble = true;
        }

        public ShaderLabParserConfig(ShaderLabParserConfig config)
            : base(config)
        {
            ParseEmbeddedHLSL = config.ParseEmbeddedHLSL;
            IncludeProgramBlockPreamble = config.IncludeProgramBlockPreamble;
        }
    }

    public class ShaderLabParser : BaseParser<TokenKind>
    {
        public ShaderLabParser(List<SLToken> tokens, ShaderLabParserConfig config)
            : base(tokens, config.ThrowExceptionOnError, config.DiagnosticFilter)
        {
            this.config = config;
        }

        protected override TokenKind StringLiteralTokenKind => TokenKind.StringLiteralToken;
        protected override TokenKind IntegerLiteralTokenKind => TokenKind.IntegerLiteralToken;
        protected override TokenKind FloatLiteralTokenKind => TokenKind.FloatLiteralToken;
        protected override TokenKind IdentifierTokenKind => TokenKind.IdentifierToken;
        protected override TokenKind InvalidTokenKind => TokenKind.InvalidToken;
        protected override ParserStage Stage => ParserStage.ShaderLabParsing;

        // Tokens that we may be able to recover to after encountered an error in a command.
        private static readonly HashSet<TokenKind> commandSyncTokens = new HashSet<TokenKind>()
        {
            TokenKind.TagsKeyword, TokenKind.LodKeyword, TokenKind.LightingKeyword, TokenKind.SeparateSpecularKeyword,
            TokenKind.ZWriteKeyword,TokenKind.AlphaToMaskKeyword, TokenKind.ZClipKeyword, TokenKind.ConservativeKeyword,
            TokenKind.CullKeyword, TokenKind.ZTestKeyword, TokenKind.BlendKeyword, TokenKind.OffsetKeyword, TokenKind.ColorMaskKeyword,
            TokenKind.AlphaTestKeyword, TokenKind.FogKeyword, TokenKind.NameKeyword, TokenKind.BindChannelsKeyword, TokenKind.ColorKeyword,
            TokenKind.BlendOpKeyword,TokenKind.MaterialKeyword, TokenKind.SetTextureKeyword, TokenKind.ColorMaterialKeyword, TokenKind.StencilKeyword,
            TokenKind.SubShaderKeyword, TokenKind.ShaderKeyword, TokenKind.PassKeyword, TokenKind.CategoryKeyword, TokenKind.GrabPassKeyword,
            TokenKind.UsePassKeyword, TokenKind.FallbackKeyword, TokenKind.CustomEditorKeyword,
        };

        protected ShaderLabParserConfig config = default;
        protected Stack<List<HLSLIncludeBlock>> currentIncludeBlocks = new Stack<List<HLSLIncludeBlock>>();

        public static ShaderNode Parse(List<SLToken> tokens, ShaderLabParserConfig config, out List<Diagnostic> diagnostics)
        {
            ShaderLabParser parser = new ShaderLabParser(tokens, config);
            var result = parser.ParseShader();
            result.ComputeParents();
            diagnostics = parser.diagnostics;
            return result;
        }

        public static ShaderNode ParseShader(List<SLToken> tokens, ShaderLabParserConfig config, out List<Diagnostic> diagnostics)
        {
            return Parse(tokens, config, out diagnostics);
        }

        public static SubShaderNode ParseSubShader(List<SLToken> tokens, ShaderLabParserConfig config, out List<Diagnostic> diagnostics)
        {
            ShaderLabParser parser = new ShaderLabParser(tokens, config);
            var result = parser.ParseSubShader();
            result.ComputeParents();
            diagnostics = parser.diagnostics;
            return result;
        }

        public static ShaderPassNode ParseShaderPass(List<SLToken> tokens, ShaderLabParserConfig config, out List<Diagnostic> diagnostics)
        {
            ShaderLabParser parser = new ShaderLabParser(tokens, config);
            ShaderPassNode result = null;
            switch (parser.Peek().Kind)
            {
                case TokenKind.PassKeyword: result = parser.ParseCodePass(); break;
                case TokenKind.GrabPassKeyword: result = parser.ParseGrabPass(); break;
                case TokenKind.UsePassKeyword: result = parser.ParseUsePass(); break;
            }
            result.ComputeParents();
            diagnostics = parser.diagnostics;
            return result;
        }

        public static ShaderPropertyNode ParseShaderProperty(List<SLToken> tokens, ShaderLabParserConfig config, out List<Diagnostic> diagnostics)
        {
            ShaderLabParser parser = new ShaderLabParser(tokens, config);
            var result = parser.ParseProperty();
            result.ComputeParents();
            diagnostics = parser.diagnostics;
            return result;
        }

        public static List<ShaderPropertyNode> ParseShaderProperties(List<SLToken> tokens, ShaderLabParserConfig config, out List<Diagnostic> diagnostics)
        {
            ShaderLabParser parser = new ShaderLabParser(tokens, config);
            List<ShaderPropertyNode> result = new List<ShaderPropertyNode>();
            while (parser.Match(TokenKind.IdentifierToken, TokenKind.BracketedStringLiteralToken))
            {
                result.Add(parser.ParseProperty());
            }
            foreach (var property in result)
            {
                property.ComputeParents();
            }
            diagnostics = parser.diagnostics;
            return result;
        }

        public static List<ShaderPropertyNode> ParseShaderPropertyBlock(List<SLToken> tokens, ShaderLabParserConfig config, out List<Diagnostic> diagnostics)
        {
            ShaderLabParser parser = new ShaderLabParser(tokens, config);
            List<ShaderPropertyNode> result = new List<ShaderPropertyNode>();
            parser.ParsePropertySection(result);
            foreach (var property in result)
            {
                property.ComputeParents();
            }
            diagnostics = parser.diagnostics;
            return result;
        }

        public static ShaderLabCommandNode ParseShaderLabCommand(List<SLToken> tokens, ShaderLabParserConfig config, out List<Diagnostic> diagnostics)
        {
            ShaderLabParser parser = new ShaderLabParser(tokens, config);
            parser.TryParseCommand(out var result);
            result.ComputeParents();
            diagnostics = parser.diagnostics;
            return result;
        }

        public static List<ShaderLabCommandNode> ParseShaderLabCommands(List<SLToken> tokens, ShaderLabParserConfig config, out List<Diagnostic> diagnostics)
        {
            ShaderLabParser parser = new ShaderLabParser(tokens, config);
            List<ShaderLabCommandNode> result = new List<ShaderLabCommandNode>();
            parser.ParseCommandsIfPresent(result);
            foreach (var property in result)
            {
                property.ComputeParents();
            }
            diagnostics = parser.diagnostics;
            return result;
        }

        // Concatenate token streams, but merge the trivia from the EOF token.
        private static void AppendLexed(List<HLSLToken> dest, List<HLSLToken> src)
        {
            if (dest.Count > 0 && dest[dest.Count - 1].Kind == HLSL.TokenKind.EndOfFileToken)
            {
                var eof = dest[dest.Count - 1];
                dest.RemoveAt(dest.Count - 1);
                if (eof.HasLeadingTrivia && src.Count > 0)
                {
                    var first = src[0];
                    var combined = new List<SyntaxTrivia>(eof.RawLeadingTrivia);
                    if (first.HasLeadingTrivia) combined.AddRange(first.RawLeadingTrivia);
                    src[0] = new HLSLToken(first.Kind, first.Identifier, first.Span, first.OriginalSpan, first.Position, combined);
                }
            }
            dest.AddRange(src);
        }
        protected void ProcessCurrentIncludes(
            SLToken programToken,
            bool lexEmbeddedHLSL,
            out string fullCode,
            out List<HLSLToken> tokenStream)
        {
            tokenStream = new List<HLSLToken>();
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < currentIncludeBlocks.Count; i++)
            {
                var includeBlockList = currentIncludeBlocks.ElementAt(currentIncludeBlocks.Count - 1 - i);
                foreach (var includeBlock in includeBlockList)
                {
                    if (lexEmbeddedHLSL)
                    {
                        AppendLexed(tokenStream, HLSLLexer.Lex(includeBlock.Code, config.BasePath, config.FileName, config.ThrowExceptionOnError, includeBlock.Span.Start, out var includeLexerDiags));
                        diagnostics.AddRange(includeLexerDiags);
                    }
                    sb.Append(includeBlock.Code);
                }
            }
            if (lexEmbeddedHLSL)
            {
                AppendLexed(tokenStream, HLSLLexer.Lex(programToken.Identifier, config.BasePath, config.FileName, config.ThrowExceptionOnError, programToken.Span.Start, out var lexerDiags));
                diagnostics.AddRange(lexerDiags);
            }
            sb.Append(programToken.Identifier);
            fullCode = sb.ToString();
        }
        protected HLSLProgramBlock ParseOrSkipEmbeddedHLSL()
        {
            var programToken = Eat(TokenKind.CgProgramBlock, TokenKind.HlslProgramBlock, TokenKind.GlslProgramBlock);
            ProgramKind kind = ProgramKind.Cg;
            if (programToken.Kind == TokenKind.HlslProgramBlock)
                kind = ProgramKind.Hlsl;
            else if (programToken.Kind == TokenKind.GlslProgramBlock)
                kind = ProgramKind.Glsl;

            string program = programToken.Identifier;
            
            // Prepend include blocks
            ProcessCurrentIncludes(
                programToken,
                config.ParseEmbeddedHLSL,
                out string fullCode,
                out var tokenStream);

            // Try to figure out if we have surface shader.
            // Surface shaders have some additional implicit includes.
            string[] lines = fullCode.Split('\n');
            bool isSurfaceShader = false;
            foreach (string line in lines)
            {
                if (line.TrimStart().StartsWith("#pragma"))
                {
                    string[] args = line.TrimStart().Split(' ');
                    if (args.Length > 0)
                    {
                        if (args[1] == "surface")
                        {
                            isSurfaceShader = true;
                            break;
                        }
                        else if (args[1] == "vertex" || args[1] == "fragment")
                        {
                            isSurfaceShader = false;
                            break;
                        }
                    }
                }
            }

            // Add preamble
            string preamble = string.Empty;
            if (config.IncludeProgramBlockPreamble)
            {
                if (isSurfaceShader)
                {
                    // Surface shader compiler has some secret INTERNAL_DATA macro and special includes :(
                    preamble = $"#ifndef INTERNAL_DATA\n#define INTERNAL_DATA\n#endif\n#include \"UnityCG.cginc\"\n";
                }
                else if (programToken.Kind != TokenKind.HlslProgramBlock) // HLSLPROGRAM doesn't include anything implicitly.
                {
                    // UnityShaderVariables.cginc should always be included otherwise
                    preamble = $"#include \"UnityShaderVariables.cginc\"\n";
                }
            }
            fullCode = $"{preamble}{fullCode}";
            if (!config.ParseEmbeddedHLSL)
            {
                return new HLSLProgramBlock
                {
                    CodeWithoutIncludes = program,
                    FullCode = fullCode,
                    Span = programToken.Span,
                    Pragmas = new List<string>(),
                    TopLevelDeclarations = new List<HLSLSyntaxNode>(),
                    Kind = kind,
                };
            }

            // Lex preamble
            var premableTokens = HLSLLexer.Lex(preamble, config.BasePath, config.FileName, config.ThrowExceptionOnError, out var lexerDiags);
            diagnostics.InsertRange(0, lexerDiags);
            AppendLexed(premableTokens, tokenStream);
            tokenStream = premableTokens;

            // Other PreProcessorModes regenerate positions via HLSLPreProcessor.PreProcess,
            // need this for DoNothing too
            if (config.PreProcessorMode == HLSL.PreProcessor.PreProcessorMode.DoNothing)
            {
                for (int i = 0; i < tokenStream.Count; i++)
                {
                    var t = tokenStream[i];
                    if (t.Position != i)
                        tokenStream[i] = new HLSLToken(t.Kind, t.Identifier, t.Span, t.OriginalSpan, i, t.RawLeadingTrivia);
                }
            }

            // TODO: Don't redo the parsing work every time - it's slow x)
            var decls = HLSLParser.ParseTopLevelDeclarations(tokenStream, config, out var parserDiags, out var pragmas);
            diagnostics.AddRange(parserDiags);
            return new HLSLProgramBlock
            {
                CodeWithoutIncludes = program,
                FullCode = fullCode,
                Span = programToken.Span,
                Pragmas = pragmas,
                TopLevelDeclarations = decls,
                Kind = kind,
            };
        }
        protected void PushIncludes() => currentIncludeBlocks.Push(new List<HLSLIncludeBlock>());
        protected void PopIncludes() => currentIncludeBlocks.Pop();
        protected void SetIncludes(List<HLSLIncludeBlock> includes)
        {
            currentIncludeBlocks.Pop();
            currentIncludeBlocks.Push(includes);
        }

        public ShaderNode ParseShader()
        {
            PushIncludes();

            var keywordTok = Eat(TokenKind.ShaderKeyword);
            string name = Eat(TokenKind.StringLiteralToken).Identifier ?? string.Empty;
            Eat(TokenKind.OpenBraceToken);

            List<HLSLIncludeBlock> includeBlocks = new List<HLSLIncludeBlock>();

            ParseIncludeBlocksIfPresent(includeBlocks);

            List<ShaderPropertyNode> properties = new List<ShaderPropertyNode>();
            if (Match(TokenKind.PropertiesKeyword))
            {
                ParsePropertySection(properties);
            }

            List<SubShaderNode> subshaders = new List<SubShaderNode>();
            string fallback = null;
            bool fallbackDisabledExplicitly = false;
            string customEditor = null;
            Dictionary<string, string> dependencies = new Dictionary<string, string>();

            // Keep track of commands inherited by categories as we parse.
            // We essentially pretend categories don't exist, since they are a niche feature.
            Stack<List<ShaderLabCommandNode>> categoryCommands = new Stack<List<ShaderLabCommandNode>>();

            while (LoopShouldContinue())
            {
                ParseIncludeBlocksIfPresent(includeBlocks);
                SetIncludes(includeBlocks);

                // If we are in a category, put the commands there
                if (categoryCommands.Count > 0)
                    ParseCommandsIfPresent(categoryCommands.Peek());

                SLToken next = Peek();
                if (next.Kind == TokenKind.CloseBraceToken)
                    break;

                switch (next.Kind)
                {
                    case TokenKind.SubShaderKeyword:
                        var subShader = ParseSubShader();
                        subShader.Commands.AddRange(categoryCommands.SelectMany(x => x));
                        subshaders.Add(subShader);
                        break;
                    case TokenKind.FallbackKeyword:
                        Advance();
                        if (Match(TokenKind.OffKeyword, TokenKind.FalseKeyword))
                        {
                            fallbackDisabledExplicitly = true;
                            Advance();
                        }
                        else
                        {
                            fallback = Eat(TokenKind.StringLiteralToken).Identifier ?? string.Empty;
                        }
                        break;
                    case TokenKind.DependencyKeyword:
                        Advance();
                        string key = ParseStringLiteral();
                        Eat(TokenKind.EqualsToken);
                        string val = ParseStringLiteral();
                        dependencies[key] = val;
                        break;
                    case TokenKind.CustomEditorKeyword:
                        Advance();
                        customEditor = Eat(TokenKind.StringLiteralToken).Identifier ?? string.Empty;
                        break;
                    case TokenKind.CategoryKeyword:
                        Advance();
                        Eat(TokenKind.OpenBraceToken);
                        categoryCommands.Push(new List<ShaderLabCommandNode>());
                        break;
                    case TokenKind.CloseBraceToken when categoryCommands.Count > 0:
                        Advance();
                        categoryCommands.Pop();
                        break;
                    default:
                        Advance();
                        Error($"SubShader, Fallback, Dependency or CustomEditor", next);
                        break;
                }
            }

            ParseIncludeBlocksIfPresent(includeBlocks);

            var closeTok = Eat(TokenKind.CloseBraceToken);
            RecoverTo(TokenKind.CloseBraceToken);

            PopIncludes();

            return new ShaderNode(Range(keywordTok, closeTok))
            {
                Name = name,
                Properties = properties,
                SubShaders = subshaders,
                Fallback = fallback,
                FallbackDisabledExplicitly = fallbackDisabledExplicitly,
                CustomEditor = customEditor,
                Dependencies = dependencies,
                IncludeBlocks = includeBlocks,
            };
        }

        public void ParseIncludeBlocksIfPresent(List<HLSLIncludeBlock> outIncludeBlocks)
        {
            while (true)
            {
                SLToken next = Peek();
                if ((next.Kind == TokenKind.CgIncludeBlock || next.Kind == TokenKind.HlslIncludeBlock || next.Kind == TokenKind.GlslIncludeBlock)
                    && !string.IsNullOrEmpty(next.Identifier))
                {
                    ProgramKind kind = ProgramKind.Cg;
                    if (next.Kind == TokenKind.HlslProgramBlock)
                        kind = ProgramKind.Hlsl;
                    else if (next.Kind == TokenKind.GlslProgramBlock)
                        kind = ProgramKind.Glsl;

                    outIncludeBlocks.Add(new HLSLIncludeBlock { Span = next.Span, Code = next.Identifier, Kind = kind });
                    Advance();
                }
                else
                {
                    break;
                }
            }
        }

        // TODO: Actually parse contents. In rare cases it can matter.
        public string ParseBracketedStringLiteral()
        {
            SLToken literalToken = Eat(TokenKind.BracketedStringLiteralToken);
            string literal = literalToken.Identifier ?? string.Empty;
            if (string.IsNullOrEmpty(literal))
                Error("a valid bracketed string literal / property reference", literalToken);
            return literal;
        }

        public void ParsePropertySection(List<ShaderPropertyNode> outProperties)
        {
            Eat(TokenKind.PropertiesKeyword);
            Eat(TokenKind.OpenBraceToken);

            while (Match(TokenKind.IdentifierToken, TokenKind.BracketedStringLiteralToken))
            {
                outProperties.Add(ParseProperty());
            }

            Eat(TokenKind.CloseBraceToken);
            RecoverTo(TokenKind.CloseBraceToken);
        }

        public ShaderPropertyNode ParseProperty()
        {
            var firstTok = Peek();
            List<string> attributes = new List<string>();
            while (Match(TokenKind.BracketedStringLiteralToken))
            {
                attributes.Add(ParseBracketedStringLiteral());
            }

            string uniform = ParseIdentifier();

            Eat(TokenKind.OpenParenToken);

            string name = ParseStringLiteral();
            Eat(TokenKind.CommaToken);

            ShaderPropertyKind kind = ShaderPropertyKind.None;
            (float Min, float Max)? rangeMinMax = null;
            SLToken typeToken = Advance();
            switch (typeToken.Kind)
            {
                case TokenKind.FloatKeyword: kind = ShaderPropertyKind.Float; break;
                case TokenKind.IntegerKeyword: kind = ShaderPropertyKind.Integer; break;
                case TokenKind.IntKeyword: kind = ShaderPropertyKind.Int; break;
                case TokenKind.ColorKeyword: kind = ShaderPropertyKind.Color; break;
                case TokenKind.VectorKeyword: kind = ShaderPropertyKind.Vector; break;
                case TokenKind._2DKeyword: case TokenKind.RectKeyword: kind = ShaderPropertyKind.Texture2D; break;
                case TokenKind._3DKeyword: kind = ShaderPropertyKind.Texture3D; break;
                case TokenKind.CubeKeyword: kind = ShaderPropertyKind.TextureCube; break;
                case TokenKind._2DArrayKeyword: kind = ShaderPropertyKind.Texture2DArray; break;
                case TokenKind._3DArrayKeyword: kind = ShaderPropertyKind.Texture3DArray; break;
                case TokenKind.CubeArrayKeyword: kind = ShaderPropertyKind.TextureCubeArray; break;
                case TokenKind.AnyKeyword: kind = ShaderPropertyKind.TextureAny; break;
                case TokenKind.RangeKeyword:
                    kind = ShaderPropertyKind.Range;
                    Eat(TokenKind.OpenParenToken);
                    float min = ParseNumericLiteral();
                    Eat(TokenKind.CommaToken);
                    float max = ParseNumericLiteral();
                    Eat(TokenKind.CloseParenToken);
                    rangeMinMax = (min, max);
                    break;
                default:
                    Error("a valid type", typeToken);
                    break;
            }

            Eat(TokenKind.CloseParenToken);

            Eat(TokenKind.EqualsToken);

            var valueNodeFirstTok = Peek();
            ShaderPropertyValueNode valueNode = null;
            switch (kind)
            {
                case ShaderPropertyKind.Color:
                case ShaderPropertyKind.Vector:
                    Eat(TokenKind.OpenParenToken);
                    float x = ParseNumericLiteral();
                    Eat(TokenKind.CommaToken);
                    float y = ParseNumericLiteral();
                    Eat(TokenKind.CommaToken);
                    float z = ParseNumericLiteral();
                    float w = 1;
                    bool hasLastChannel = false;
                    if (Match(TokenKind.CommaToken))
                    {
                        Eat(TokenKind.CommaToken);
                        w = ParseNumericLiteral();
                        hasLastChannel = true;
                    }
                    var closeTok = Eat(TokenKind.CloseParenToken);
                    if (kind == ShaderPropertyKind.Color)
                        valueNode = new ShaderPropertyValueColorNode(Range(valueNodeFirstTok, closeTok)) { HasAlphaChannel = hasLastChannel, Color = (x, y, z, w) };
                    else
                        valueNode = new ShaderPropertyValueVectorNode(Range(valueNodeFirstTok, closeTok)) { HasWChannel = hasLastChannel, Vector = (x, y, z, w) };
                    break;

                case ShaderPropertyKind.TextureCube:
                case ShaderPropertyKind.Texture2D:
                case ShaderPropertyKind.Texture3D:
                case ShaderPropertyKind.TextureAny:
                case ShaderPropertyKind.TextureCubeArray:
                case ShaderPropertyKind.Texture2DArray:
                case ShaderPropertyKind.Texture3DArray:
                    string texName = ParseStringLiteral();
                    valueNode = new ShaderPropertyValueTextureNode(Range(valueNodeFirstTok, Previous())) { Kind = ShaderLabSyntaxFacts.ShaderPropertyTypeToTextureType(kind), TextureName = texName };
                    break;

                case ShaderPropertyKind.Integer:
                case ShaderPropertyKind.Int:
                    int intVal = ParseIntegerLiteral();
                    valueNode = new ShaderPropertyValueIntegerNode(Range(valueNodeFirstTok, Previous())) { Number = intVal };
                    break;

                case ShaderPropertyKind.Float:
                case ShaderPropertyKind.Range:
                    float floatVal = ParseNumericLiteral();
                    valueNode = new ShaderPropertyValueFloatNode(Range(valueNodeFirstTok, Previous())) { Number = floatVal };
                    break;

                default:
                    break;
            }

            if (Match(TokenKind.OpenBraceToken))
            {
                Eat(TokenKind.OpenBraceToken);
                while (Peek().Kind != TokenKind.CloseBraceToken)
                    Advance();
                Eat(TokenKind.CloseBraceToken);
            }

            return new ShaderPropertyNode(Range(firstTok, Previous()))
            {
                Attributes = attributes,
                Uniform = uniform,
                Name = name,
                Kind = kind,
                RangeMinMax = rangeMinMax,
                Value = valueNode,
            };
        }

        public SubShaderNode ParseSubShader()
        {
            PushIncludes();

            var keywordTok = Eat(TokenKind.SubShaderKeyword);
            Eat(TokenKind.OpenBraceToken);

            List<ShaderPassNode> passes = new List<ShaderPassNode>();
            List<ShaderLabCommandNode> commands = new List<ShaderLabCommandNode>();
            List<HLSLProgramBlock> programBlocks = new List<HLSLProgramBlock>();
            List<HLSLIncludeBlock> includeBlocks = new List<HLSLIncludeBlock>();

            while (LoopShouldContinue())
            {
                SLToken next = Peek();
                if (next.Kind == TokenKind.CloseBraceToken)
                    break;

                int lastPosition = position;

                switch (next.Kind)
                {
                    case TokenKind.PassKeyword: passes.Add(ParseCodePass()); break;
                    case TokenKind.GrabPassKeyword: passes.Add(ParseGrabPass()); break;
                    case TokenKind.UsePassKeyword: passes.Add(ParseUsePass()); break;
                    case TokenKind.CgProgramBlock:
                    case TokenKind.HlslProgramBlock:
                    case TokenKind.GlslProgramBlock:
                        programBlocks.Add(ParseOrSkipEmbeddedHLSL()); break;
                    default:
                        ParseCommandsAndIncludeBlocksIfPresent(commands, includeBlocks);
                        SetIncludes(includeBlocks);
                        break;
                }

                // We got stuck, so error and try to recover to something sensible
                if (position == lastPosition)
                {
                    Error("a valid ShaderLab command or program block", next);
                    RecoverTo(x => x == TokenKind.CloseBraceToken || commandSyncTokens.Contains(x), false);
                }
            }

            ParseCommandsAndIncludeBlocksIfPresent(commands, includeBlocks);

            var closeTok = Eat(TokenKind.CloseBraceToken);
            RecoverTo(TokenKind.CloseBraceToken);

            PopIncludes();

            return new SubShaderNode(Range(keywordTok, closeTok))
            {
                Passes = passes,
                Commands = commands,
                IncludeBlocks = includeBlocks,
                ProgramBlocks = programBlocks,
            };
        }

        public ShaderCodePassNode ParseCodePass()
        {
            PushIncludes();

            var keywordTok = Eat(TokenKind.PassKeyword);
            Eat(TokenKind.OpenBraceToken);

            List<ShaderLabCommandNode> commands = new List<ShaderLabCommandNode>();
            List<HLSLProgramBlock> programBlocks = new List<HLSLProgramBlock>();
            List<HLSLIncludeBlock> includeBlocks = new List<HLSLIncludeBlock>();

            ParseCommandsAndIncludeBlocksIfPresent(commands, includeBlocks);
            SetIncludes(includeBlocks);

            while (Match(TokenKind.CgProgramBlock, TokenKind.HlslProgramBlock, TokenKind.GlslProgramBlock))
            {
                programBlocks.Add(ParseOrSkipEmbeddedHLSL());
                ParseCommandsAndIncludeBlocksIfPresent(commands, includeBlocks);
                SetIncludes(includeBlocks);
            }

            var closeTok = Eat(TokenKind.CloseBraceToken);
            RecoverTo(TokenKind.CloseBraceToken);

            PopIncludes();

            return new ShaderCodePassNode(Range(keywordTok, closeTok))
            {
                ProgramBlocks = programBlocks,
                Commands = commands,
                IncludeBlocks = includeBlocks
            };
        }

        public ShaderGrabPassNode ParseGrabPass()
        {
            var keywordTok = Eat(TokenKind.GrabPassKeyword);
            Eat(TokenKind.OpenBraceToken);

            List<ShaderLabCommandNode> commands = new List<ShaderLabCommandNode>();
            List<HLSLIncludeBlock> includeBlocks = new List<HLSLIncludeBlock>();

            ParseCommandsAndIncludeBlocksIfPresent(commands, includeBlocks);
            string name = null;
            if (Peek().Kind != TokenKind.CloseBraceToken)
            {
                name = ParseStringLiteral();

                ParseCommandsAndIncludeBlocksIfPresent(commands, includeBlocks);
            }

            var closeTok = Eat(TokenKind.CloseBraceToken);
            RecoverTo(TokenKind.CloseBraceToken);

            return new ShaderGrabPassNode(Range(keywordTok, closeTok))
            {
                TextureName = name,
                Commands = commands,
                IncludeBlocks = includeBlocks,
            };
        }

        public ShaderUsePassNode ParseUsePass()
        {
            var keywordTok = Eat(TokenKind.UsePassKeyword);
            string name = ParseStringLiteral();
            return new ShaderUsePassNode(Range(keywordTok, Previous()))
            {
                PassName = name
            };
        }

        public void ParseCommandsAndIncludeBlocksIfPresent(List<ShaderLabCommandNode> outCommands, List<HLSLIncludeBlock> outIncludeBlocks)
        {
            while (true)
            {
                int lastPosition = position;

                ParseCommandsIfPresent(outCommands);
                ParseIncludeBlocksIfPresent(outIncludeBlocks);

                if (lastPosition == position)
                    break;
            }
        }

        public bool TryParseCommand(out ShaderLabCommandNode result)
        {
            var next = Peek();
            switch (next.Kind)
            {
                case TokenKind.LightingKeyword: result = ParseBasicToggleCommand(next.Kind, (a, b) => new ShaderLabCommandLightingNode(Range(a, b))); return true;
                case TokenKind.SeparateSpecularKeyword: result = ParseBasicToggleCommand(next.Kind, (a, b) => new ShaderLabCommandSeparateSpecularNode(Range(a, b))); return true;
                case TokenKind.ZWriteKeyword: result = ParseBasicToggleCommand(next.Kind, (a, b) => new ShaderLabCommandZWriteNode(Range(a, b))); return true;
                case TokenKind.AlphaToMaskKeyword: result = ParseBasicToggleCommand(next.Kind, (a, b) => new ShaderLabCommandAlphaToMaskNode(Range(a, b))); return true;
                case TokenKind.ZClipKeyword: result = ParseBasicToggleCommand(next.Kind, (a, b) => new ShaderLabCommandZClipNode(Range(a, b))); return true;
                case TokenKind.ConservativeKeyword: result = ParseBasicToggleCommand(next.Kind, (a, b) => new ShaderLabCommandConservativeNode(Range(a, b))); return true;
                case TokenKind.TagsKeyword: result = ParseTagsCommand(); return true;
                case TokenKind.LodKeyword: result = ParseLodCommand(); return true;
                case TokenKind.CullKeyword: result = ParseCullCommand(); return true;
                case TokenKind.ZTestKeyword: result = ParseZTestCommand(); return true;
                case TokenKind.BlendKeyword: result = ParseBlendCommand(); return true;
                case TokenKind.OffsetKeyword: result = ParseOffsetCommand(); return true;
                case TokenKind.ColorMaskKeyword: result = ParseColorMaskCommand(); return true;
                case TokenKind.AlphaTestKeyword: result = ParseAlphaTestCommand(); return true;
                case TokenKind.FogKeyword: result = ParseFogCommand(); return true;
                case TokenKind.NameKeyword: result = ParseNameCommand(); return true;
                case TokenKind.BindChannelsKeyword: result = ParseBindChannelsCommand(); return true;
                case TokenKind.ColorKeyword: result = ParseColorCommand(); return true;
                case TokenKind.BlendOpKeyword: result = ParseBlendOpCommand(); return true;
                case TokenKind.MaterialKeyword: result = ParseMaterialCommand(); return true;
                case TokenKind.SetTextureKeyword: result = ParseSetTextureCommand(); return true;
                case TokenKind.ColorMaterialKeyword: result = ParseColorMaterialNode(); return true;
                case TokenKind.StencilKeyword: result = ParseStencilNode(); return true;
                case TokenKind.PackageRequirementsKeyword: result = ParsePackageRequirementsNode(); return true;
                default: result = null; return false;
            }
        }

        public void ParseCommandsIfPresent(List<ShaderLabCommandNode> outCommands)
        {
            bool run = true;
            while (run)
            {
                if (TryParseCommand(out var command))
                {
                    outCommands.Add(command);
                }
                else
                {
                    run = false;
                }

                // If we encountered an error, try to find the next command.
                RecoverTo(kind => commandSyncTokens.Contains(kind), false);
            }
        }

        public T ParseBasicToggleCommand<T>(TokenKind keyword, Func<SLToken, SLToken, T> ctor)
            where T : ShaderLabBasicToggleCommandNode
        {
            var firstTok = Eat(keyword);
            var prop = ParsePropertyReferenceOr(() =>
            {
                var kind = Eat(TokenKind.OnKeyword, TokenKind.OffKeyword, TokenKind.TrueKeyword, TokenKind.FalseKeyword).Kind;
                return kind == TokenKind.OnKeyword || kind == TokenKind.TrueKeyword;
            });
            var lastTok = Previous();
            var result = ctor(firstTok, lastTok);
            result.Enabled = prop;
            return result;
        }

        public ShaderLabCommandTagsNode ParseTagsCommand()
        {
            var keywordTok = Eat(TokenKind.TagsKeyword);
            Eat(TokenKind.OpenBraceToken);

            Dictionary<string, string> tags = new Dictionary<string, string>();
            while (LoopShouldContinue() && Peek().Kind != TokenKind.CloseBraceToken)
            {
                var tagKeySpan = Peek().Span;
                string key = ParseStringLiteral();
                Eat(TokenKind.EqualsToken);
                string val = ParseStringLiteral();

                if (tags.ContainsKey(key))
                {
                    Error(DiagnosticFlags.Warning, $"Duplicate definition of tag '{key}' found.", tagKeySpan);
                }
                tags[key] = val;
            }

            var closeTok = Eat(TokenKind.CloseBraceToken);
            RecoverTo(TokenKind.CloseBraceToken);

            return new ShaderLabCommandTagsNode(Range(keywordTok, closeTok))
            {
                Tags = tags
            };
        }

        public ShaderLabCommandLodNode ParseLodCommand()
        {
            var keywordTok = Eat(TokenKind.LodKeyword);
            int level = ParseIntegerLiteral();
            return new ShaderLabCommandLodNode(Range(keywordTok, Previous()))
            {
                LodLevel = level,
            };
        }

        public PropertyReferenceOr<TOther> ParsePropertyReferenceOr<TOther>(Func<TOther> otherParser)
        {
            if (Match(TokenKind.BracketedStringLiteralToken))
            {
                return new PropertyReferenceOr<TOther> { Property = ParseBracketedStringLiteral() };
            }
            else
            {
                return new PropertyReferenceOr<TOther> { Value = otherParser() };
            }
        }

        public ShaderLabCommandCullNode ParseCullCommand()
        {
            var keywordTok = Eat(TokenKind.CullKeyword);
            var prop = ParsePropertyReferenceOr(() =>
            {
                var kind = Eat(TokenKind.OffKeyword, TokenKind.FrontKeyword, TokenKind.BackKeyword, TokenKind.FalseKeyword).Kind;
                CullMode mode = default;
                if (kind == TokenKind.OffKeyword || kind == TokenKind.FalseKeyword)
                    mode = CullMode.Off;
                else if (kind == TokenKind.FrontKeyword)
                    mode = CullMode.Front;
                else if (kind == TokenKind.BackKeyword)
                    mode = CullMode.Back;
                return mode;
            });
            return new ShaderLabCommandCullNode(Range(keywordTok, Previous())) { Mode = prop };
        }

        public ShaderLabCommandZTestNode ParseZTestCommand()
        {
            var keywordTok = Eat(TokenKind.ZTestKeyword);
            var prop = ParsePropertyReferenceOr(() => ParseEnum<ComparisonMode>("a valid comparison operator"));
            return new ShaderLabCommandZTestNode(Range(keywordTok, Previous())) { Mode = prop };
        }

        private static readonly Dictionary<TokenKind, BlendFactor> blendFactors = new Dictionary<TokenKind, BlendFactor>()
        {
            { TokenKind.OneKeyword, BlendFactor.One },
            { TokenKind.ZeroKeyword, BlendFactor.Zero },
            { TokenKind.SrcColorKeyword, BlendFactor.SrcColor },
            { TokenKind.SrcAlphaKeyword, BlendFactor.SrcAlpha },
            { TokenKind.SrcAlphaSaturateKeyword, BlendFactor.SrcAlphaSaturate },
            { TokenKind.DstColorKeyword, BlendFactor.DstColor },
            { TokenKind.DstAlphaKeyword, BlendFactor.DstAlpha },
            { TokenKind.OneMinusSrcColorKeyword, BlendFactor.OneMinusSrcColor },
            { TokenKind.OneMinusSrcAlphaKeyword, BlendFactor.OneMinusSrcAlpha },
            { TokenKind.OneMinusDstColorKeyword, BlendFactor.OneMinusDstColor },
            { TokenKind.OneMinusDstAlphaKeyword, BlendFactor.OneMinusDstAlpha }
        };
        private static readonly TokenKind[] blendFactorsKeys = blendFactors.Keys.ToArray();

        private static U GetValueOrDefault<T, U>(Dictionary<T, U> dictionary, T key)
        {
            if (dictionary.TryGetValue(key, out U result))
                return result;
            else
                return default;
        }

        public ShaderLabCommandBlendNode ParseBlendCommand()
        {
            var keywordTok = Eat(TokenKind.BlendKeyword);

            int renderTarget = 0;
            if (Match(TokenKind.FloatLiteralToken, TokenKind.IntegerLiteralToken))
            {
                renderTarget = ParseIntegerLiteral();
            }

            if (Match(TokenKind.OffKeyword, TokenKind.FalseKeyword))
            {
                var offTok = Advance();
                return new ShaderLabCommandBlendNode(Range(keywordTok, offTok)) { RenderTarget = renderTarget, Enabled = false };
            }

            var srcRGB = ParsePropertyReferenceOr(() => GetValueOrDefault(blendFactors, Eat(blendFactorsKeys).Kind));
            var dstRGB = ParsePropertyReferenceOr(() => GetValueOrDefault(blendFactors, Eat(blendFactorsKeys).Kind));

            var srcAlpha = srcRGB;
            var dstAlpha = dstRGB;
            if (Match(TokenKind.CommaToken))
            {
                Eat(TokenKind.CommaToken);
                srcAlpha = ParsePropertyReferenceOr(() => GetValueOrDefault(blendFactors, Eat(blendFactorsKeys).Kind));
                dstAlpha = ParsePropertyReferenceOr(() => GetValueOrDefault(blendFactors, Eat(blendFactorsKeys).Kind));
            }

            return new ShaderLabCommandBlendNode(Range(keywordTok, Previous()))
            {
                RenderTarget = renderTarget,
                Enabled = true,
                SourceFactorRGB = srcRGB,
                DestinationFactorRGB = dstRGB,
                SourceFactorAlpha = srcAlpha,
                DestinationFactorAlpha = dstAlpha
            };
        }

        public ShaderLabCommandOffsetNode ParseOffsetCommand()
        {
            var keywordTok = Eat(TokenKind.OffsetKeyword);
            var factor = ParsePropertyReferenceOr(ParseNumericLiteral);
            Eat(TokenKind.CommaToken);
            var units = ParsePropertyReferenceOr(ParseNumericLiteral);
            return new ShaderLabCommandOffsetNode(Range(keywordTok, Previous())) { Factor = factor, Units = units };
        }

        public ShaderLabCommandColorMaskNode ParseColorMaskCommand()
        {
            var keywordTok = Eat(TokenKind.ColorMaskKeyword);
            var mask = ParsePropertyReferenceOr(() =>
            {
                SLToken next = Peek();
                if (next.Kind == TokenKind.FloatLiteralToken || next.Kind == TokenKind.IntegerLiteralToken)
                {
                    string result = ParseNumericLiteral().ToString();
                    if (result != "0")
                        Error("the numeric literal 0", next);
                    return result;
                }
                else
                {
                    string result = ParseIdentifier();
                    if (!result.ToLower().All(x => x == 'r' || x == 'g' || x == 'b' || x == 'a'))
                        Error("a valid mask containing only the letter 'r', 'g', 'b', 'a'", next);
                    return result;
                }
            });
            int renderTarget = 0;
            if (Match(TokenKind.FloatLiteralToken, TokenKind.IntegerLiteralToken))
            {
                renderTarget = ParseIntegerLiteral();
            }
            return new ShaderLabCommandColorMaskNode(Range(keywordTok, Previous())) { RenderTarget = renderTarget, Mask = mask };
        }

        public ShaderLabCommandAlphaTestNode ParseAlphaTestCommand()
        {
            var keywordTok = Eat(TokenKind.AlphaTestKeyword);
            var prop = ParsePropertyReferenceOr(() => ParseEnum<ComparisonMode>("a valid comparison operator"));
            PropertyReferenceOr<float>? alpha = null;
            if (Match(TokenKind.FloatLiteralToken, TokenKind.IntegerLiteralToken, TokenKind.BracketedStringLiteralToken))
            {
                alpha = ParsePropertyReferenceOr(ParseNumericLiteral);
            }
            return new ShaderLabCommandAlphaTestNode(Range(keywordTok, Previous())) { Mode = prop, AlphaValue = alpha };
        }

        public void ParseColor(out (float r, float g, float b, float a) color, out bool hasAlphaChannel)
        {
            hasAlphaChannel = false;
            float r, g, b, a = 1;
            Eat(TokenKind.OpenParenToken);
            r = ParseNumericLiteral();
            Eat(TokenKind.CommaToken);
            g = ParseNumericLiteral();
            Eat(TokenKind.CommaToken);
            b = ParseNumericLiteral();
            if (Match(TokenKind.CommaToken))
            {
                Eat(TokenKind.CommaToken);
                a = ParseNumericLiteral();
                hasAlphaChannel = true;
            }
            Eat(TokenKind.CloseParenToken);
            color = (r, g, b, a);
        }

        public ShaderLabCommandFogNode ParseFogCommand()
        {
            var keywordTok = Eat(TokenKind.FogKeyword);
            Eat(TokenKind.OpenBraceToken);

            (float, float, float, float)? color = null;
            bool isEnabled;
            if (Match(TokenKind.ColorKeyword))
            {
                isEnabled = true;

                Eat(TokenKind.ColorKeyword);
                ParseColor(out var parsedColor, out _);
                color = parsedColor;
            }
            else
            {
                Eat(TokenKind.ModeKeyword);

                TokenKind modeKind = Eat(TokenKind.OffKeyword, TokenKind.GlobalKeyword).Kind;
                isEnabled = modeKind == TokenKind.GlobalKeyword;
            }

            var closeTok = Eat(TokenKind.CloseBraceToken);
            RecoverTo(TokenKind.CloseBraceToken);
            return new ShaderLabCommandFogNode(Range(keywordTok, closeTok)) { Enabled = isEnabled, Color = color };
        }

        public ShaderLabCommandNameNode ParseNameCommand()
        {
            var keywordTok = Eat(TokenKind.NameKeyword);
            string name = ParseStringLiteral();
            return new ShaderLabCommandNameNode(Range(keywordTok, Previous())) { Name = name };
        }

        public ShaderLabCommandBindChannelsNode ParseBindChannelsCommand()
        {
            var keywordTok = Eat(TokenKind.BindChannelsKeyword);
            Eat(TokenKind.OpenBraceToken);

            Dictionary<BindChannel, BindChannel> bindings = new Dictionary<BindChannel, BindChannel>();
            while (LoopShouldContinue() && Peek().Kind != TokenKind.CloseBraceToken)
            {
                Eat(TokenKind.BindKeyword);
                string source = ParseStringLiteral();
                Eat(TokenKind.CommaToken);
                SLToken targetToken = Advance();
                // Handle ShaderLab's ambiguous syntax: Could be a keyword or an identifier here, in the case of color.
                string target = targetToken.Kind == TokenKind.ColorKeyword ? "color" : targetToken.Identifier ?? String.Empty;
                if (ShaderLabSyntaxFacts.TryParseBindChannelName(source, out BindChannel sourceChannel) &&
                    ShaderLabSyntaxFacts.TryParseBindChannelName(target, out BindChannel targetChannel))
                {
                    bindings[sourceChannel] = targetChannel;
                }
                else
                {
                    Error(DiagnosticFlags.SemanticError, $"Failed to parse channel binding from '{source}' to '{target}'.");
                }
            }

            var closeTok = Eat(TokenKind.CloseBraceToken);
            RecoverTo(TokenKind.CloseBraceToken);

            return new ShaderLabCommandBindChannelsNode(Range(keywordTok, closeTok)) { Bindings = bindings };
        }

        public ShaderLabCommandColorNode ParseColorCommand()
        {
            var keywordTok = Eat(TokenKind.ColorKeyword);
            bool hasAlphaChannel = false;
            var prop = ParsePropertyReferenceOr(() =>
            {
                ParseColor(out var color, out hasAlphaChannel);
                return color;
            });
            return new ShaderLabCommandColorNode(Range(keywordTok, Previous())) { Color = prop, HasAlphaChannel = hasAlphaChannel };
        }

        private static readonly Dictionary<TokenKind, BlendOp> blendOps = new Dictionary<TokenKind, BlendOp>()
        {
            { TokenKind.AddKeyword, BlendOp.Add },
            { TokenKind.SubKeyword, BlendOp.Sub },
            { TokenKind.RevSubKeyword, BlendOp.RevSub },
            { TokenKind.MinKeyword, BlendOp.Min },
            { TokenKind.MaxKeyword, BlendOp.Max },
            { TokenKind.LogicalClearKeyword, BlendOp.LogicalClear },
            { TokenKind.LogicalSetKeyword, BlendOp.LogicalSet },
            { TokenKind.LogicalCopyKeyword, BlendOp.LogicalCopy },
            { TokenKind.LogicalCopyInvertedKeyword, BlendOp.LogicalCopyInverted },
            { TokenKind.LogicalNoopKeyword, BlendOp.LogicalNoop },
            { TokenKind.LogicalInvertKeyword, BlendOp.LogicalInvert },
            { TokenKind.LogicalAndKeyword, BlendOp.LogicalAnd },
            { TokenKind.LogicalNandKeyword, BlendOp.LogicalNand },
            { TokenKind.LogicalOrKeyword, BlendOp.LogicalOr },
            { TokenKind.LogicalNorKeyword, BlendOp.LogicalNor },
            { TokenKind.LogicalXorKeyword, BlendOp.LogicalXor },
            { TokenKind.LogicalEquivKeyword, BlendOp.LogicalEquiv },
            { TokenKind.LogicalAndReverseKeyword, BlendOp.LogicalAndReverse },
            { TokenKind.LogicalOrReverseKeyword, BlendOp.LogicalOrReverse },
            { TokenKind.LogicalOrInvertedKeyword, BlendOp.LogicalOrInverted },
            { TokenKind.MultiplyKeyword, BlendOp.Multiply },
            { TokenKind.ScreenKeyword, BlendOp.Screen },
            { TokenKind.OverlayKeyword, BlendOp.Overlay },
            { TokenKind.DarkenKeyword, BlendOp.Darken },
            { TokenKind.LightenKeyword, BlendOp.Lighten },
            { TokenKind.ColorDodgeKeyword, BlendOp.ColorDodge },
            { TokenKind.ColorBurnKeyword, BlendOp.ColorBurn },
            { TokenKind.HardLightKeyword, BlendOp.HardLight },
            { TokenKind.SoftLightKeyword, BlendOp.SoftLight },
            { TokenKind.DifferenceKeyword, BlendOp.Difference },
            { TokenKind.ExclusionKeyword, BlendOp.Exclusion },
            { TokenKind.HSLHueKeyword, BlendOp.HSLHue },
            { TokenKind.HSLSaturationKeyword, BlendOp.HSLSaturation },
            { TokenKind.HSLColorKeyword, BlendOp.HSLColor },
            { TokenKind.HSLLuminosityKeyword, BlendOp.HSLLuminosity },
        };
        private static readonly TokenKind[] blendOpsKeys = blendOps.Keys.ToArray();
        public ShaderLabCommandBlendOpNode ParseBlendOpCommand()
        {
            var keywordTok = Eat(TokenKind.BlendOpKeyword);
            var op = ParsePropertyReferenceOr(() => GetValueOrDefault(blendOps, Eat(blendOpsKeys).Kind));
            PropertyReferenceOr<BlendOp>? alphaOp = null;
            if (Match(TokenKind.CommaToken))
            {
                Eat(TokenKind.CommaToken);
                alphaOp = ParsePropertyReferenceOr(() => GetValueOrDefault(blendOps, Eat(blendOpsKeys).Kind));
            }
            return new ShaderLabCommandBlendOpNode(Range(keywordTok, Previous())) { BlendOp = op, BlendOpAlpha = alphaOp };
        }

        private static readonly Dictionary<TokenKind, FixedFunctionMaterialProperty> fixedFunctionsMatProps = new Dictionary<TokenKind, FixedFunctionMaterialProperty>()
        {
            { TokenKind.DiffuseKeyword, FixedFunctionMaterialProperty.Diffuse },
            { TokenKind.SpecularKeyword, FixedFunctionMaterialProperty.Specular },
            { TokenKind.AmbientKeyword, FixedFunctionMaterialProperty.Ambient },
            { TokenKind.EmissionKeyword, FixedFunctionMaterialProperty.Emission },
            { TokenKind.ShininessKeyword, FixedFunctionMaterialProperty.Shininess },
        };
        private static readonly TokenKind[] fixedFunctionsMatPropsKeys = fixedFunctionsMatProps.Keys.ToArray();

        public ShaderLabCommandMaterialNode ParseMaterialCommand()
        {
            var keywordTok = Eat(TokenKind.MaterialKeyword);
            Eat(TokenKind.OpenBraceToken);

            var props = new Dictionary<FixedFunctionMaterialProperty, PropertyReferenceOr<(float, float, float, float)>>();
            while (LoopShouldContinue() && !Match(TokenKind.CloseBraceToken))
            {
                var prop = GetValueOrDefault(fixedFunctionsMatProps, Eat(fixedFunctionsMatPropsKeys).Kind);
                var val = ParsePropertyReferenceOr(() =>
                {
                    ParseColor(out var color, out _);
                    return color;
                });

            }

            var closeTok = Eat(TokenKind.CloseBraceToken);
            RecoverTo(TokenKind.CloseBraceToken);

            return new ShaderLabCommandMaterialNode(Range(keywordTok, closeTok)) { Properties = props };
        }

        public ShaderLabCommandSetTextureNode ParseSetTextureCommand()
        {
            var keywordTok = Eat(TokenKind.SetTextureKeyword);
            string name = ParseBracketedStringLiteral();
            Eat(TokenKind.OpenBraceToken);

            List<SLToken> tokens = new List<SLToken>();
            while (LoopShouldContinue() && !Match(TokenKind.CloseBraceToken))
            {
                tokens.Add(Advance());
            }

            var closeTok = Eat(TokenKind.CloseBraceToken);
            RecoverTo(TokenKind.CloseBraceToken);

            return new ShaderLabCommandSetTextureNode(Range(keywordTok, closeTok)) { TextureName = name, Body = tokens };
        }

        public ShaderLabCommandColorMaterialNode ParseColorMaterialNode()
        {
            var keywordTok = Eat(TokenKind.ColorMaterialKeyword);
            var modeTok = Eat(TokenKind.EmissionKeyword, TokenKind.AmbientAndDiffuseKeyword);
            bool ambient = modeTok.Kind == TokenKind.AmbientAndDiffuseKeyword;
            return new ShaderLabCommandColorMaterialNode(Range(keywordTok, modeTok)) { AmbientAndDiffuse = ambient };
        }

        public ShaderLabCommandStencilNode ParseStencilNode()
        {
            var keywordTok = Eat(TokenKind.StencilKeyword);
            Eat(TokenKind.OpenBraceToken);

            // Set defaults
            var @ref = new PropertyReferenceOr<byte> { Value = 0 };
            var readMask = new PropertyReferenceOr<byte> { Value = 255 };
            var writeMask = new PropertyReferenceOr<byte> { Value = 255 };
            var comparisonOperationBack = new PropertyReferenceOr<ComparisonMode> { Value = ComparisonMode.Always };
            var passOperationBack = new PropertyReferenceOr<StencilOp> { Value = StencilOp.Keep };
            var failOperationBack = new PropertyReferenceOr<StencilOp> { Value = StencilOp.Keep };
            var zFailOperationBack = new PropertyReferenceOr<StencilOp> { Value = StencilOp.Keep };
            var comparisonOperationFront = new PropertyReferenceOr<ComparisonMode> { Value = ComparisonMode.Always };
            var passOperationFront = new PropertyReferenceOr<StencilOp> { Value = StencilOp.Keep };
            var failOperationFront = new PropertyReferenceOr<StencilOp> { Value = StencilOp.Keep };
            var zFailOperationFront = new PropertyReferenceOr<StencilOp> { Value = StencilOp.Keep };

            StencilOp ParseStencilOp() => ParseEnum<StencilOp>("a valid stencil operator");
            ComparisonMode ParseComparisonMode() => ParseEnum<ComparisonMode>("a valid stencil comparison operator");

            while (LoopShouldContinue() && !Match(TokenKind.CloseBraceToken))
            {
                SLToken next = Advance();
                switch (next.Kind)
                {
                    case TokenKind.RefKeyword: @ref = ParsePropertyReferenceOr(ParseByteLiteral); break;
                    case TokenKind.ReadMaskKeyword: readMask = ParsePropertyReferenceOr(ParseByteLiteral); break;
                    case TokenKind.WriteMaskKeyword: writeMask = ParsePropertyReferenceOr(ParseByteLiteral); break;
                    case TokenKind.CompKeyword: comparisonOperationBack = comparisonOperationFront = ParsePropertyReferenceOr(ParseComparisonMode); break;
                    case TokenKind.PassKeyword: passOperationBack = passOperationFront = ParsePropertyReferenceOr(ParseStencilOp); break;
                    case TokenKind.FailKeyword: failOperationBack = failOperationFront = ParsePropertyReferenceOr(ParseStencilOp); break;
                    case TokenKind.ZFailKeyword: zFailOperationBack = zFailOperationFront = ParsePropertyReferenceOr(ParseStencilOp); break;
                    case TokenKind.CompBackKeyword: comparisonOperationBack = ParsePropertyReferenceOr(ParseComparisonMode); break;
                    case TokenKind.PassBackKeyword: passOperationBack = ParsePropertyReferenceOr(ParseStencilOp); break;
                    case TokenKind.FailBackKeyword: failOperationBack = ParsePropertyReferenceOr(ParseStencilOp); break;
                    case TokenKind.ZFailBackKeyword: zFailOperationBack = ParsePropertyReferenceOr(ParseStencilOp); break;
                    case TokenKind.CompFrontKeyword: comparisonOperationFront = ParsePropertyReferenceOr(ParseComparisonMode); break;
                    case TokenKind.PassFrontKeyword: passOperationFront = ParsePropertyReferenceOr(ParseStencilOp); break;
                    case TokenKind.FailFrontKeyword: failOperationFront = ParsePropertyReferenceOr(ParseStencilOp); break;
                    case TokenKind.ZFailFrontKeyword: zFailOperationFront = ParsePropertyReferenceOr(ParseStencilOp); break;

                    default:
                        Error("a valid stencil operation", next);
                        break;
                }
            }

            var closeTok = Eat(TokenKind.CloseBraceToken);
            RecoverTo(TokenKind.CloseBraceToken);

            return new ShaderLabCommandStencilNode(Range(keywordTok, closeTok))
            {
                Ref = @ref,
                ReadMask = readMask,
                WriteMask = writeMask ,
                ComparisonOperationBack = comparisonOperationBack ,
                PassOperationBack = passOperationBack ,
                FailOperationBack = failOperationBack ,
                ZFailOperationBack = zFailOperationBack ,
                ComparisonOperationFront = comparisonOperationFront ,
                PassOperationFront = passOperationFront ,
                FailOperationFront = failOperationFront ,
                ZFailOperationFront = zFailOperationFront ,
            };
        }

        public ShaderLabCommandPackageRequirementsNode ParsePackageRequirementsNode()
        {
            var keywordTok = Eat(TokenKind.PackageRequirementsKeyword);
            Eat(TokenKind.OpenBraceToken);

            var references = new Dictionary<string, string>();
            while (LoopShouldContinue() && !Match(TokenKind.CloseBraceToken))
            {
                string package = ParseStringLiteral();
                string version = null;
                if (Match(TokenKind.ColonToken))
                {
                    Advance();
                    version = ParseStringLiteral();
                }
                references[package] = version;
            }

            var closeTok = Eat(TokenKind.CloseBraceToken);

            return new ShaderLabCommandPackageRequirementsNode(Range(keywordTok, closeTok))
            {
                References = references
            };
        }
    }
}
