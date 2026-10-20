using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Illusion.ShaderTools.Syntax.Common;

namespace Illusion.ShaderTools.Syntax.HLSL.PreProcessor
{
    using HLSLToken = Token<TokenKind>;

    [Flags]
    public enum PreProcessorMode
    {
        // Common combinations

        /// <summary>
        /// Don't do anything, just pass the tokens through the preprocesor as-is.
        /// </summary>
        DoNothing = 0,

        /// <summary>
        /// Expand everything. This will cause the preprocessor to act exactly like the real HLSL preprocessor.
        /// </summary>
        ExpandAll = ExpandIncludeDirectives | ExpandConditionalDirectives | ExpandLineDirectives | ExpandDefineDirectives |
            ExpandUndefDirectives | ExpandErrorDirectives | ExpandPragmaDirectives | ExpandMacroInvocations,

        /// <summary>
        /// Expand only #include directives and ignore everything else.
        /// </summary>
        ExpandIncludesOnly = ExpandIncludeDirectives,

        /// <summary>
        /// Expand everything except for #include directives.
        /// </summary>
        ExpandAllExceptIncludes = ExpandAll & ~ExpandIncludeDirectives,

        /// <summary>
        /// Expand macro invocations and pragmas, but nothing else.
        /// </summary>
        ExpandMacroInvocationsAndPragmas = ExpandMacroInvocations | ExpandPragmaDirectives,

        // Flags

        /// <summary>
        /// Whether to expand #include directives.
        /// </summary>
        ExpandIncludeDirectives     = 1 << 0,

        /// <summary>
        /// Whether to expand conditional directives such as #if and #ifdef.
        /// </summary>
        ExpandConditionalDirectives = 1 << 1,

        /// <summary>
        /// Whether to expand #line directives. This kind of directive modifies the span of encountered tokens.
        /// </summary>
        ExpandLineDirectives        = 1 << 2,

        /// <summary>
        /// Whether to expand #define directives.
        /// </summary>
        ExpandDefineDirectives      = 1 << 3,

        /// <summary>
        /// Whether to expand #undef directives.
        /// </summary>
        ExpandUndefDirectives       = 1 << 4,

        /// <summary>
        /// Whether to expand #error directives. This kind of directives produces error diagnostics.
        /// </summary>
        ExpandErrorDirectives       = 1 << 5,

        /// <summary>
        /// Whether to expand #pragma directives.
        /// </summary>
        ExpandPragmaDirectives      = 1 << 6,

        /// <summary>
        /// Whether to expand macro invocations, i.e. invocations of macros defined using the #define directive.
        /// </summary>
        ExpandMacroInvocations      = 1 << 7,

        /// <summary>
        /// Whether to strip directives from the source code. This flag can not be combined with other flags.
        /// </summary>
        StripDirectives             = 1 << 8,

        // TODO: Option to embed directives into tokens
    }

    internal struct Macro
    {
        public bool FunctionLike;
        public string Name;
        public List<string> Parameters;
        public List<HLSLToken> Tokens;
    }

    public class HLSLPreProcessor : BaseParser<TokenKind>
    {
        protected override TokenKind StringLiteralTokenKind => TokenKind.StringLiteralToken;
        protected override TokenKind IntegerLiteralTokenKind => TokenKind.IntegerLiteralToken;
        protected override TokenKind FloatLiteralTokenKind => TokenKind.FloatLiteralToken;
        protected override TokenKind IdentifierTokenKind => TokenKind.IdentifierToken;
        protected override TokenKind InvalidTokenKind => TokenKind.InvalidToken;
        protected override ParserStage Stage => ParserStage.HLSLPreProcessing;

        protected string basePath;
        protected string fileName;
        protected IPreProcessorIncludeResolver includeResolver;

        protected int lineOffset = 0;
        internal Dictionary<string, Macro> defines = new Dictionary<string, Macro>();

        protected List<HLSLToken> outputTokens = new List<HLSLToken>();
        protected List<string> outputPragmas = new List<string>();

        protected List<SyntaxTrivia> pendingLeadingTrivia = new List<SyntaxTrivia>();
        protected List<SyntaxTrivia> pendingMacroPostTrivia = new List<SyntaxTrivia>();
        protected void AddLeadingTrivia(HLSLToken tok)
        {
            if (tok.RawLeadingTrivia != null)
                pendingLeadingTrivia.AddRange(tok.RawLeadingTrivia);
        }
        protected HLSLToken EmitWithSourceTrivia(HLSLToken source, SourceSpan span, SourceSpan originalSpan)
        {
            List<SyntaxTrivia> leading = source.RawLeadingTrivia;
            if (pendingLeadingTrivia.Count > 0)
            {
                var combined = new List<SyntaxTrivia>(pendingLeadingTrivia.Count + (leading?.Count ?? 0));
                combined.AddRange(pendingLeadingTrivia);
                if (leading != null) combined.AddRange(leading);
                leading = combined;
                pendingLeadingTrivia.Clear();
            }
            // Fast path
            return new HLSLToken(source.Kind, source.Identifier, span, originalSpan, outputTokens.Count, leading);
        }

        protected SourceSpan AddFileContext(SourceSpan span)
        {
            string newBasePath = span.BasePath;
            string newFilePath = span.FileName;
            if (string.IsNullOrEmpty(newBasePath)) newBasePath = basePath;
            if (string.IsNullOrEmpty(newFilePath)) newFilePath = fileName;

            return new SourceSpan(
                newBasePath,
                newFilePath,
                new SourceLocation(span.Start.Line + lineOffset, span.Start.Column, span.Start.Index),
                new SourceLocation(span.End.Line + lineOffset, span.End.Column, span.End.Index));
        }

        protected void Passthrough()
        {
            var token = Advance();
            outputTokens.Add(EmitWithSourceTrivia(token, AddFileContext(token.Span), token.OriginalSpan));
        }

        public HLSLPreProcessor(List<HLSLToken> tokens, bool throwExceptionOnError, DiagnosticFlags diagnosticFilter, string basePath, IPreProcessorIncludeResolver includeResolver, Dictionary<string, string> defines)
            : base(tokens, throwExceptionOnError, diagnosticFilter)
        {
            this.basePath = basePath;
            this.includeResolver = includeResolver;

            foreach (var kvp in defines)
            {
                var localTokens = HLSLLexer.Lex(kvp.Value, null, null, false, out var localLexerDiags);
                if (localLexerDiags.Count > 0)
                {
                    Error(DiagnosticFlags.SyntaxError, $"Invalid define '{kvp.Key}' passed.");
                }
                string key = kvp.Key;
                bool functionLike = false;
                var parameters = new List<string>();
                if (kvp.Key.Contains("("))
                {
                    key = kvp.Key.Substring(0, kvp.Key.IndexOf('('));
                    var paramsLexeme = kvp.Key.Substring(kvp.Key.IndexOf('('));
                    paramsLexeme = paramsLexeme.TrimStart('(').TrimEnd(')');
                    parameters = paramsLexeme.Split(',').Select(x => x.Trim()).ToList();
                    functionLike = true;
                }
                this.defines.Add(key, new Macro
                {
                    FunctionLike = functionLike,
                    Name = key,
                    Parameters = parameters,
                    Tokens = localTokens
                });
            }
        }

        public static List<HLSLToken> PreProcess(
            List<HLSLToken> tokens,
            bool throwExceptionOnError,
            DiagnosticFlags diagnosticFilter,
            PreProcessorMode mode,
            string basePath,
            IPreProcessorIncludeResolver includeResolver,
            Dictionary<string, string> defines,
            out List<string> pragmas,
            out List<Diagnostic> diagnostics)
        {
            HLSLPreProcessor preProcessor = new HLSLPreProcessor(tokens, throwExceptionOnError, diagnosticFilter, basePath, includeResolver, defines);
            switch (mode)
            {
                case PreProcessorMode.ExpandIncludesOnly:
                    preProcessor.ExpandIncludesOnly();
                    break;
                case PreProcessorMode.StripDirectives:
                    preProcessor.StripDirectives();
                    break;
                case PreProcessorMode.DoNothing:
                    preProcessor.outputTokens = tokens;
                    break;
                default:
                    preProcessor.ExpandDirectives(mode);
                    break;
            }
            pragmas = preProcessor.outputPragmas;
            diagnostics = preProcessor.diagnostics;
            return preProcessor.outputTokens;
        }

        private new string ParseIdentifier()
        {
            if (Match(TokenKind.IdentifierToken))
            {
                return base.ParseIdentifier();
            }
            else
            {
                var identifierToken = Advance();
                if (HLSLSyntaxFacts.TryConvertKeywordToString(identifierToken.Kind, out string result))
                {
                    return result;
                }
                Error("a valid identifier", identifierToken);
                return string.Empty;
            }
        }

        protected struct PreProcessorSnapshot
        {
            public List<HLSLToken> Tokens;
            public int LineOffset;
            public int ExitPosition;
            public SourceSpan IncludeSpan;
        }

        protected Stack<PreProcessorSnapshot> fileSnapshots = new Stack<PreProcessorSnapshot>();

        protected void EnterFile(SourceSpan includeSpan, string newFileName)
        {
            string source = includeResolver.ReadFile(basePath, newFileName);
            var sourceTokens = HLSLLexer.Lex(source, basePath, fileName, throwExceptionOnError, out var diagnosticsToAdd);
            diagnostics.AddRange(diagnosticsToAdd);

            fileSnapshots.Push(new PreProcessorSnapshot
            {
                Tokens = tokens,
                LineOffset = lineOffset,
                ExitPosition = position,
                IncludeSpan = includeSpan,
            });

            position = 0;
            lineOffset = 0;
            tokens = sourceTokens;

            includeResolver.EnterFile(ref basePath, ref fileName, newFileName);
        }

        protected void ExitFile()
        {
            var snapshot = fileSnapshots.Pop();
            tokens = snapshot.Tokens;
            lineOffset = snapshot.LineOffset;
            position = snapshot.ExitPosition;

            includeResolver.ExitFile(ref basePath, ref fileName);
        }

        private void ExpandInclude(PreProcessorMode mode)
        {
            var keywordTok = Eat(TokenKind.IncludeDirectiveKeyword);
            AddLeadingTrivia(keywordTok);
            var pathToken = Eat(TokenKind.SystemIncludeLiteralToken, TokenKind.StringLiteralToken);
            var endTok = Eat(TokenKind.EndDirectiveToken);
            var includeSpan = SourceSpan.Between(keywordTok.Span, endTok.Span);
            string newFileName = pathToken.Identifier ?? string.Empty;

            EnterFile(includeSpan, newFileName);

            if (mode == PreProcessorMode.ExpandIncludesOnly)
                ExpandIncludesOnly();
            else
                ExpandDirectives(mode);

            ExitFile();
        }

        // Glues tokens together with ##, stringize with #, and evaluates defined(x) between each expansion
        private void ReplaceBetweenExpansions(List<HLSLToken> tokens)
        {
            HLSLToken LocalPeek(int i) => i < tokens.Count ? tokens[i] : InvalidToken;

            List<HLSLToken> result = new List<HLSLToken>();
            for (int i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];
                if (HLSLSyntaxFacts.TryConvertIdentifierOrKeywordToString(token, out string gluedIdentifier) && LocalPeek(i + 1).Kind == TokenKind.HashHashToken)
                {
                    SourceSpan startSpan = token.Span;
                    SourceSpan startSpanOriginal = token.OriginalSpan;
                    SourceSpan endSpan = token.Span;
                    SourceSpan endSpanOriginal = token.OriginalSpan;
                    int startPosition = token.Position;

                    i++; // identifier
                    while (LocalPeek(i).Kind == TokenKind.HashHashToken &&
                        HLSLSyntaxFacts.TryConvertIdentifierOrKeywordToString(LocalPeek(i + 1), out string nextIdentifier))
                    {
                        i++; // ##
                        var nextToken = LocalPeek(i++); // identifier
                        gluedIdentifier += nextIdentifier;
                        endSpan = nextToken.Span;
                        endSpanOriginal = nextToken.OriginalSpan;
                    }

                    var gluedToken = new HLSLToken(
                        TokenKind.IdentifierToken,
                        gluedIdentifier,
                        SourceSpan.Between(startSpan, endSpan),
                        SourceSpan.Between(startSpanOriginal, endSpanOriginal),
                        startPosition);
                    i--; // For loop continues

                    result.Add(gluedToken);
                }
                else if (token.Kind == TokenKind.IdentifierToken && token.Identifier == "defined")
                {
                    SourceSpan startSpan = token.Span;
                    SourceSpan startSpanOriginal = token.OriginalSpan;
                    int startPosition = token.Position;

                    i++; // defined
                    bool hasParen = LocalPeek(i).Kind == TokenKind.OpenParenToken;
                    if (hasParen) i++;
                    HLSLToken identifier = LocalPeek(i++);
                    SourceSpan endSpan = identifier.Span;
                    SourceSpan endSpanOriginal = identifier.OriginalSpan;
                    if (hasParen)
                    {
                        var closeParen = LocalPeek(i++);
                        endSpan = closeParen.Span;
                        endSpanOriginal = closeParen.OriginalSpan;
                    }

                    var replacedToken = new HLSLToken(
                        TokenKind.IntegerLiteralToken,
                        defines.ContainsKey(HLSLSyntaxFacts.IdentifierOrKeywordToString(identifier)) ? "1" : "0",
                        SourceSpan.Between(startSpan, endSpan),
                        SourceSpan.Between(startSpanOriginal, endSpanOriginal),
                        startPosition);
                    i--; // For loop continues

                    result.Add(replacedToken);
                }
                else if (token.Kind == TokenKind.HashToken && LocalPeek(i + 1).Kind == TokenKind.IdentifierToken)
                {
                    SourceSpan startSpan = token.Span;
                    SourceSpan startSpanOriginal = token.OriginalSpan;
                    int startPosition = token.Position;

                    i++; // Hash
                    HLSLToken identifier = LocalPeek(i++); // Identifier

                    var stringizedToken = new HLSLToken(
                        TokenKind.StringLiteralToken,
                        identifier.Identifier,
                        SourceSpan.Between(startSpan, identifier.Span),
                        SourceSpan.Between(startSpanOriginal, identifier.OriginalSpan),
                        startPosition
                    );
                    i--; // For loop continues
                    result.Add(stringizedToken);
                }
                else
                {
                    result.Add(token);
                }
            }

            tokens.Clear();
            tokens.AddRange(result);
        }

        private bool TryParseFunctionLikeMacroInvocationParameters(List<HLSLToken> tokenStream, ref int streamOffset, out List<List<HLSLToken>> parameters, out List<SyntaxTrivia> closeTrivia)
        {
            int localOffset = streamOffset + 1;

            // Setup local parser functionality (we want to parse on a secondary token stream)
            bool LocalIsAtEnd() => localOffset >= tokenStream.Count;
            HLSLToken LocalAdvance() => LocalIsAtEnd() ? InvalidToken : tokenStream[localOffset++];
            HLSLToken LocalPeek() => LocalIsAtEnd() ? InvalidToken : tokenStream[localOffset];
            bool LocalMatch(TokenKind kind) => LocalIsAtEnd() ? false : kind == tokenStream[localOffset].Kind;
            HLSLToken LocalEat(TokenKind kind)
            {
                if (!LocalMatch(kind))
                    Error(DiagnosticFlags.PreProcessorError, $"Expected token type '{kind}', got '{LocalPeek().Kind}'.");
                return LocalAdvance();
            }

            parameters = new List<List<HLSLToken>>();
            closeTrivia = null;

            var pendingArgTrivia = new List<SyntaxTrivia>();
            var parametersRef = parameters;
            void AddArgToken(HLSLToken tok)
            {
                if (pendingArgTrivia.Count > 0)
                {
                    var leading = new List<SyntaxTrivia>(pendingArgTrivia);
                    if (tok.HasLeadingTrivia) leading.AddRange(tok.RawLeadingTrivia);
                    tok = new HLSLToken(tok.Kind, tok.Identifier, tok.Span, tok.OriginalSpan, tok.Position, leading);
                    pendingArgTrivia.Clear();
                }
                parametersRef[parametersRef.Count - 1].Add(tok);
            }

            // Eat arguments if they are available
            if (LocalMatch(TokenKind.OpenParenToken))
            {
                // Always eat open paren
                var openParen = LocalEat(TokenKind.OpenParenToken);
                if (openParen.HasLeadingTrivia) pendingArgTrivia.AddRange(openParen.RawLeadingTrivia);

                // Check for special case of 0 args
                if (LocalMatch(TokenKind.CloseParenToken))
                {
                    var closeParen = LocalEat(TokenKind.CloseParenToken);
                    if (closeParen.HasLeadingTrivia) closeTrivia = new List<SyntaxTrivia>(closeParen.RawLeadingTrivia);
                    streamOffset = localOffset - 1;
                    return true;
                }

                parameters.Add(new List<HLSLToken>());

                // Parse until we have match parens, they might be nested
                int numParens = 1;
                while (numParens > 0)
                {
                    var next = LocalAdvance();
                    switch (next.Kind)
                    {
                        case TokenKind.OpenParenToken:
                            numParens++;
                            if (numParens > 1) AddArgToken(next);
                            break;
                        case TokenKind.CloseParenToken:
                            numParens--;
                            if (numParens > 0) AddArgToken(next);
                            else if (next.HasLeadingTrivia) closeTrivia = new List<SyntaxTrivia>(next.RawLeadingTrivia);
                            break;
                        case TokenKind.CommaToken when numParens == 1:
                            parameters.Add(new List<HLSLToken>());
                            if (next.HasLeadingTrivia) pendingArgTrivia.AddRange(next.RawLeadingTrivia);
                            break;
                        default:
                            AddArgToken(next);
                            break;
                    }
                }
                streamOffset = localOffset - 1;
                return true;
            }
            // If no args, it must be a regular identifier
            return false;
        }

        private List<HLSLToken> ApplyMacros()
        {
            // First, get the entire macro identifier
            List<HLSLToken> expanded = new List<HLSLToken>();
            var identifierTok = Eat(TokenKind.IdentifierToken);
            expanded.Add(identifierTok);
            string identifier = identifierTok.Identifier ?? string.Empty;

            // Check if it is a functionlike macro
            bool isFunctionLike = (defines.ContainsKey(identifier) && defines[identifier].FunctionLike) || identifier == "defined";
            if (isFunctionLike)
            {
                // If so, eat arguments if they are available
                if (Match(TokenKind.OpenParenToken))
                {
                    expanded.Add(Eat(TokenKind.OpenParenToken));
                    int numParens = 1;
                    while (numParens > 0) // Might have nested parens
                    {
                        var next = Advance();
                        if (next.Kind == TokenKind.OpenParenToken)
                            numParens++;
                        else if (next.Kind == TokenKind.CloseParenToken)
                            numParens--;
                        expanded.Add(next);
                    }
                }
                // Otherwise, it must be a regular identifier
                else
                {
                    return expanded;
                }
            }

            // Optimization: Only do this if necessary
            bool hasGlueTokenOrDefined = expanded.Any(x => x.Kind == TokenKind.HashHashToken || x.Identifier == "defined");
            if (hasGlueTokenOrDefined)
            {
                ReplaceBetweenExpansions(expanded);
            }

            HashSet<string> emptyHideset = new HashSet<string>();
            List<HashSet<string>> hidesets = Enumerable.Repeat(emptyHideset, expanded.Count).ToList();

            // Loop until we can't apply macros anymore
            while (true)
            {
                List<HLSLToken> next = new List<HLSLToken>();
                List<HashSet<string>> nextHidesets = new List<HashSet<string>>();

                // Go over each token and try to apply, adding to the hideset as we go
                bool anyThingApplied = false;
                for (int i = 0; i < expanded.Count; i++)
                {
                    HLSLToken token = expanded[i];

                    string lexeme = HLSLSyntaxFacts.IdentifierOrKeywordToString(token);
                    // If the macro matches
                    if (!hidesets[i].Contains(lexeme) && !HLSLSyntaxFacts.IsStringLikeLiteral(token.Kind) && defines.TryGetValue(lexeme, out Macro macro))
                    {
                        anyThingApplied = true;
                        HashSet<string> bodyHideset = new HashSet<string>(hidesets[i]) { lexeme };

                        // We need to replace tokens.
                        // First, check if we have a functionlike macro
                        if (macro.FunctionLike)
                        {
                            // Try to parse parameters.
                            if (!TryParseFunctionLikeMacroInvocationParameters(expanded, ref i, out var parameters, out var closeTrivia))
                            {
                                // If they aren't present, it might be a deferred function-like macro.
                                // Eat more tokens to get the parameters and retry.
                                if (Match(TokenKind.OpenParenToken))
                                {
                                    expanded.Add(Eat(TokenKind.OpenParenToken));
                                    hidesets.Add(emptyHideset);
                                    int numParens = 1;
                                    while (numParens > 0) // Might have nested parens
                                    {
                                        var nextTok = Advance();
                                        if (nextTok.Kind == TokenKind.OpenParenToken)
                                            numParens++;
                                        else if (nextTok.Kind == TokenKind.CloseParenToken)
                                            numParens--;
                                        expanded.Add(nextTok);
                                        hidesets.Add(emptyHideset);
                                    }
                                    if (!TryParseFunctionLikeMacroInvocationParameters(expanded, ref i, out parameters, out closeTrivia))
                                    {
                                        next.Add(token); // Still no luck, must be a regular identifier.
                                        nextHidesets.Add(hidesets[i]);
                                    }
                                }
                                // Otherwise, must be a regular identifier.
                                else
                                {
                                    next.Add(token);
                                    nextHidesets.Add(hidesets[i]);
                                }
                            }

                            if (parameters.Count != macro.Parameters.Count)
                                Error(DiagnosticFlags.PreProcessorError, $"Incorrect number of arguments passed to macro '{macro.Name}', expected {macro.Parameters.Count}, got {parameters.Count}.");

                            // If they are there, substitute them
                            int lastParamEndPosInNext = -1;
                            foreach (var macroToken in macro.Tokens)
                            {
                                string macroTokenLexeme = HLSLSyntaxFacts.IdentifierOrKeywordToString(macroToken);
                                int paramIndex = macro.Parameters.IndexOf(macroTokenLexeme);
                                if (paramIndex >= 0 && paramIndex < parameters.Count)
                                {
                                    var parameter = parameters[paramIndex];
                                    foreach (var parameterToken in parameter)
                                    {
                                        next.Add(new HLSLToken(parameterToken.Kind, parameterToken.Identifier, macroToken.Span, parameterToken.Span, parameterToken.Position,
                                            parameterToken.RawLeadingTrivia));
                                        nextHidesets.Add(emptyHideset);
                                    }
                                    lastParamEndPosInNext = next.Count;
                                }
                                else
                                {
                                    next.Add(macroToken);
                                    nextHidesets.Add(bodyHideset);
                                }
                            }

                            if (closeTrivia?.Count > 0)
                            {
                                // Add trivia end of last param, example:
                                // #define foo(x) x;
                                // foo(1 /*bar*/) <-- attach trivia to semicolon
                                if (lastParamEndPosInNext >= 0 && lastParamEndPosInNext < next.Count)
                                {
                                    var bodyTok = next[lastParamEndPosInNext];
                                    var newLeading = new List<SyntaxTrivia>(closeTrivia);
                                    if (bodyTok.HasLeadingTrivia) newLeading.AddRange(bodyTok.RawLeadingTrivia);
                                    next[lastParamEndPosInNext] = new HLSLToken(bodyTok.Kind, bodyTok.Identifier, bodyTok.Span, bodyTok.OriginalSpan, bodyTok.Position, newLeading);
                                }
                                // Add trivia to whatever comes next, example:
                                // #define foo(x) x
                                // foo(1 /*bar*/) <-- attach trivia to next line
                                else
                                {
                                    pendingMacroPostTrivia.AddRange(closeTrivia);
                                }
                            }
                        }
                        // If not, we can just substitute tokens directly
                        else
                        {
                            next.AddRange(macro.Tokens);
                            nextHidesets.AddRange(Enumerable.Repeat(bodyHideset, macro.Tokens.Count));
                        }
                    }
                    // Otherwise just pass the token through
                    else
                    {
                        next.Add(token);
                        nextHidesets.Add(hidesets[i]);
                    }
                }

                // Optimization: Check if anything changed - costly to replace
                if (anyThingApplied)
                {
                    ReplaceBetweenExpansions(next);
                }

                expanded = next;
                hidesets = nextHidesets;

                // If nothing was applied, stop
                if (!anyThingApplied)
                {
                    break;
                }
            }

            return expanded;
        }

        private static void ShiftPositionsToStartFrom(int start, List<HLSLToken> tokens)
        {
            for (int i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];
                var newToken = new HLSLToken(token.Kind, token.Identifier, token.Span, token.OriginalSpan, start + i);
                tokens[i] = newToken;
            }
        }

        private List<HLSLToken> SkipUntilEndOfConditional()
        {
            List<HLSLToken> skipped = new List<HLSLToken>();
            int depth = 0;
            while (true)
            {
                if (!LoopShouldContinue())
                {
                    Error(DiagnosticFlags.PreProcessorError, "Unterminated conditional directive.");
                    break;
                }

                switch (Peek().Kind)
                {
                    case TokenKind.IfdefDirectiveKeyword:
                    case TokenKind.IfndefDirectiveKeyword:
                    case TokenKind.IfDirectiveKeyword:
                        depth++;
                        skipped.Add(Advance());
                        break;

                    case TokenKind.ElseDirectiveKeyword:
                    case TokenKind.ElifDirectiveKeyword:
                        if (depth == 0)
                        {
                            return skipped;
                        }
                        else
                        {
                            skipped.Add(Advance());
                        }
                        break;

                    case TokenKind.EndifDirectiveKeyword:
                        if (depth == 0)
                        {
                            return skipped;
                        }
                        else
                        {
                            depth--;
                            skipped.Add(Advance());
                        }
                        break;

                    default:
                        skipped.Add(Advance());
                        break;
                }
            }
            return skipped;
        }

        private bool EvaluateConstExpr(List<HLSLToken> exprTokens)
        {
            bool result = ConstExpressionEvaluator.EvaluateConstExprTokens(exprTokens, throwExceptionOnError, diagnosticFilter, out var evalDiags);
            if (evalDiags.Count > 0)
            {
                foreach (var diag in evalDiags)
                {
                    Error(DiagnosticFlags.PreProcessorError, diag);
                }
                return false;
            }
            return result;
        }

        private bool EvaluateCondition(bool continued)
        {
            HLSLToken conditional = Advance();
            switch (conditional.Kind)
            {
                case TokenKind.IfdefDirectiveKeyword:
                    string ifdefName = ParseIdentifier();
                    Eat(TokenKind.EndDirectiveToken);
                    return defines.ContainsKey(ifdefName);

                case TokenKind.IfndefDirectiveKeyword:
                    string ifndefName = ParseIdentifier();
                    Eat(TokenKind.EndDirectiveToken);
                    return !defines.ContainsKey(ifndefName);

                case TokenKind.ElseDirectiveKeyword:
                    Eat(TokenKind.EndDirectiveToken);
                    if (!continued)
                    {
                        Error(DiagnosticFlags.PreProcessorError, "Unexpected #else directive - there is no conditional directive preceding it.");
                    }
                    return true;

                case TokenKind.IfDirectiveKeyword:
                case TokenKind.ElifDirectiveKeyword:
                    if (!continued && conditional.Kind == TokenKind.ElifDirectiveKeyword)
                    {
                        Error(DiagnosticFlags.PreProcessorError, "Unexpected #elif directive - there is no conditional directive preceding it.");
                    }
                    // Get the expanded tokens for the condition expression
                    List<HLSLToken> expandedConditionTokens = new List<HLSLToken>();
                    while (LoopShouldContinue() && !Match(TokenKind.EndDirectiveToken))
                    {
                        // If we find an identifier, eagerly expand (https://www.math.utah.edu/docs/info/cpp_1.html)
                        var next = Peek();
                        if (next.Kind == TokenKind.IdentifierToken)
                        {
                            expandedConditionTokens.AddRange(ApplyMacros());
                        }
                        else
                        {
                            expandedConditionTokens.Add(Advance());
                        }
                    }
                    // The C spec says we should replace any identifiers remaining after expansion with the literal 0
                    for (int i = 0; i < expandedConditionTokens.Count; i++)
                    {
                        var token = expandedConditionTokens[i];
                        if (token.Kind == TokenKind.IdentifierToken)
                        {
                            var newToken = new HLSLToken(TokenKind.IntegerLiteralToken, "0", token.Span, token.OriginalSpan, token.Position);
                            expandedConditionTokens[i] = newToken;
                        }
                    }
                    Eat(TokenKind.EndDirectiveToken);
                    // Finally evaluate the expression
                    ShiftPositionsToStartFrom(0, expandedConditionTokens);
                    return EvaluateConstExpr(expandedConditionTokens);
                default:
                    Error(DiagnosticFlags.PreProcessorError, $"Unexpected token '{conditional.Kind}', expected preprocessor directive.");
                    return false;
            }
        }

        private void ExpandConditional()
        {
            AddLeadingTrivia(Peek());
            int startPosition = position;
            List<HLSLToken> takenTokens = new List<HLSLToken>();
            bool branchTaken = false;

            bool condEvaluation = EvaluateCondition(false);

            while (true)
            {
                if (!LoopShouldContinue())
                {
                    Error(DiagnosticFlags.PreProcessorError, "Unterminated conditional directive.");
                    break;
                }

                // Eat the body
                var skipped = SkipUntilEndOfConditional();

                // If we haven't already taken a branch, and this one is true, take it
                if (!branchTaken && condEvaluation)
                {
                    branchTaken = true;
                    takenTokens = skipped;
                }

                // If we have reached the end, stop
                if (Match(TokenKind.EndifDirectiveKeyword))
                {
                    Eat(TokenKind.EndifDirectiveKeyword);
                    Eat(TokenKind.EndDirectiveToken);
                    break;
                }

                condEvaluation = EvaluateCondition(true);
            }

            // Substitution. First take away the tokens we just evaluated, then insert the substitution,
            // and rewind to the start of it
            int numTokensInDirective = position - startPosition;
            position = startPosition;
            tokens.RemoveRange(position, numTokensInDirective);
            tokens.InsertRange(position, takenTokens);
        }

        private void GlueStringLiteralsPass()
        {
            position = 0;
            lineOffset = 0;
            tokens = new List<HLSLToken>(outputTokens);
            outputTokens.Clear();
            while (LoopShouldContinue())
            {
                if (Match(TokenKind.StringLiteralToken))
                {
                    var strTok = Eat(TokenKind.StringLiteralToken);
                    string glued = strTok.Identifier ?? string.Empty;
                    SourceSpan spanStart = strTok.Span;
                    SourceSpan spanStartOriginal = strTok.OriginalSpan;
                    SourceSpan spanEnd = strTok.Span;
                    SourceSpan spanEndOriginal = strTok.OriginalSpan;
                    while (Match(TokenKind.StringLiteralToken))
                    {
                        var nextStrTok = Eat(TokenKind.StringLiteralToken);
                        glued += nextStrTok.Identifier ?? string.Empty;
                        spanEnd = nextStrTok.Span;
                        spanEndOriginal = nextStrTok.OriginalSpan;
                    }
                    var gluedSpan = SourceSpan.Between(spanStart, spanEnd);
                    var gluedSpanOriginal = SourceSpan.Between(spanStartOriginal, spanEndOriginal);
                    var gluedToken = new HLSLToken(TokenKind.StringLiteralToken, glued, gluedSpan, gluedSpanOriginal, outputTokens.Count);
                    outputTokens.Add(gluedToken);
                }
                else
                {
                    Passthrough();
                }
            }
        }

        public void ExpandDirectives(PreProcessorMode mode)
        {
            while (LoopShouldContinue())
            {
                HLSLToken next = Peek();
                switch (next.Kind)
                {
                    case TokenKind.IncludeDirectiveKeyword when mode.HasFlag(PreProcessorMode.ExpandIncludeDirectives):
                        ExpandInclude(mode);
                        break;

                    case TokenKind.LineDirectiveKeyword when mode.HasFlag(PreProcessorMode.ExpandLineDirectives):
                        int tokenLine = next.Span.Start.Line; // where we actually are
                        AddLeadingTrivia(Eat(TokenKind.LineDirectiveKeyword));
                        int targetLine = ParseIntegerLiteral(); // where we want to be
                        lineOffset = targetLine - tokenLine - 1; // calculate the offset
                        if (Match(TokenKind.StringLiteralToken))
                        {
                            Advance();
                        }
                        Eat(TokenKind.EndDirectiveToken);
                        break;

                    case TokenKind.DefineDirectiveKeyword when mode.HasFlag(PreProcessorMode.ExpandDefineDirectives):
                        AddLeadingTrivia(Eat(TokenKind.DefineDirectiveKeyword));
                        string from = ParseIdentifier();
                        List<string> args = new List<string>();
                        bool functionLike = false;
                        if (Match(TokenKind.OpenFunctionLikeMacroParenToken))
                        {
                            functionLike = true;
                            Eat(TokenKind.OpenFunctionLikeMacroParenToken);
                            args = ParseSeparatedList0(TokenKind.CloseParenToken, TokenKind.CommaToken, ParseIdentifier);
                            Eat(TokenKind.CloseParenToken);
                        }
                        List<HLSLToken> toks = ParseMany0(() => !Match(TokenKind.EndDirectiveToken), () => Advance());
                        Eat(TokenKind.EndDirectiveToken);
                        defines[from] = new Macro
                        {
                            Name = from,
                            FunctionLike = functionLike,
                            Parameters = args,
                            Tokens = toks
                        };
                        break;

                    case TokenKind.UndefDirectiveKeyword when mode.HasFlag(PreProcessorMode.ExpandUndefDirectives):
                        AddLeadingTrivia(Eat(TokenKind.UndefDirectiveKeyword));
                        string undef = ParseIdentifier();
                        Eat(TokenKind.EndDirectiveToken);
                        defines.Remove(undef);
                        break;

                    case TokenKind.ErrorDirectiveKeyword when mode.HasFlag(PreProcessorMode.ExpandErrorDirectives):
                        AddLeadingTrivia(Eat(TokenKind.ErrorDirectiveKeyword));
                        var errorToks = ParseMany0(() => !Match(TokenKind.EndDirectiveToken), () => Advance())
                            .Select(x => HLSLSyntaxFacts.TokenToString(x));
                        Eat(TokenKind.EndDirectiveToken);
                        string error = string.Join(" ", errorToks);
                        Error(DiagnosticFlags.PreProcessorError, error);
                        break;

                    case TokenKind.PragmaDirectiveKeyword when mode.HasFlag(PreProcessorMode.ExpandPragmaDirectives):
                        AddLeadingTrivia(Eat(TokenKind.PragmaDirectiveKeyword));
                        var pragmaToks = ParseMany0(() => !Match(TokenKind.EndDirectiveToken), () => Advance())
                            .Select(x => HLSLSyntaxFacts.TokenToString(x));
                        Eat(TokenKind.EndDirectiveToken);
                        string pragma = string.Join(" ", pragmaToks);
                        outputPragmas.Add(pragma);
                        break;

                    case TokenKind.IfdefDirectiveKeyword:
                    case TokenKind.IfndefDirectiveKeyword:
                    case TokenKind.IfDirectiveKeyword:
                    case TokenKind.ElifDirectiveKeyword:
                    case TokenKind.ElseDirectiveKeyword:
                    case TokenKind.EndifDirectiveKeyword:
                        if (mode.HasFlag(PreProcessorMode.ExpandConditionalDirectives))
                            ExpandConditional();
                        else
                            Passthrough();
                        break;

                    case TokenKind.IdentifierToken when mode.HasFlag(PreProcessorMode.ExpandMacroInvocations):
                        var identifierTok = Peek();
                        var startSpan = identifierTok.Span;
                        var expanded = ApplyMacros();
                        var endSpan = Previous().Span;
                        var newSpan = SourceSpan.Between(startSpan, endSpan);
                        if (expanded.Count == 0 || !ReferenceEquals(expanded[0], identifierTok))
                            AddLeadingTrivia(identifierTok);
                        foreach (var token in expanded)
                        {
                            outputTokens.Add(EmitWithSourceTrivia(token, AddFileContext(newSpan), token.Span));
                        }
                        if (pendingMacroPostTrivia.Count > 0)
                        {
                            pendingLeadingTrivia.AddRange(pendingMacroPostTrivia);
                            pendingMacroPostTrivia.Clear();
                        }
                        break;

                    // Don't emit EOF tokens from included files.
                    case TokenKind.EndOfFileToken when fileSnapshots.Count > 0:
                        AddLeadingTrivia(next);
                        return;

                    default:
                        Passthrough();
                        break;
                }
            }

            if (fileSnapshots.Count == 0)
            {
                // C spec says we need to glue adjacent string literals
                GlueStringLiteralsPass();
            }
        }

        public void ExpandIncludesOnly()
        {
            while (LoopShouldContinue())
            {
                HLSLToken next = Peek();
                if (next.Kind == TokenKind.IncludeDirectiveKeyword)
                {
                    ExpandInclude(PreProcessorMode.ExpandIncludesOnly);
                }
                // Don't emit EOF tokens from included files.
                else if (next.Kind == TokenKind.EndOfFileToken && fileSnapshots.Count > 0)
                {
                    AddLeadingTrivia(next);
                    break;
                }
                else
                {
                    Passthrough();
                }
            }
        }

        public void StripDirectives(bool expandIncludes = true)
        {
            while (LoopShouldContinue())
            {
                HLSLToken next = Peek();
                switch (next.Kind)
                {
                    case TokenKind.IncludeDirectiveKeyword:
                    case TokenKind.LineDirectiveKeyword:
                    case TokenKind.DefineDirectiveKeyword:
                    case TokenKind.UndefDirectiveKeyword:
                    case TokenKind.ErrorDirectiveKeyword:
                    case TokenKind.PragmaDirectiveKeyword:
                    case TokenKind.IfdefDirectiveKeyword:
                    case TokenKind.IfndefDirectiveKeyword:
                    case TokenKind.IfDirectiveKeyword:
                    case TokenKind.ElifDirectiveKeyword:
                    case TokenKind.ElseDirectiveKeyword:
                    case TokenKind.EndifDirectiveKeyword:
                        AddLeadingTrivia(Peek());
                        while (LoopShouldContinue() && !Match(TokenKind.EndDirectiveToken))
                        {
                            Advance();
                        }
                        if (Match(TokenKind.EndDirectiveToken))
                        {
                            Advance();
                        }
                        break;

                    default:
                        Passthrough();
                        break;
                }
            }

            // C spec says we need to glue adjacent string literals
            GlueStringLiteralsPass();
        }
    }
}
