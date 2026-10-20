using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Illusion.ShaderTools.Syntax.Common;
using Illusion.ShaderTools.Syntax.HLSL.PreProcessor;

namespace Illusion.ShaderTools.Syntax.HLSL
{
    using HLSLToken = Token<TokenKind>;

    public class HLSLParserConfig
    {
        /// <summary>
        /// Which mode to use for preprocessing the HLSL code before parsing.
        /// </summary>
        public PreProcessorMode PreProcessorMode { get; set; }

        /// <summary>
        /// The base path to use when resolving include directives, and for constructing token spans.
        /// </summary>
        public string BasePath { get; set; }

        /// <summary>
        /// The file name to use when constructing token spans.
        /// </summary>
        public string FileName { get; set; }

        /// <summary>
        /// Use this provide custom logic for resolving include directives.
        /// </summary>
        public IPreProcessorIncludeResolver IncludeResolver { get; set; }

        /// <summary>
        /// Custom pre-defined macros to feed to the preprocessor.
        /// </summary>
        public Dictionary<string, string> Defines { get; set; }

        /// <summary>
        /// Whether to throw an exception when an error is encountered during parsing, or to attempt to keep parsing.
        /// </summary>
        public bool ThrowExceptionOnError { get; set; }

        /// <summary>
        /// Which diagnostic messages to output.
        /// </summary>
        public DiagnosticFlags DiagnosticFilter { get; set; }

        public HLSLParserConfig()
        {
            PreProcessorMode = PreProcessorMode.ExpandAll;
            BasePath = Directory.GetCurrentDirectory();
            FileName = null;
            IncludeResolver = new DefaultPreProcessorIncludeResolver();
            Defines = new Dictionary<string, string>();
            ThrowExceptionOnError = false;
            DiagnosticFilter = DiagnosticFlags.All;
        }

        public HLSLParserConfig(HLSLParserConfig config)
        {
            PreProcessorMode = config.PreProcessorMode;
            BasePath = config.BasePath;
            FileName = config.FileName;
            IncludeResolver = config.IncludeResolver;
            Defines = config.Defines;
            ThrowExceptionOnError = config.ThrowExceptionOnError;
            DiagnosticFilter = config.DiagnosticFilter;
        }
    }

    public class HLSLParser : BaseParser<TokenKind>
    {
        private HLSLToken[] syntaxTokens;

        protected new IList<HLSLToken> Range(HLSLToken first, HLSLToken last)
        {
            if (first == null || last == null) return Array.Empty<HLSLToken>();
            if (syntaxTokens == null) syntaxTokens = tokens.ToArray();
            int count = Math.Max(0, last.Position - first.Position + 1);
            count = Math.Min(count, syntaxTokens.Length - first.Position);
            return new ArraySegment<HLSLToken>(syntaxTokens, first.Position, count);
        }

        public HLSLParser(List<HLSLToken> tokens, bool throwExceptionOnError, DiagnosticFlags diagnosticFilter)
            : base(tokens, throwExceptionOnError, diagnosticFilter)
        {
            InitOperatorGroups();
        }

        protected override TokenKind StringLiteralTokenKind => TokenKind.StringLiteralToken;
        protected override TokenKind IntegerLiteralTokenKind => TokenKind.IntegerLiteralToken;
        protected override TokenKind FloatLiteralTokenKind => TokenKind.FloatLiteralToken;
        protected override TokenKind IdentifierTokenKind => TokenKind.IdentifierToken;
        protected override TokenKind InvalidTokenKind => TokenKind.InvalidToken;
        protected override ParserStage Stage => ParserStage.HLSLParsing;

        public static List<HLSLSyntaxNode> ParseTopLevelDeclarations(List<HLSLToken> tokens, HLSLParserConfig config, out List<Diagnostic> diagnostics, out List<string> pragmas)
        {
            HLSLParser parser = new HLSLParser(tokens, config.ThrowExceptionOnError, config.DiagnosticFilter);
            parser.RunPreProcessor(config, out pragmas);
            var result = parser.ParseTopLevelDeclarations();
            foreach (var decl in result)
            {
                decl.ComputeParents();
            }
            diagnostics = parser.diagnostics;
            return result;
        }

        public static HLSLSyntaxNode ParseTopLevelDeclaration(List<HLSLToken> tokens, HLSLParserConfig config, out List<Diagnostic> diagnostics, out List<string> pragmas)
        {
            HLSLParser parser = new HLSLParser(tokens, config.ThrowExceptionOnError, config.DiagnosticFilter);
            parser.RunPreProcessor(config, out pragmas);
            var result = parser.ParseTopLevelDeclaration();
            result.ComputeParents();
            diagnostics = parser.diagnostics;
            return result;
        }

        public static List<StatementNode> ParseStatements(List<HLSLToken> tokens, HLSLParserConfig config, out List<Diagnostic> diagnostics, out List<string> pragmas)
        {
            HLSLParser parser = new HLSLParser(tokens, config.ThrowExceptionOnError, config.DiagnosticFilter);
            parser.RunPreProcessor(config, out pragmas);
            var result = parser.ParseMany0(() => parser.LoopShouldContinue() && !parser.Match(TokenKind.EndOfFileToken), () => parser.ParseStatement());
            foreach (var stmt in result)
            {
                stmt.ComputeParents();
            }
            diagnostics = parser.diagnostics;
            return result;
        }

        public static StatementNode ParseStatement(List<HLSLToken> tokens, HLSLParserConfig config, out List<Diagnostic> diagnostics, out List<string> pragmas)
        {
            HLSLParser parser = new HLSLParser(tokens, config.ThrowExceptionOnError, config.DiagnosticFilter);
            parser.RunPreProcessor(config, out pragmas);
            var result = parser.ParseStatement();
            result.ComputeParents();
            diagnostics = parser.diagnostics;
            return result;
        }

        public static ExpressionNode ParseExpression(List<HLSLToken> tokens, HLSLParserConfig config, out List<Diagnostic> diagnostics, out List<string> pragmas)
        {
            HLSLParser parser = new HLSLParser(tokens, config.ThrowExceptionOnError, config.DiagnosticFilter);
            parser.RunPreProcessor(config, out pragmas);
            var result = parser.ParseExpression();
            result.ComputeParents();
            diagnostics = parser.diagnostics;
            return result;
        }

        public void RunPreProcessor(HLSLParserConfig config, out List<string> pragmas)
        {
            syntaxTokens = null;
            if (config.PreProcessorMode == PreProcessorMode.DoNothing)
            {
                pragmas = new List<string>();
                return;
            }

            tokens = HLSLPreProcessor.PreProcess(
                tokens,
                config.ThrowExceptionOnError,
                config.DiagnosticFilter,
                config.PreProcessorMode,
                config.BasePath,
                config.IncludeResolver,
                config.Defines,
                out pragmas,
                out var ppDiags);
            diagnostics.AddRange(ppDiags);
        }

        public List<HLSLSyntaxNode> ParseTopLevelDeclarations()
        {
            List<HLSLSyntaxNode> result = new List<HLSLSyntaxNode>();

            while (LoopShouldContinue() && !Match(TokenKind.EndOfFileToken))
            {
                result.Add(ParseTopLevelDeclaration());
            }

            return result;
        }

        public HLSLSyntaxNode ParseTopLevelDeclaration()
        {
            if (Match(TokenKind.IdentifierToken) && Peek().Identifier == "enum") return ParseEnum();
            if (Match(TokenKind.IdentifierToken) && Peek().Identifier == "_Static_assert")
            {
                var first = Peek();
                var expression = ParseExpression();
                Eat(TokenKind.SemiToken);
                return new ExpressionStatementNode(Range(first, Previous()))
                    { Attributes = new List<AttributeNode>(), Expression = expression };
            }
            switch (Peek().Kind)
            {
                case TokenKind.NamespaceKeyword:
                    return ParseNamespace();

                case TokenKind.CBufferKeyword:
                case TokenKind.TBufferKeyword:
                    return ParseConstantBuffer();

                case TokenKind.StructKeyword:
                case TokenKind.ClassKeyword:
                    return ParseStructDefinitionOrDeclaration(new List<AttributeNode>());

                case TokenKind.InterfaceKeyword:
                  return ParseInterfaceDefinition(new List<AttributeNode>());

                case TokenKind.TypedefKeyword:
                    return ParseTypedef(new List<AttributeNode>());

                case TokenKind.Technique10Keyword:
                case TokenKind.Technique11Keyword:
                case TokenKind.TechniqueKeyword:
                    return ParseTechnique();

                case TokenKind.SemiToken:
                    var semiTok = Advance();
                    return new EmptyStatementNode(Range(semiTok, semiTok)) { Attributes = new List<AttributeNode>() };

                default:
                    if (IsNextPreProcessorDirective())
                    {
                        return ParsePreProcessorDirective(ParseTopLevelDeclaration);
                    }
                    else if (IsNextPossiblyFunctionDeclaration())
                    {
                        return ParseFunction();
                    }
                    else
                    {
                        return ParseVariableDeclarationStatement(new List<AttributeNode>());
                    }
            }
        }

        private EnumDefinitionNode ParseEnum()
        {
            var first = Advance();
            bool scoped = Match(TokenKind.ClassKeyword);
            if (scoped) Advance();
            var name = ParseIdentifier();
            TypeNode underlying = null;
            if (Match(TokenKind.ColonToken)) { Advance(); underlying = ParseType(); }
            Eat(TokenKind.OpenBraceToken);
            var values = new List<VariableDeclaratorNode>();
            while (!Match(TokenKind.CloseBraceToken) && LoopShouldContinue())
            {
                values.Add(ParseVariableDeclarator(false));
                if (!Match(TokenKind.CommaToken)) break;
                Advance();
            }
            Eat(TokenKind.CloseBraceToken);
            Eat(TokenKind.SemiToken);
            return new EnumDefinitionNode(Range(first, Previous()))
                { Name = name, IsScoped = scoped, UnderlyingType = underlying, Values = values };
        }

        private bool IsNextCast()
        {
            int offset = 0;

            // Must have initial paren
            if (LookAhead(offset).Kind != TokenKind.OpenParenToken)
                return false;
            offset++;

            // If we mention a builtin or user defined type - it might be a cast
            bool isBuiltinType = false;
            if (HLSLSyntaxFacts.IsBuiltinType(LookAhead(offset).Kind) ||
                LookAhead(offset).Kind == TokenKind.ClassKeyword ||
                LookAhead(offset).Kind == TokenKind.StructKeyword ||
                LookAhead(offset).Kind == TokenKind.InterfaceKeyword)
            {
                isBuiltinType = true;
                offset++;
            }
            // If there is an identifier
            else if (LookAhead(offset).Kind == TokenKind.IdentifierToken)
            {
                // Take as many qualifier sections as possible
                offset++;
                while (LookAhead(offset).Kind == TokenKind.ColonColonToken)
                {
                    offset++;
                    if (LookAhead(offset).Kind != TokenKind.IdentifierToken)
                    {
                        return false;
                    }
                    offset++;
                }
            }
            // If none of the above are true, can't be a cast
            else
            {
                return false;
            }

            // It could be a generic type
            if (LookAhead(offset).Kind == TokenKind.LessThanToken)
            {
                offset++;
                while (LookAhead(offset).Kind != TokenKind.GreaterThanToken)
                {
                    if (LookAhead(offset).Kind == TokenKind.InvalidToken)
                        return false;

                    offset++;
                }
                offset++;
            }

            // If we had an identifier, check if it is followed by an array type
            while (LookAhead(offset).Kind == TokenKind.OpenBracketToken)
            {
                // All arguments must be constants or identifiers
                offset++;
                if (LookAhead(offset).Kind != TokenKind.IntegerLiteralToken && LookAhead(offset).Kind != TokenKind.IdentifierToken)
                {
                    return false;
                }
                offset++;
                if (LookAhead(offset).Kind != TokenKind.CloseBracketToken)
                {
                    return false;
                }
                offset++;
            }

            // If we've reached this point, make sure the cast is closed
            if (LookAhead(offset).Kind != TokenKind.CloseParenToken)
                return false;

            // It might still be ambiguous, so check if the next token is allowed to follow a cast
            offset++;
            var followKind = LookAhead(offset).Kind;

            // Handle special case of `(float2)-1` etc.
            if (isBuiltinType && (followKind == TokenKind.MinusToken || followKind == TokenKind.PlusToken))
                return true;

            return HLSLSyntaxFacts.CanTokenComeAfterCast(followKind);
        }

        private bool IsNextPossiblyFunctionDeclaration()
        {
            return Speculate(() =>
            {
                ParseMany0(TokenKind.OpenBracketToken, ParseAttribute);
                ParseDeclarationModifiers();
                ParseType(true);
                ParseUserDefinedNamedType();
                return Match(TokenKind.OpenParenToken);
            });
        }

        private new IdentifierNode ParseIdentifier()
        {
            string identifier = base.ParseIdentifier();
            return new IdentifierNode(new List<HLSLToken> { Previous() })
            {
                Identifier = identifier,
            };
        }

        public StatePropertyNode ParseStateProperty()
        {
            var firstTok = Peek();

            UserDefinedNamedTypeNode name;
            if (Match(TokenKind.TextureKeyword))
            {
                var nameTok = Advance();
                name = new NamedTypeNode(new List<HLSLToken> { nameTok }) 
                {
                    Name = new IdentifierNode(new List<HLSLToken> { nameTok })
                    { 
                        Identifier = "texture" 
                    } 
                };
            }
            else
            {
                name = ParseUserDefinedNamedType();
            }
            ArrayRankNode rank = null;
            if (Match(TokenKind.OpenBracketToken))
            {
                rank = ParseArrayRank();
            }

            ExpressionNode expr;
            Eat(TokenKind.EqualsToken);
            bool isReference = Match(TokenKind.LessThanToken);
            if (isReference)
            {
                Eat(TokenKind.LessThanToken);
                expr = ParseNamedExpression();
                if (Match(TokenKind.OpenBracketToken))
                {
                    Eat(TokenKind.OpenBracketToken);
                    var indexExpr = ParseExpression();
                    Eat(TokenKind.CloseBracketToken);
                    expr = new ElementAccessExpressionNode(Range(firstTok, Previous()))
                    {
                        Target = expr,
                        Index = indexExpr
                    };
                }
                Eat(TokenKind.GreaterThanToken);
            }
            else
            {
                expr = ParseExpression();
            }
            Eat(TokenKind.SemiToken);

            return new StatePropertyNode(Range(firstTok, Previous()))
            {
                Name = name,
                ArrayRank = rank,
                Value = expr,
                IsReference = isReference,
            };
        }

        public SamplerStateLiteralExpressionNode ParseSamplerStateLiteral()
        {
            var keywordTok = Eat(TokenKind.SamplerStateLegacyKeyword);
            Eat(TokenKind.OpenBraceToken);

            List<StatePropertyNode> states = new List<StatePropertyNode>();
            while (Match(TokenKind.IdentifierToken, TokenKind.TextureKeyword))
            {
                states.Add(ParseStateProperty());
            }

            Eat(TokenKind.CloseBraceToken);

            return new SamplerStateLiteralExpressionNode(Range(keywordTok, Previous()))
            {
                States = states
            };
        }

        public CompileExpressionNode ParseCompileExpression()
        {
            var keywordTok = Eat(TokenKind.CompileKeyword);
            var target = ParseIdentifier();

            var name = ParseNamedExpression();
            var param = ParseParameterList();
            var expr = new FunctionCallExpressionNode(Range(keywordTok, Previous())) { Name = name, Arguments = param };

            return new CompileExpressionNode(Range(keywordTok, Previous()))
            {
                Target = target,
                Invocation = expr
            };
        }

        internal ExpressionNode ParseExpression(int level = 0)
        {
            if (Match(TokenKind.SamplerStateLegacyKeyword))
            {
                return ParseSamplerStateLiteral();
            }

            return ParseBinaryExpression(level);
        }

        // https://en.cppreference.com/w/c/language/operator_precedence
        private List<(
            HashSet<TokenKind> operators,
            bool rightAssociative,
            Func<ExpressionNode, OperatorKind, ExpressionNode, ExpressionNode> ctor
        )> operatorGroups;
        private Dictionary<TokenKind, int> operatorLevels;

        private void InitOperatorGroups()
        {
            operatorGroups = new List<(HashSet<TokenKind> operators, bool rightAssociative, Func<ExpressionNode, OperatorKind, ExpressionNode, ExpressionNode> ctor)>
            {
                // Compound expression
                (new HashSet<TokenKind>() { TokenKind.CommaToken },
                false,
                (l, op, r) => new CompoundExpressionNode(Range(l.Tokens.FirstOrDefault(), r.Tokens.LastOrDefault())) { Left = l, Right = r }),

                // Assignment
                (new HashSet<TokenKind>() {
                    TokenKind.EqualsToken, TokenKind.PlusEqualsToken, TokenKind.MinusEqualsToken,
                    TokenKind.AsteriskEqualsToken, TokenKind.SlashEqualsToken, TokenKind.PercentEqualsToken,
                    TokenKind.LessThanLessThanEqualsToken, TokenKind.GreaterThanGreaterThanEqualsToken,
                    TokenKind.AmpersandEqualsToken, TokenKind.CaretEqualsToken, TokenKind.BarEqualsToken },
                true,
                (l, op, r) => new AssignmentExpressionNode(Range(l.Tokens.FirstOrDefault(), r.Tokens.LastOrDefault())) { Left = l, Operator = op, Right = r }),

                // Ternary
                (new HashSet<TokenKind>() { TokenKind.QuestionToken },
                true,
                (l, op, r) => throw new Exception("This should never happen. Please file a bug report.")),

                // LogicalOr
                (new HashSet<TokenKind>() { TokenKind.BarBarToken },
                false,
                (l, op, r) => new BinaryExpressionNode(Range(l.Tokens.FirstOrDefault(), r.Tokens.LastOrDefault())) { Left = l, Operator = op, Right = r }),

                // LogicalAnd
                (new HashSet<TokenKind>() { TokenKind.AmpersandAmpersandToken },
                false,
                (l, op, r) => new BinaryExpressionNode(Range(l.Tokens.FirstOrDefault(), r.Tokens.LastOrDefault())) { Left = l, Operator = op, Right = r }),

                // BitwiseOr
                (new HashSet<TokenKind>() { TokenKind.BarToken },
                false,
                (l, op, r) => new BinaryExpressionNode(Range(l.Tokens.FirstOrDefault(), r.Tokens.LastOrDefault())) { Left = l, Operator = op, Right = r }),

                // BitwiseXor
                (new HashSet<TokenKind>() { TokenKind.CaretToken },
                false,
                (l, op, r) => new BinaryExpressionNode(Range(l.Tokens.FirstOrDefault(), r.Tokens.LastOrDefault())) { Left = l, Operator = op, Right = r }),

                // BitwiseAnd
                (new HashSet<TokenKind>() { TokenKind.AmpersandToken },
                false,
                (l, op, r) => new BinaryExpressionNode(Range(l.Tokens.FirstOrDefault(), r.Tokens.LastOrDefault())) { Left = l, Operator = op, Right = r }),

                // Equality
                (new HashSet<TokenKind>() { TokenKind.EqualsEqualsToken, TokenKind.ExclamationEqualsToken },
                false,
                (l, op, r) => new BinaryExpressionNode(Range(l.Tokens.First(), r.Tokens.Last())) { Left = l, Operator = op, Right = r }),

                // Comparison
                (new HashSet<TokenKind>() { TokenKind.LessThanToken, TokenKind.LessThanEqualsToken, TokenKind.GreaterThanToken, TokenKind.GreaterThanEqualsToken },
                false,
                (l, op, r) => new BinaryExpressionNode(Range(l.Tokens.FirstOrDefault(), r.Tokens.LastOrDefault())) { Left = l, Operator = op, Right = r }),

                // BitShift
                (new HashSet<TokenKind>() { TokenKind.LessThanLessThanToken, TokenKind.GreaterThanGreaterThanToken },
                false,
                (l, op, r) => new BinaryExpressionNode(Range(l.Tokens.FirstOrDefault(), r.Tokens.LastOrDefault())) { Left = l, Operator = op, Right = r }),

                // AddSub
                (new HashSet<TokenKind>() { TokenKind.PlusToken, TokenKind.MinusToken },
                false,
                (l, op, r) => new BinaryExpressionNode(Range(l.Tokens.FirstOrDefault(), r.Tokens.LastOrDefault())) { Left = l, Operator = op, Right = r }),

                // MulDivMod
                (new HashSet<TokenKind>() { TokenKind.AsteriskToken, TokenKind.SlashToken, TokenKind.PercentToken },
                false,
                (l, op, r) => new BinaryExpressionNode(Range(l.Tokens.FirstOrDefault(), r.Tokens.LastOrDefault())) { Left = l, Operator = op, Right = r }),

                // Binds most tightly
            };
            operatorLevels = new Dictionary<TokenKind, int>();
            for (int level = 0; level < operatorGroups.Count; level++)
                foreach (var token in operatorGroups[level].operators)
                    operatorLevels.Add(token, level);
        }
        
        public ExpressionNode ParseBinaryExpression(int level = 0)
        {
            ExpressionNode higher = ParsePrefixOrPostFixExpression();
            while (operatorLevels.TryGetValue(Peek().Kind, out int operatorLevel) && operatorLevel >= level)
            {
                if (operatorLevel == (int)OperatorPrecedence.Ternary)
                {
                    Eat(TokenKind.QuestionToken);
                    var left = ParseExpression();
                    Eat(TokenKind.ColonToken);
                    var right = ParseExpression();
                    return new TernaryExpressionNode(Range(higher.Tokens.First(), right.Tokens.Last())) { Condition = higher, TrueCase = left, FalseCase = right };
                }
                var group = operatorGroups[operatorLevel];
                HLSLToken next = Advance();
                if (!HLSLSyntaxFacts.TryConvertToOperator(next.Kind, out OperatorKind op))
                {
                    Error("a valid operator", next);
                }

                higher = group.ctor(
                    higher,
                    op,
                    ParseBinaryExpression(group.rightAssociative ? operatorLevel : operatorLevel + 1));

                if (IsAtEnd())
                {
                    return higher;
                }
            }

            return higher;
        }

        public ExpressionNode ParsePrefixOrPostFixExpression()
        {
            var firstTok = Peek();
            ExpressionNode higher;
            switch (firstTok.Kind)
            {
                case TokenKind.PlusPlusToken:
                case TokenKind.MinusMinusToken:
                case TokenKind.PlusToken:
                case TokenKind.MinusToken:
                case TokenKind.NotToken:
                case TokenKind.TildeToken:
                    TokenKind opKind = Eat(HLSLSyntaxFacts.IsPrefixUnaryToken).Kind;
                    HLSLSyntaxFacts.TryConvertToOperator(opKind, out var op);
                    var unExpr = ParsePrefixOrPostFixExpression();
                    higher = new PrefixUnaryExpressionNode(Range(firstTok, Previous())) { Operator = op, Expression = unExpr };
                    break;

                case TokenKind.OpenParenToken when IsNextCast():
                    Eat(TokenKind.OpenParenToken);
                    var type = ParseType();
                    List<ArrayRankNode> arrayRanks = new List<ArrayRankNode>();
                    while (Match(TokenKind.OpenBracketToken))
                    {
                        arrayRanks.Add(ParseArrayRank());
                    }
                    Eat(TokenKind.CloseParenToken);
                    var castExpr = ParsePrefixOrPostFixExpression();
                    higher = new CastExpressionNode(Range(firstTok, Previous())) { Kind = type, Expression = castExpr, ArrayRanks = arrayRanks, IsFunctionLike = false };
                    break;

                case TokenKind.OpenParenToken:
                    Eat(TokenKind.OpenParenToken);
                    higher = ParseExpression();
                    Eat(TokenKind.CloseParenToken);
                    break;

                default:
                    // Special case for constructors of built-in types. Their target is not an expression, but a keyword.
                    if (Match(HLSLSyntaxFacts.IsMultiArityNumericConstructor))
                    {
                        var kind = ParseNumericType();
                        var ctorArgs = ParseParameterList();
                        higher = new NumericConstructorCallExpressionNode(Range(firstTok, Previous())) { Kind = kind, Arguments = ctorArgs };
                    }
                    // Special case for function style C-casts
                    else if (Match(HLSLSyntaxFacts.IsSingleArityNumericConstructor))
                    {
                        var kind = ParseNumericType();
                        Eat(TokenKind.OpenParenToken);
                        var castFrom = ParseExpression();
                        Eat(TokenKind.CloseParenToken);
                        higher = new CastExpressionNode(Range(firstTok, Previous())) { Kind = kind, Expression = castFrom, ArrayRanks = new List<ArrayRankNode>(), IsFunctionLike = true };
                    }
                    else
                    {
                        higher = ParseTerminalExpression();
                    }
                    break;
            }

            while (LoopShouldContinue())
            {
                switch (Peek().Kind)
                {
                    case TokenKind.PlusPlusToken:
                    case TokenKind.MinusMinusToken:
                        HLSLSyntaxFacts.TryConvertToOperator(Advance().Kind, out var incrOp);
                        higher = new PostfixUnaryExpressionNode(Range(firstTok, Previous())) { Expression = higher, Operator = incrOp };
                        break;

                    case TokenKind.OpenParenToken when higher is NamedExpressionNode target:
                        var funcArgs = ParseParameterList();
                        higher = new FunctionCallExpressionNode(Range(firstTok, Previous())) { Name = target, Arguments = funcArgs };
                        break;

                    case TokenKind.OpenBracketToken:
                        Eat(TokenKind.OpenBracketToken);
                        var indexArg = ParseExpression();
                        Eat(TokenKind.CloseBracketToken);
                        higher = new ElementAccessExpressionNode(Range(firstTok, Previous())) { Target = higher, Index = indexArg };
                        break;

                    case TokenKind.DotToken:
                        Eat(TokenKind.DotToken);
                        var identifier = ParseIdentifier();

                        if (Match(TokenKind.OpenParenToken))
                        {
                            var methodArgs = ParseParameterList();
                            higher = new MethodCallExpressionNode(Range(firstTok, Previous())) { Target = higher, Name = identifier, Arguments = methodArgs };
                        }
                        else
                        {
                            higher = new FieldAccessExpressionNode(Range(firstTok, Previous())) { Target = higher, Name = identifier };
                        }
                        break;

                    default:
                        return higher;
                }
            }

            return higher;
        }

        public NamedExpressionNode ParseNamedExpression()
        {
            var firstTok = Peek();
            if (Match(TokenKind.ColonColonToken))
            {
                Advance();
                var root = new IdentifierExpressionNode(Range(firstTok, firstTok))
                    { Name = new IdentifierNode(Range(firstTok, firstTok)) { Identifier = "" } };
                var right = ParseNamedExpression();
                return new QualifiedIdentifierExpressionNode(Range(firstTok, Previous())) { Left = root, Right = right };
            }
            var identifier = ParseIdentifier();
            
            var name = new IdentifierExpressionNode(Range(firstTok, firstTok)) { Name = identifier };

            if (Match(TokenKind.ColonColonToken))
            {
                Eat(TokenKind.ColonColonToken);

                var nextNameExpr = ParseNamedExpression();
                return new QualifiedIdentifierExpressionNode(Range(firstTok, Previous())) { Left = name, Right = nextNameExpr };
            }
            else
            {
                return name;
            }
        }

        public ArrayInitializerExpressionNode ParseArrayInitializer()
        {
            var openTok = Eat(TokenKind.OpenBraceToken);
            var exprs = ParseSeparatedList0(
                TokenKind.CloseBraceToken,
                TokenKind.CommaToken,
                () => ParseExpression((int)OperatorPrecedence.Compound + 1),
                true);
            var closeTok = Eat(TokenKind.CloseBraceToken);
            return new ArrayInitializerExpressionNode(Range(openTok, closeTok)) { Elements = exprs };
        }

        public LiteralExpressionNode ParseLiteralExpression()
        {
            HLSLToken next = Peek();
            string lexeme = HLSLSyntaxFacts.IdentifierOrKeywordToString(next);

            if (!HLSLSyntaxFacts.TryConvertLiteralKind(next.Kind, out var literalKind))
            {
                Error("a valid literal expression", next);
            }
            Advance();

            return new LiteralExpressionNode(Range(next, next)) { Lexeme = lexeme, Kind = literalKind };
        }

        public ExpressionNode ParseTerminalExpression()
        {
            if (Match(TokenKind.IdentifierToken, TokenKind.ColonColonToken))
            {
                return ParseNamedExpression();
            }

            else if (Match(TokenKind.CompileKeyword))
            {
                return ParseCompileExpression();
            }

            else if (Match(TokenKind.OpenBraceToken))
            {
                return ParseArrayInitializer();
            }

            return ParseLiteralExpression();
        }

        public List<ExpressionNode> ParseParameterList()
        {
            Eat(TokenKind.OpenParenToken);
            List<ExpressionNode> exprs = ParseSeparatedList0(
                TokenKind.CloseParenToken,
                TokenKind.CommaToken,
                () => ParseExpression((int)OperatorPrecedence.Compound + 1));
            Eat(TokenKind.CloseParenToken);
            return exprs;
        }

        public AttributeNode ParseAttribute()
        {
            var openTok = Eat(TokenKind.OpenBracketToken);

            var identifier = ParseIdentifier();

            List<ExpressionNode> args = new List<ExpressionNode>();
            if (Match(TokenKind.OpenParenToken))
            {
                Eat(TokenKind.OpenParenToken);

                args = ParseSeparatedList1(TokenKind.CommaToken, () => ParseExpression((int)OperatorPrecedence.Compound + 1));

                Eat(TokenKind.CloseParenToken);
            }

            var closeTok = Eat(TokenKind.CloseBracketToken);

            return new AttributeNode(Range(openTok, closeTok))
            {
                Name = identifier,
                Arguments = args
            };
        }

        public FunctionNode ParseFunction()
        {
            var firstTok = Peek();
            List<AttributeNode> attributes = ParseMany0(TokenKind.OpenBracketToken, ParseAttribute);

            var modifiers = ParseDeclarationModifiers();
            TypeNode returnType = ParseType(true);

            UserDefinedNamedTypeNode name = ParseUserDefinedNamedType();

            Eat(TokenKind.OpenParenToken);
            List<FormalParameterNode> parameters = ParseSeparatedList0(TokenKind.CloseParenToken, TokenKind.CommaToken, ParseFormalParameter);
            Eat(TokenKind.CloseParenToken);

            SemanticNode semantic = ParseOptional(TokenKind.ColonToken, ParseSemantic);

            // Function prototype
            if (Match(TokenKind.SemiToken))
            {
                var semiTok = Eat(TokenKind.SemiToken);
                return new FunctionDeclarationNode(Range(firstTok, semiTok))
                {
                    Attributes = attributes,
                    Modifiers = modifiers,
                    ReturnType = returnType,
                    Name = name,
                    Parameters = parameters,
                    Semantic = semantic
                };
            }
            RecoverTo(TokenKind.SemiToken, TokenKind.CloseBraceToken);

            // Otherwise, full function
            BlockNode body = ParseBlock(new List<AttributeNode>());
            return new FunctionDefinitionNode(Range(firstTok, Previous()))
            {
                Attributes = attributes,
                Modifiers = modifiers,
                ReturnType = returnType,
                Name = name,
                Parameters = parameters,
                Semantic = semantic,
                Body = body
            };
        }

        public StatementNode ParseStructDefinitionOrDeclaration(List<AttributeNode> attributes)
        {
            var firstTok = attributes.FirstOrDefault()?.Tokens.FirstOrDefault() ?? Peek();
            var modifiers = ParseDeclarationModifiers();
            StructTypeNode structType = ParseStructType();

            // This is a definition - no instance
            if (Match(TokenKind.SemiToken))
            {
                if (modifiers.Count > 0)
                {
                    Error(DiagnosticFlags.SyntaxError, $"Struct definitions cannot have modifiers, found '{string.Join(", ", modifiers)}'.");
                }

                var semiTok = Eat(TokenKind.SemiToken);
                RecoverTo(TokenKind.SemiToken);
                return new StructDefinitionNode(Range(firstTok, semiTok))
                {
                    Attributes = attributes,
                    StructType = structType,
                };
            }
            // This is a declaration - making a type and an instance
            else
            {
                List<VariableDeclaratorNode> variables = ParseSeparatedList1(TokenKind.CommaToken, () => ParseVariableDeclarator());
                var semiTok = Eat(TokenKind.SemiToken);
                RecoverTo(TokenKind.SemiToken);
                return new VariableDeclarationStatementNode(Range(firstTok, semiTok))
                {
                    Modifiers = modifiers,
                    Kind = structType,
                    Declarators = variables,
                    Attributes = attributes,
                };
            }
        }

        public InterfaceDefinitionNode ParseInterfaceDefinition(List<AttributeNode> attributes)
        {
            var keywordTok = Eat(TokenKind.InterfaceKeyword);
            var firstTok = attributes.FirstOrDefault()?.Tokens.FirstOrDefault() ?? keywordTok;
            var name = ParseUserDefinedNamedType();

            Eat(TokenKind.OpenBraceToken);

            List<FunctionNode> funs = ParseMany0(
                () => !Match(TokenKind.CloseBraceToken),
                ParseFunction);

            List<FunctionDeclarationNode> decls = new List<FunctionDeclarationNode>();
            foreach (var function in funs)
            {
                if (function is FunctionDeclarationNode decl)
                {
                    decls.Add(decl);
                }
                else
                {
                    Error(DiagnosticFlags.SemanticError, "Expected only function declarations/prototypes in interface type, but found a function body.");
                }
            }

            Eat(TokenKind.CloseBraceToken);
            var semiTok = Eat(TokenKind.SemiToken);
            RecoverTo(TokenKind.SemiToken);

            return new InterfaceDefinitionNode(Range(keywordTok, semiTok))
            {
                Attributes = attributes,
                Name = name,
                Functions = decls,
            };
        }

        public NamespaceNode ParseNamespace()
        {
            var keywordTok = Eat(TokenKind.NamespaceKeyword);
            var name = ParseUserDefinedNamedType();
            Eat(TokenKind.OpenBraceToken);
            var decls = ParseMany0(() => !Match(TokenKind.CloseBraceToken), ParseTopLevelDeclaration);
            var closeTok = Eat(TokenKind.CloseBraceToken);
            RecoverTo(TokenKind.CloseBraceToken);

            return new NamespaceNode(Range(keywordTok, closeTok))
            {
                Name = name,
                Declarations = decls,
            };
        }

        public ConstantBufferNode ParseConstantBuffer()
        {
            var buffer = Eat(TokenKind.CBufferKeyword, TokenKind.TBufferKeyword);
            var name = ParseUserDefinedNamedType();

            RegisterLocationNode reg = null;
            if (Match(TokenKind.ColonToken))
            {
                reg = ParseRegisterLocation();
            }

            Eat(TokenKind.OpenBraceToken);

            var decls = new List<VariableDeclarationStatementNode>();
            while (!Match(TokenKind.CloseBraceToken) && LoopShouldContinue())
            {
                if (Match(TokenKind.SemiToken)) Eat(TokenKind.SemiToken);
                else decls.Add(ParseVariableDeclarationStatement(new List<AttributeNode>()));
            }

            Eat(TokenKind.CloseBraceToken);
            if (Match(TokenKind.SemiToken))
            {
                Eat(TokenKind.SemiToken);
            }
            RecoverTo(TokenKind.SemiToken, TokenKind.CloseBraceToken);

            return new ConstantBufferNode(Range(buffer, Previous()))
            {
                Name = name,
                RegisterLocation = reg,
                Declarations = decls,
                IsTextureBuffer = buffer.Kind == TokenKind.TBufferKeyword
            };
        }

        public TypedefNode ParseTypedef(List<AttributeNode> attributes)
        {
            var keywordTok = Eat(TokenKind.TypedefKeyword);
            var firstTok = attributes.FirstOrDefault()?.Tokens.FirstOrDefault() ?? keywordTok;

            bool isConst = false;
            if (Match(TokenKind.ConstKeyword))
            {
                Eat(TokenKind.ConstKeyword);
                isConst = true;
            }

            var type = ParseType();

            var names = ParseSeparatedList1(TokenKind.CommaToken, ParseUserDefinedNamedType);

            var semiTok = Eat(TokenKind.SemiToken);
            RecoverTo(TokenKind.SemiToken);

            return new TypedefNode(Range(firstTok, semiTok))
            {
                Attributes = attributes,
                FromType = type,
                ToNames = names,
                IsConst = isConst,
            };
        }

        public TypeNode ParseType(bool allowVoid = false)
        {
            if (HLSLSyntaxFacts.TryConvertToPredefinedObjectType(Peek(), out PredefinedObjectType predefinedType))
            {
                var firstTok = Advance();

                List<TypeNode> args = new List<TypeNode>();
                if (Match(TokenKind.LessThanToken))
                {
                    Eat(TokenKind.LessThanToken);
                    args = ParseSeparatedList0(
                        TokenKind.GreaterThanToken,
                        TokenKind.CommaToken,
                        ParseTemplateArgumentType);
                    Eat(TokenKind.GreaterThanToken);
                }

                return new PredefinedObjectTypeNode(Range(firstTok, Previous()))
                {
                    Kind = predefinedType,
                    TemplateArguments = args,
                };
            }

            if (Match(TokenKind.IdentifierToken))
            {
                return ParseUserDefinedNamedType();
            }

            if (Match(TokenKind.StructKeyword, TokenKind.ClassKeyword))
            {
                return ParseStructType();
            }

            return ParseNumericType(allowVoid);
        }

        public StructTypeNode ParseStructType()
        {
            var keywordTok = Eat(TokenKind.StructKeyword, TokenKind.ClassKeyword);
            bool isClass = keywordTok.Kind == TokenKind.ClassKeyword;
            var name = ParseOptional(TokenKind.IdentifierToken, ParseUserDefinedNamedType);

            if (Match(TokenKind.SemiToken))
                return new StructTypeNode(Range(keywordTok, Previous()))
                {
                    Name = name, IsClass = isClass, IsForwardDeclaration = true,
                    Inherits = new List<UserDefinedNamedTypeNode>(),
                    Fields = new List<VariableDeclarationStatementNode>(), Methods = new List<FunctionNode>()
                };

            // base list
            List<UserDefinedNamedTypeNode> baseList = new List<UserDefinedNamedTypeNode>();
            if (Match(TokenKind.ColonToken))
            {
                Eat(TokenKind.ColonToken);

                baseList = ParseSeparatedList1(TokenKind.CommaToken, ParseUserDefinedNamedType);
            }

            Eat(TokenKind.OpenBraceToken);

            List<VariableDeclarationStatementNode> decls = new List<VariableDeclarationStatementNode>();
            List<FunctionNode> methods = new List<FunctionNode>();
            var enums = new List<EnumDefinitionNode>();
            var nestedTypes = new List<StructDefinitionNode>();
            while (LoopShouldContinue() && !Match(TokenKind.CloseBraceToken))
            {
                if (Match(TokenKind.SemiToken))
                {
                    Advance();
                }
                else if (Match(TokenKind.IdentifierToken) && Peek().Identifier == "enum")
                {
                    enums.Add(ParseEnum());
                }
                else if (Match(TokenKind.StructKeyword, TokenKind.ClassKeyword))
                {
                    var nested = ParseStructDefinitionOrDeclaration(new List<AttributeNode>());
                    if (nested is StructDefinitionNode definition) nestedTypes.Add(definition);
                    else decls.Add((VariableDeclarationStatementNode)nested);
                }
                else if (IsNextPossiblyFunctionDeclaration())
                {
                    methods.Add(ParseFunction());
                }
                else
                {
                    decls.Add(ParseVariableDeclarationStatement(new List<AttributeNode>()));
                }
            }

            var closeTok = Eat(TokenKind.CloseBraceToken);

            return new StructTypeNode(Range(keywordTok, closeTok))
            {
                Name = name,
                Inherits = baseList,
                Fields = decls,
                Methods = methods,
                Enums = enums,
                NestedTypes = nestedTypes,
                IsClass = isClass,
            };
        }

        public NumericTypeNode ParseNumericType(bool allowVoid = false)
        {
            HLSLToken typeToken = Advance();
            if (HLSLSyntaxFacts.TryConvertToScalarType(typeToken.Kind, out ScalarType scalarType))
            {
                if (scalarType == ScalarType.Void && !allowVoid)
                    Error("a type that isn't 'void'", typeToken);
                return new ScalarTypeNode(Range(typeToken, typeToken)) { Kind = scalarType };
            }

            if (HLSLSyntaxFacts.TryConvertToMonomorphicVectorType(typeToken.Kind, out ScalarType vectorType, out int dimension))
            {
                if (typeToken.Kind == TokenKind.VectorKeyword && Match(TokenKind.LessThanToken))
                {
                    Eat(TokenKind.LessThanToken);
                    var genVectorType = ParseNumericType().Kind;
                    Eat(TokenKind.CommaToken);
                    var genDim = ParseExpression((int)OperatorPrecedence.Comparison + 1);
                    var closeTok = Eat(TokenKind.GreaterThanToken);
                    return new GenericVectorTypeNode(Range(typeToken, closeTok)) { Kind = genVectorType, Dimension = genDim };
                }

                return new VectorTypeNode(Range(typeToken, typeToken)) { Kind = vectorType, Dimension = dimension };
            }

            if (HLSLSyntaxFacts.TryConvertToMonomorphicMatrixType(typeToken.Kind, out ScalarType matrixType, out int dimX, out int dimY))
            {
                if (typeToken.Kind == TokenKind.MatrixKeyword && Match(TokenKind.LessThanToken))
                {
                    Eat(TokenKind.LessThanToken);
                    var genMatrixType = ParseNumericType().Kind;
                    Eat(TokenKind.CommaToken);
                    var genDimX = ParseExpression((int)OperatorPrecedence.Comparison + 1);
                    Eat(TokenKind.CommaToken);
                    var genDimY = ParseExpression((int)OperatorPrecedence.Comparison + 1);
                    var closeTok = Eat(TokenKind.GreaterThanToken);
                    return new GenericMatrixTypeNode(Range(typeToken, closeTok)) { Kind = genMatrixType, FirstDimension = genDimX, SecondDimension = genDimY };
                }

                return new MatrixTypeNode(Range(typeToken, typeToken)) { Kind = matrixType, FirstDimension = dimX, SecondDimension = dimY };
            }

            if (typeToken.Kind == TokenKind.UnsignedKeyword)
            {
                var type = ParseNumericType();
                type.Kind = HLSLSyntaxFacts.MakeUnsigned(type.Kind);
                return type;
            }

            if (typeToken.Kind == TokenKind.UNormKeyword || typeToken.Kind == TokenKind.SNormKeyword)
            {
                var type = ParseNumericType();
                type.Kind = HLSLSyntaxFacts.MakeNormed(type.Kind, typeToken.Kind);
                return type;
            }

            Error("a valid type", typeToken);
            return new ScalarTypeNode(Range(typeToken, typeToken)) { Kind = ScalarType.Void };
        }

        public UserDefinedNamedTypeNode ParseUserDefinedNamedType()
        {
            var firstTok = Peek();
            var identifier = ParseIdentifier();
            var name = new NamedTypeNode(Range(firstTok, firstTok)) { Name = identifier };

            if (Match(TokenKind.ColonColonToken))
            {
                Eat(TokenKind.ColonColonToken);
                var right = ParseUserDefinedNamedType();
                return new QualifiedNamedTypeNode(Range(firstTok, Previous())) { Left = name, Right = right };
            }
            else
            {
                return name;
            }
        }

        public TypeNode ParseTemplateArgumentType()
        {
            if (Match(TokenKind.CharacterLiteralToken, TokenKind.FloatLiteralToken, TokenKind.IntegerLiteralToken, TokenKind.StringLiteralToken))
            {
                var expression = ParseLiteralExpression();
                return new LiteralTemplateArgumentType(Range(Previous(), Previous())) { Literal = expression };
            }

            return ParseType();
        }

        public FormalParameterNode ParseFormalParameter()
        {
            var firstTok = Peek();
            List<AttributeNode> attributes = ParseMany0(TokenKind.OpenBracketToken, ParseAttribute);
            var modifiers = ParseParameterModifiers();
            TypeNode type = ParseType();
            VariableDeclaratorNode declarator = ParseVariableDeclarator(false);

            return new FormalParameterNode(Range(firstTok, Previous()))
            {
                Attributes = attributes,
                Modifiers = modifiers,
                ParamType = type,
                Declarator = declarator
            };
        }

        public ArrayRankNode ParseArrayRank()
        {
            var openTok = Eat(TokenKind.OpenBracketToken);
            ExpressionNode expr = null;
            if (!Match(TokenKind.CloseBracketToken))
            {
                expr = ParseExpression();
            }
            var closeTok = Eat(TokenKind.CloseBracketToken);
            return new ArrayRankNode(Range(openTok, closeTok)) { Dimension = expr };
        }

        public VariableDeclaratorNode ParseVariableDeclarator(bool allowCompoundInitializer = true)
        {
            var firstTok = Peek();
            var identifier = ParseIdentifier();

            List<ArrayRankNode> arrayRanks = new List<ArrayRankNode>();
            while (Match(TokenKind.OpenBracketToken))
            {
                arrayRanks.Add(ParseArrayRank());
            }

            List<VariableDeclaratorQualifierNode> qualifiers = ParseMany0(TokenKind.ColonToken, ParseVariableDeclaratorQualifierNode);

            List<VariableDeclarationStatementNode> annotations = new List<VariableDeclarationStatementNode>();
            if (Match(TokenKind.LessThanToken))
            {
                Eat(TokenKind.LessThanToken);
                annotations = ParseMany0(() => !Match(TokenKind.GreaterThanToken), () => ParseVariableDeclarationStatement(new List<AttributeNode>()));
                Eat(TokenKind.GreaterThanToken);
            }

            InitializerNode initializer = null;
            if (Match(TokenKind.EqualsToken))
            {
                initializer = ParseValueInitializer(allowCompoundInitializer);
            }
            else if (Match(TokenKind.OpenBraceToken))
            {
                initializer = ParseStateInitializerOrArray();
            }

            return new VariableDeclaratorNode(Range(firstTok, Previous()))
            {
                Name = identifier,
                ArrayRanks = arrayRanks,
                Qualifiers = qualifiers,
                Annotations = annotations,
                Initializer = initializer,
            };
        }

        public ValueInitializerNode ParseValueInitializer(bool allowCompoundInitializer = true)
        {
            var eqTok = Eat(TokenKind.EqualsToken);
            var expr = ParseExpression(allowCompoundInitializer ? 0 : (int)OperatorPrecedence.Compound + 1);
            return new ValueInitializerNode(Range(eqTok, Previous())) { Expression = expr };
        }

        public StateInitializerNode ParseStateInitializer()
        {
            var openTok = Eat(TokenKind.OpenBraceToken);
            List<StatePropertyNode> states = new List<StatePropertyNode>();
            while (Match(TokenKind.IdentifierToken))
            {
                states.Add(ParseStateProperty());
            }
            var closeTok = Eat(TokenKind.CloseBraceToken);
            return new StateInitializerNode(Range(openTok, closeTok)) { States = states };
        }

        public InitializerNode ParseStateInitializerOrArray()
        {
            if (LookAhead().Kind == TokenKind.OpenBraceToken)
            {
                var openTok = Eat(TokenKind.OpenBraceToken);
                List<StateInitializerNode> initializers = ParseSeparatedList0(TokenKind.CloseBraceToken, TokenKind.CommaToken, ParseStateInitializer);
                var closeTok = Eat(TokenKind.CloseBraceToken);
                return new StateArrayInitializerNode(Range(openTok, closeTok)) { Initializers = initializers };
            }
            else
            {
                return ParseStateInitializer();
            }
        }

        public VariableDeclaratorQualifierNode ParseVariableDeclaratorQualifierNode()
        {
            switch (LookAhead().Kind)
            {
                case TokenKind.IdentifierToken: return ParseSemantic();
                case TokenKind.RegisterKeyword: return ParseRegisterLocation();
                case TokenKind.PackoffsetKeyword: return ParsePackoffsetNode();
                default: return ParseSemantic();
            }
        }

        public SemanticNode ParseSemantic()
        {
            var colTok = Eat(TokenKind.ColonToken);
            var identifier = ParseIdentifier();
            return new SemanticNode(Range(colTok, Previous())) { Name = identifier };
        }

        public RegisterLocationNode ParseRegisterLocation()
        {
            var colTok = Eat(TokenKind.ColonToken);
            Eat(TokenKind.RegisterKeyword);
            Eat(TokenKind.OpenParenToken);

            string location = base.ParseIdentifier();
            RegisterKind kind = default;
            int index = 0;
            switch (location.ToLower().FirstOrDefault())
            {
                case 't': kind = RegisterKind.Texture; break;
                case 'b': kind = RegisterKind.Buffer; break;
                case 'u': kind = RegisterKind.UAV; break;
                case 's': kind = RegisterKind.Sampler; break;
                default: break;
            }
            string indexLexeme = string.Concat(location.SkipWhile(x => !char.IsNumber(x)));
            if (!int.TryParse(indexLexeme, out index))
            {
                Error(DiagnosticFlags.SemanticError, $"Expected a valid register location, but got '{location}'.");
            }

            int? spaceIndex = null;
            if (Match(TokenKind.CommaToken))
            {
                Eat(TokenKind.CommaToken);

                string space = base.ParseIdentifier();
                string spaceLexeme = string.Concat(space.SkipWhile(x => !char.IsNumber(x)));
                if (int.TryParse(spaceLexeme, out int parsedIndex))
                {
                    spaceIndex = parsedIndex;
                }
                else
                {
                    Error(DiagnosticFlags.SemanticError, $"Expected a valid space, but got '{location}'.");
                }
            }

            var closeTok = Eat(TokenKind.CloseParenToken);

            return new RegisterLocationNode(Range(colTok, closeTok))
            {
                Kind = kind,
                Location = index,
                Space = spaceIndex,
            };
        }

        public PackoffsetNode ParsePackoffsetNode()
        {
            var colTok = Eat(TokenKind.ColonToken);
            Eat(TokenKind.PackoffsetKeyword);
            Eat(TokenKind.OpenParenToken);

            string location = base.ParseIdentifier();
            int index = 0;
            string indexLexeme = string.Concat(location.SkipWhile(x => !char.IsNumber(x)));
            if (!int.TryParse(indexLexeme, out index))
            {
                Error(DiagnosticFlags.SemanticError, $"Expected a valid packoffset location, but got '{location}'.");
            }

            string swizzle = null;
            if (Match(TokenKind.DotToken))
            {
                Eat(TokenKind.DotToken);
                swizzle = base.ParseIdentifier();
            }

            var closeTok = Eat(TokenKind.CloseParenToken);

            return new PackoffsetNode(Range(colTok, closeTok))
            {
                Location = index,
                Swizzle = swizzle,
            };
        }

        public BlockNode ParseBlock(List<AttributeNode> attributes)
        {
            var openTok = Eat(TokenKind.OpenBraceToken);
            var firstTok = attributes.FirstOrDefault()?.Tokens.FirstOrDefault() ?? openTok;
            List<StatementNode> statements = ParseMany0(() => !Match(TokenKind.CloseBraceToken), ParseStatement);
            var closeTok = Eat(TokenKind.CloseBraceToken);
            RecoverTo(TokenKind.CloseBraceToken);

            return new BlockNode(Range(firstTok, closeTok))
            {
                Attributes = attributes,
                Statements = statements,
            };
        }

        private bool IsVariableDeclarationStatement(TokenKind nextKind)
        {
            if (HLSLSyntaxFacts.IsModifier(nextKind))
                return true;
            if ((HLSLSyntaxFacts.IsBuiltinType(nextKind) || nextKind == TokenKind.IdentifierToken))
            {
                return Speculate(() =>
                {
                    ParseType();
                    return Match(TokenKind.IdentifierToken);
                });
            }
            return false;
        }

        public StatementNode ParseStatement()
        {
            var firstTok = Peek();
            List<AttributeNode> attributes = ParseMany0(TokenKind.OpenBracketToken, ParseAttribute);

            HLSLToken next = Peek();
            switch (next.Kind)
            {
                case TokenKind.SemiToken:
                    var emptySemiTok = Advance();
                    return new EmptyStatementNode(Range(firstTok, emptySemiTok)) { Attributes = attributes };

                case TokenKind.OpenBraceToken:
                    return ParseBlock(attributes);

                case TokenKind.ReturnKeyword:
                    Advance();
                    ExpressionNode returnExpr = null;
                    if (!Match(TokenKind.SemiToken))
                    {
                        returnExpr = ParseExpression();
                    }
                    var returnSemiTok = Eat(TokenKind.SemiToken);
                    RecoverTo(TokenKind.SemiToken);
                    return new ReturnStatementNode(Range(firstTok, returnSemiTok)) { Attributes = attributes, Expression = returnExpr };

                case TokenKind.ForKeyword:
                    return ParseForStatement(attributes);

                case TokenKind.WhileKeyword:
                    return ParseWhileStatement(attributes);

                case TokenKind.DoKeyword:
                    return ParseDoWhileStatement(attributes);

                case TokenKind.IfKeyword:
                    return ParseIfStatement(attributes);

                case TokenKind.SwitchKeyword:
                    return ParseSwitchStatement(attributes);

                case TokenKind.TypedefKeyword:
                    return ParseTypedef(attributes);

                case TokenKind.BreakKeyword:
                    Advance();
                    var breakTok = Eat(TokenKind.SemiToken);
                    RecoverTo(TokenKind.SemiToken);
                    return new BreakStatementNode(Range(firstTok, breakTok)) { Attributes = attributes };

                case TokenKind.ContinueKeyword:
                    Advance();
                    var continueTok = Eat(TokenKind.SemiToken);
                    RecoverTo(TokenKind.SemiToken);
                    return new ContinueStatementNode(Range(firstTok, continueTok)) { Attributes = attributes };

                case TokenKind.DiscardKeyword:
                    Advance();
                    var discardTok = Eat(TokenKind.SemiToken);
                    RecoverTo(TokenKind.SemiToken);
                    return new DiscardStatementNode(Range(firstTok, discardTok)) { Attributes = attributes };

                case TokenKind.InterfaceKeyword:
                    return ParseInterfaceDefinition(attributes);

                case TokenKind.StructKeyword:
                case TokenKind.ClassKeyword:
                    return ParseStructDefinitionOrDeclaration(attributes);

                case TokenKind kind when IsVariableDeclarationStatement(kind):
                    return ParseVariableDeclarationStatement(attributes);

                case var _ when IsNextPreProcessorDirective():
                    return ParsePreProcessorDirective(ParseStatement);

                default:
                    ExpressionNode expr = ParseExpression();
                    var exprSemiTok = Eat(TokenKind.SemiToken);
                    RecoverTo(TokenKind.SemiToken);
                    return new ExpressionStatementNode(Range(firstTok, exprSemiTok)) { Attributes = attributes, Expression = expr };
            }
        }

        public List<BindingModifier> ParseParameterModifiers()
        {
            List<BindingModifier> modifiers = new List<BindingModifier>();
            while (HLSLSyntaxFacts.TryConvertToParameterModifier(Peek(), out var modifier))
            {
                Advance();
                modifiers.Add(modifier);
            }
            return modifiers;
        }

        public List<BindingModifier> ParseDeclarationModifiers()
        {
            List<BindingModifier> modifiers = new List<BindingModifier>();
            while (HLSLSyntaxFacts.TryConvertToDeclarationModifier(Peek(), out var modifier))
            {
                Advance();
                modifiers.Add(modifier);
            }
            return modifiers;
        }

        public VariableDeclarationStatementNode ParseVariableDeclarationStatement(List<AttributeNode> attributes)
        {
            var firstTok = attributes.FirstOrDefault()?.Tokens.FirstOrDefault() ?? Peek();
            var modifiers = ParseDeclarationModifiers();
            TypeNode kind = ParseType();
            List<VariableDeclaratorNode> variables = ParseSeparatedList1(TokenKind.CommaToken, () => ParseVariableDeclarator(false));
            var semiTok = Eat(TokenKind.SemiToken);
            RecoverTo(TokenKind.SemiToken);

            return new VariableDeclarationStatementNode(Range(firstTok, semiTok))
            {
                Modifiers = modifiers,
                Kind = kind,
                Declarators = variables,
                Attributes = attributes,
            };
        }

        public ForStatementNode ParseForStatement(List<AttributeNode> attributes)
        {
            var keywordTok = Eat(TokenKind.ForKeyword);
            var firstTok = attributes.FirstOrDefault()?.Tokens.FirstOrDefault() ?? keywordTok;
            Eat(TokenKind.OpenParenToken);

            VariableDeclarationStatementNode decl = null;
            ExpressionNode initializer = null;
            if (!Match(TokenKind.SemiToken))
            {
                if (!TryParse(() => ParseVariableDeclarationStatement(new List<AttributeNode>()), out decl))
                {
                    if (TryParse(() => ParseExpression(), out initializer))
                    {
                        Eat(TokenKind.SemiToken);
                    }
                    else
                    {
                        Error("an expression or declaration in first section of for loop", Peek());
                    }
                }
            }
            else
            {
                Eat(TokenKind.SemiToken);
            }

            ExpressionNode cond = null;
            if (!Match(TokenKind.SemiToken))
            {
                cond = ParseExpression();
            }
            Eat(TokenKind.SemiToken);

            ExpressionNode incrementor = null;
            if (!Match(TokenKind.SemiToken))
            {
                incrementor = ParseExpression();
            }
            Eat(TokenKind.CloseParenToken);

            var body = ParseStatement();
            RecoverTo(TokenKind.SemiToken, TokenKind.CloseBraceToken);

            return new ForStatementNode(Range(firstTok, Previous()))
            {
                Declaration = decl,
                Condition = cond,
                Increment = incrementor,
                Body = body,
                Attributes = attributes,
            };
        }

        public WhileStatementNode ParseWhileStatement(List<AttributeNode> attributes)
        {
            var keywordTok = Eat(TokenKind.WhileKeyword);
            var firstTok = attributes.FirstOrDefault()?.Tokens.FirstOrDefault() ?? keywordTok;
            Eat(TokenKind.OpenParenToken);

            var cond = ParseExpression();

            Eat(TokenKind.CloseParenToken);

            var body = ParseStatement();
            RecoverTo(TokenKind.SemiToken, TokenKind.CloseBraceToken);

            return new WhileStatementNode(Range(firstTok, Previous()))
            {
                Attributes = attributes,
                Condition = cond,
                Body = body,
            };
        }

        public DoWhileStatementNode ParseDoWhileStatement(List<AttributeNode> attributes)
        {
            var keywordTok = Eat(TokenKind.DoKeyword);
            var firstTok = attributes.FirstOrDefault()?.Tokens.FirstOrDefault() ?? keywordTok;
            var body = ParseStatement();

            Eat(TokenKind.WhileKeyword);
            Eat(TokenKind.OpenParenToken);

            var cond = ParseExpression();

            Eat(TokenKind.CloseParenToken);
            var semiTok = Eat(TokenKind.SemiToken);
            RecoverTo(TokenKind.SemiToken);

            return new DoWhileStatementNode(Range(firstTok, semiTok))
            {
                Attributes = attributes,
                Body = body,
                Condition = cond,
            };
        }

        public IfStatementNode ParseIfStatement(List<AttributeNode> attributes)
        {
            var keywordTok = Eat(TokenKind.IfKeyword);
            var firstTok = attributes.FirstOrDefault()?.Tokens.FirstOrDefault() ?? keywordTok;
            Eat(TokenKind.OpenParenToken);

            var cond = ParseExpression();

            Eat(TokenKind.CloseParenToken);

            var body = ParseStatement();

            StatementNode elseClause = null;
            if (Match(TokenKind.ElseKeyword))
            {
                Eat(TokenKind.ElseKeyword);
                elseClause = ParseStatement();
            }
            RecoverTo(TokenKind.SemiToken, TokenKind.CloseBraceToken);

            return new IfStatementNode(Range(firstTok, Previous()))
            {
                Attributes = attributes,
                Condition = cond,
                Body = body,
                ElseClause = elseClause,
            };
        }

        public SwitchStatementNode ParseSwitchStatement(List<AttributeNode> attributes)
        {
            var keywordTok = Eat(TokenKind.SwitchKeyword);
            var firstTok = attributes.FirstOrDefault()?.Tokens.FirstOrDefault() ?? keywordTok;
            Eat(TokenKind.OpenParenToken);
            var expr = ParseExpression();
            Eat(TokenKind.CloseParenToken);
            Eat(TokenKind.OpenBraceToken);

            List<SwitchClauseNode> switchClauses = new List<SwitchClauseNode>();
            while (Match(TokenKind.CaseKeyword, TokenKind.DefaultKeyword))
            {
                var clauseStartTok = Peek();
                List<SwitchLabelNode> switchLabels = new List<SwitchLabelNode>();
                while (Match(TokenKind.CaseKeyword, TokenKind.DefaultKeyword))
                {
                    if (Match(TokenKind.CaseKeyword))
                    {
                        var caseTok = Eat(TokenKind.CaseKeyword);
                        var caseExpr = ParseExpression();
                        Eat(TokenKind.ColonToken);
                        switchLabels.Add(new SwitchCaseLabelNode(Range(caseTok, Previous())) { Value = caseExpr });
                    }
                    else
                    {
                        var defaultTok = Eat(TokenKind.DefaultKeyword);
                        Eat(TokenKind.ColonToken);
                        switchLabels.Add(new SwitchDefaultLabelNode(Range(defaultTok, Previous())) { });
                    }
                }

                List<StatementNode> statements = ParseMany0(
                    () => !Match(TokenKind.CloseBraceToken, TokenKind.CaseKeyword, TokenKind.DefaultKeyword),
                    ParseStatement);
                switchClauses.Add(new SwitchClauseNode(Range(clauseStartTok, Previous())) { Labels = switchLabels, Statements = statements });
            }

            var closeTok = Eat(TokenKind.CloseBraceToken);
            RecoverTo(TokenKind.CloseBraceToken);

            return new SwitchStatementNode(Range(firstTok, closeTok))
            {
                Attributes = attributes,
                Expression = expr,
                Clauses = switchClauses,
            };
        }

        public TechniqueNode ParseTechnique()
        {
            var keywordTok = Eat(TokenKind.TechniqueKeyword, TokenKind.Technique10Keyword, TokenKind.Technique11Keyword);
            int version = keywordTok.Kind == TokenKind.Technique10Keyword ? 10 : 11;

            UserDefinedNamedTypeNode name = null;
            if (Match(TokenKind.IdentifierToken))
            {
                name = ParseUserDefinedNamedType();
            }

            List<VariableDeclarationStatementNode> annotations = new List<VariableDeclarationStatementNode>();
            if (Match(TokenKind.LessThanToken))
            {
                Eat(TokenKind.LessThanToken);
                annotations = ParseMany0(() => !Match(TokenKind.GreaterThanToken), () => ParseVariableDeclarationStatement(new List<AttributeNode>()));
                Eat(TokenKind.GreaterThanToken);
            }

            Eat(TokenKind.OpenBraceToken);
            var passes = ParseMany0(TokenKind.PassKeyword, ParsePass);
            Eat(TokenKind.CloseBraceToken);

            if (Match(TokenKind.SemiToken))
            {
                Eat(TokenKind.SemiToken);
            }
            RecoverTo(TokenKind.SemiToken, TokenKind.CloseBraceToken);

            return new TechniqueNode(Range(keywordTok, Previous()))
            {
                Name = name,
                Annotations = annotations,
                Version = version,
                Passes = passes
            };
        }

        public PassNode ParsePass()
        {
            var keywordTok = Eat(TokenKind.PassKeyword);
            UserDefinedNamedTypeNode name = null;
            if (Match(TokenKind.IdentifierToken))
            {
                name = ParseUserDefinedNamedType();
            }

            List<VariableDeclarationStatementNode> annotations = new List<VariableDeclarationStatementNode>();
            if (Match(TokenKind.LessThanToken))
            {
                Eat(TokenKind.LessThanToken);
                annotations = ParseMany0(() => !Match(TokenKind.GreaterThanToken), () => ParseVariableDeclarationStatement(new List<AttributeNode>()));
                Eat(TokenKind.GreaterThanToken);
            }

            Eat(TokenKind.OpenBraceToken);
            var statements = ParseMany0(() => !Match(TokenKind.CloseBraceToken), () =>
            {
                if (TryParse(ParseStatement, out var stmt))
                {
                    return stmt;
                }
                // Assume state property
                else
                {
                    return ParseStateProperty();
                }
            });
            var closeTok = Eat(TokenKind.CloseBraceToken);
            RecoverTo(TokenKind.CloseBraceToken);

            return new PassNode(Range(keywordTok, closeTok))
            {
                Name = name,
                Annotations = annotations,
                Statements = statements
            };
        }

        private bool IsNextPreProcessorDirective()
        {
            switch (Peek().Kind)
            {
                case TokenKind.DefineDirectiveKeyword:
                case TokenKind.IncludeDirectiveKeyword:
                case TokenKind.LineDirectiveKeyword:
                case TokenKind.UndefDirectiveKeyword:
                case TokenKind.ErrorDirectiveKeyword:
                case TokenKind.PragmaDirectiveKeyword:
                case TokenKind.IfDirectiveKeyword:
                case TokenKind.IfdefDirectiveKeyword:
                case TokenKind.IfndefDirectiveKeyword:
                    return true;
                default:
                    return false;
            }
        }

        public PreProcessorDirectiveNode ParsePreProcessorDirective(Func<HLSLSyntaxNode> recurse)
        {
            var next = Peek();
            switch (next.Kind)
            {
                case TokenKind.DefineDirectiveKeyword:
                    return ParseDefineDirective();
                case TokenKind.IncludeDirectiveKeyword:
                    return ParseIncludeDirective();
                case TokenKind.LineDirectiveKeyword:
                    return ParseLineDirective();
                case TokenKind.UndefDirectiveKeyword:
                    return ParseUndefDirective();
                case TokenKind.ErrorDirectiveKeyword:
                    return ParseErrorDirective();
                case TokenKind.PragmaDirectiveKeyword:
                    return ParsePragmaDirective();
                case TokenKind.IfDirectiveKeyword:
                    return ParseIfDirective(recurse, false);
                case TokenKind.IfdefDirectiveKeyword:
                    return ParseIfDefDirective(recurse);
                case TokenKind.IfndefDirectiveKeyword:
                    return ParseIfNotDefDirective(recurse);
                default:
                    Error("a valid preprocessor directive", next);
                    return null;
            }
        }

        public PreProcessorDirectiveNode ParseDefineDirective()
        {
            var keywordTok = Eat(TokenKind.DefineDirectiveKeyword);
            string ident = base.ParseIdentifier();
            
            // Function like
            if (Match(TokenKind.OpenFunctionLikeMacroParenToken))
            {
                Eat(TokenKind.OpenFunctionLikeMacroParenToken);
                var args = ParseSeparatedList0(TokenKind.CloseParenToken, TokenKind.CommaToken, base.ParseIdentifier);
                Eat(TokenKind.CloseParenToken);
                var tokens = ParseMany0(() => !Match(TokenKind.EndDirectiveToken), () => Advance());
                var endTok = Eat(TokenKind.EndDirectiveToken);
                RecoverTo(TokenKind.EndDirectiveToken);
                return new FunctionLikeMacroNode(Range(keywordTok, endTok))
                {
                    Name = ident,
                    Arguments = args,
                    Value = tokens,
                };
            }
            else
            {
                var tokens = ParseMany0(() => !Match(TokenKind.EndDirectiveToken), () => Advance());
                var endTok = Eat(TokenKind.EndDirectiveToken);
                RecoverTo(TokenKind.EndDirectiveToken);
                return new ObjectLikeMacroNode(Range(keywordTok, endTok))
                {
                    Name = ident,
                    Value = tokens,
                };
            }
        }

        public IncludeDirectiveNode ParseIncludeDirective()
        {
            var keywordTok = Eat(TokenKind.IncludeDirectiveKeyword);
            string ident = Eat(TokenKind.StringLiteralToken, TokenKind.SystemIncludeLiteralToken).Identifier;
            var endTok = Eat(TokenKind.EndDirectiveToken);
            RecoverTo(TokenKind.EndDirectiveToken);
            return new IncludeDirectiveNode(Range(keywordTok, endTok)) { Path = ident };
        }

        public LineDirectiveNode ParseLineDirective()
        {
            var keywordTok = Eat(TokenKind.LineDirectiveKeyword);
            int line = ParseIntegerLiteral();
            var endTok = Eat(TokenKind.EndDirectiveToken);
            RecoverTo(TokenKind.EndDirectiveToken);
            return new LineDirectiveNode(Range(keywordTok, endTok)) { Line = line };
        }

        public UndefDirectiveNode ParseUndefDirective()
        {
            var keywordTok = Eat(TokenKind.UndefDirectiveKeyword);
            string ident = base.ParseIdentifier();
            var endTok = Eat(TokenKind.EndDirectiveToken);
            RecoverTo(TokenKind.EndDirectiveToken);
            return new UndefDirectiveNode(Range(keywordTok, endTok)) { Name = ident };
        }

        public ErrorDirectiveNode ParseErrorDirective()
        {
            var keywordTok = Eat(TokenKind.ErrorDirectiveKeyword);
            var tokens = ParseMany0(() => !Match(TokenKind.EndDirectiveToken), () => Advance());
            var endTok = Eat(TokenKind.EndDirectiveToken);
            RecoverTo(TokenKind.EndDirectiveToken);
            return new ErrorDirectiveNode(Range(keywordTok, endTok)) { Value = tokens };
        }

        public PragmaDirectiveNode ParsePragmaDirective()
        {
            var keywordTok = Eat(TokenKind.PragmaDirectiveKeyword);
            var tokens = ParseMany0(() => !Match(TokenKind.EndDirectiveToken), () => Advance());
            var endTok = Eat(TokenKind.EndDirectiveToken);
            RecoverTo(TokenKind.EndDirectiveToken);
            return new PragmaDirectiveNode(Range(keywordTok, endTok)) { Value = tokens };
        }

        public IfDirectiveNode ParseIfDirective(Func<HLSLSyntaxNode> recurse, bool elif)
        {
            var keywordTok = elif ? Eat(TokenKind.ElifDirectiveKeyword) : Eat(TokenKind.IfDirectiveKeyword);
            var expr = ParseExpression();
            var endTok = Eat(TokenKind.EndDirectiveToken);
            RecoverTo(TokenKind.EndDirectiveToken);
            var body = ParseMany0(() => !Match(TokenKind.ElseDirectiveKeyword, TokenKind.ElifDirectiveKeyword, TokenKind.EndifDirectiveKeyword), recurse);
            var elseClause = ParseDirectiveConditionalRemainder(recurse);
            return new IfDirectiveNode(Range(keywordTok, Previous()))
            {
                Condition = expr,
                Body = body,
                ElseClause = elseClause,
            };
        }

        public IfDefDirectiveNode ParseIfDefDirective(Func<HLSLSyntaxNode> recurse)
        {
            var keywordTok = Eat(TokenKind.IfdefDirectiveKeyword);
            string ident = base.ParseIdentifier();
            var endTok = Eat(TokenKind.EndDirectiveToken);
            RecoverTo(TokenKind.EndDirectiveToken);
            var body = ParseMany0(() => !Match(TokenKind.ElseDirectiveKeyword, TokenKind.ElifDirectiveKeyword, TokenKind.EndifDirectiveKeyword), recurse);
            var elseClause = ParseDirectiveConditionalRemainder(recurse);
            return new IfDefDirectiveNode(Range(keywordTok, Previous()))
            {
                Condition = ident,
                Body = body,
                ElseClause = elseClause,
            };
        }

        public IfNotDefDirectiveNode ParseIfNotDefDirective(Func<HLSLSyntaxNode> recurse)
        {
            var keywordTok = Eat(TokenKind.IfndefDirectiveKeyword);
            string ident = base.ParseIdentifier();
            var endTok = Eat(TokenKind.EndDirectiveToken);
            RecoverTo(TokenKind.EndDirectiveToken);
            var body = ParseMany0(() => !Match(TokenKind.ElseDirectiveKeyword, TokenKind.ElifDirectiveKeyword, TokenKind.EndifDirectiveKeyword), recurse);
            var elseClause = ParseDirectiveConditionalRemainder(recurse);
            return new IfNotDefDirectiveNode(Range(keywordTok, Previous()))
            {
                Condition = ident,
                Body = body,
                ElseClause = elseClause,
            };
        }

        public PreProcessorDirectiveNode ParseDirectiveConditionalRemainder(Func<HLSLSyntaxNode> recurse)
        {
            PreProcessorDirectiveNode elseClause = null;
            var next = Peek();
            switch (next.Kind)
            {
                case TokenKind.ElseDirectiveKeyword:
                    var keywordTok = Eat(TokenKind.ElseDirectiveKeyword);
                    Eat(TokenKind.EndDirectiveToken);
                    RecoverTo(TokenKind.EndDirectiveToken);
                    var body = ParseMany0(() => !Match(TokenKind.EndifDirectiveKeyword), recurse);
                    Eat(TokenKind.EndifDirectiveKeyword);
                    var endTokElse = Eat(TokenKind.EndDirectiveToken);
                    RecoverTo(TokenKind.EndDirectiveToken);
                    elseClause = new ElseDirectiveNode(Range(keywordTok, endTokElse)) { Body = body };
                    break;
                case TokenKind.ElifDirectiveKeyword:
                    elseClause = ParseIfDirective(recurse, true);
                    break;
                case TokenKind.EndifDirectiveKeyword:
                    Eat(TokenKind.EndifDirectiveKeyword);
                    var endTok = Eat(TokenKind.EndDirectiveToken);
                    RecoverTo(TokenKind.EndDirectiveToken);
                    break;
                default:
                    Error("a valid preprocessor directive", next);
                    break;
            }
            return elseClause;
        }
    }
}
