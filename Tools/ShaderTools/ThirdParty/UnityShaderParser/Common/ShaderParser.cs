using System.Collections.Generic;
using System.Linq;
using Illusion.ShaderTools.Syntax.HLSL;
using Illusion.ShaderTools.Syntax.HLSL.PreProcessor;
using Illusion.ShaderTools.Syntax.ShaderLab;

namespace Illusion.ShaderTools.Syntax.Common
{
    public static class ShaderParser
    {
        private static HLSLParserConfig DefaultHLSLConfig = new HLSLParserConfig();
        private static ShaderLabParserConfig DefaultShaderLabConfig = new ShaderLabParserConfig();

        public static ShaderNode ParseUnityShader(string source, ShaderLabParserConfig config, out List<Diagnostic> diagnostics)
        {
            var tokens = ShaderLabLexer.Lex(source, config.BasePath, config.FileName, config.ThrowExceptionOnError, out var lexerDiags);
            var rootNode = ShaderLabParser.ParseShader(tokens, config, out var parserDiags);
            diagnostics = lexerDiags.Concat(parserDiags).ToList();
            return rootNode;
        }
        public static ShaderNode ParseUnityShader(string source, ShaderLabParserConfig config) => ParseUnityShader(source, config, out _);
        public static ShaderNode ParseUnityShader(string source, out List<Diagnostic> diagnostics) => ParseUnityShader(source, DefaultShaderLabConfig, out diagnostics);
        public static ShaderNode ParseUnityShader(string source) => ParseUnityShader(source, DefaultShaderLabConfig, out _);

        public static List<HLSLSyntaxNode> ParseTopLevelDeclarations(string source, HLSLParserConfig config, out List<Diagnostic> diagnostics, out List<string> pragmas)
        {
            var tokens = HLSLLexer.Lex(source, config.BasePath, config.FileName, config.ThrowExceptionOnError, out var lexerDiags);
            var decls = HLSLParser.ParseTopLevelDeclarations(tokens, config, out var parserDiags, out pragmas);
            diagnostics = lexerDiags.Concat(parserDiags).ToList();
            return decls;
        }
        public static List<HLSLSyntaxNode> ParseTopLevelDeclarations(string source, HLSLParserConfig config) => ParseTopLevelDeclarations(source, config, out _, out _);
        public static List<HLSLSyntaxNode> ParseTopLevelDeclarations(string source, out List<Diagnostic> diagnostics, out List<string> pragmas) => ParseTopLevelDeclarations(source, DefaultHLSLConfig, out diagnostics, out pragmas);
        public static List<HLSLSyntaxNode> ParseTopLevelDeclarations(string source) => ParseTopLevelDeclarations(source, DefaultHLSLConfig, out _, out _);

        public static HLSLSyntaxNode ParseTopLevelDeclaration(string source, HLSLParserConfig config, out List<Diagnostic> diagnostics, out List<string> pragmas)
        {
            var tokens = HLSLLexer.Lex(source, config.BasePath, config.FileName, config.ThrowExceptionOnError, out var lexerDiags);
            var decl = HLSLParser.ParseTopLevelDeclaration(tokens, config, out var parserDiags, out pragmas);
            diagnostics = lexerDiags.Concat(parserDiags).ToList();
            return decl;
        }
        public static HLSLSyntaxNode ParseTopLevelDeclaration(string source, HLSLParserConfig config) => ParseTopLevelDeclaration(source, config, out _, out _);
        public static HLSLSyntaxNode ParseTopLevelDeclaration(string source, out List<Diagnostic> diagnostics, out List<string> pragmas) => ParseTopLevelDeclaration(source, DefaultHLSLConfig, out diagnostics, out pragmas);
        public static HLSLSyntaxNode ParseTopLevelDeclaration(string source) => ParseTopLevelDeclaration(source, DefaultHLSLConfig, out _, out _);

        public static List<StatementNode> ParseStatements(string source, HLSLParserConfig config, out List<Diagnostic> diagnostics, out List<string> pragmas)
        {
            var tokens = HLSLLexer.Lex(source, config.BasePath, config.FileName, config.ThrowExceptionOnError, out var lexerDiags);
            var stmt = HLSLParser.ParseStatements(tokens, config, out var parserDiags, out pragmas);
            diagnostics = lexerDiags.Concat(parserDiags).ToList();
            return stmt;
        }
        public static List<StatementNode> ParseStatements(string source, HLSLParserConfig config) => ParseStatements(source, config, out _, out _);
        public static List<StatementNode> ParseStatements(string source, out List<Diagnostic> diagnostics, out List<string> pragmas) => ParseStatements(source, DefaultHLSLConfig, out diagnostics, out pragmas);
        public static List<StatementNode> ParseStatements(string source) => ParseStatements(source, DefaultHLSLConfig, out _, out _);

        public static StatementNode ParseStatement(string source, HLSLParserConfig config, out List<Diagnostic> diagnostics, out List<string> pragmas)
        {
            var tokens = HLSLLexer.Lex(source, config.BasePath, config.FileName, config.ThrowExceptionOnError, out var lexerDiags);
            var stmt = HLSLParser.ParseStatement(tokens, config, out var parserDiags, out pragmas);
            diagnostics = lexerDiags.Concat(parserDiags).ToList();
            return stmt;
        }
        public static StatementNode ParseStatement(string source, HLSLParserConfig config) => ParseStatement(source, config, out _, out _);
        public static StatementNode ParseStatement(string source, out List<Diagnostic> diagnostics, out List<string> pragmas) => ParseStatement(source, DefaultHLSLConfig, out diagnostics, out pragmas);
        public static StatementNode ParseStatement(string source) => ParseStatement(source, DefaultHLSLConfig, out _, out _);

        public static ExpressionNode ParseExpression(string source, HLSLParserConfig config, out List<Diagnostic> diagnostics, out List<string> pragmas)
        {
            var tokens = HLSLLexer.Lex(source, config.BasePath, config.FileName, config.ThrowExceptionOnError, out var lexerDiags);
            var expr = HLSLParser.ParseExpression(tokens, config, out var parserDiags, out pragmas);
            diagnostics = lexerDiags.Concat(parserDiags).ToList();
            return expr;
        }
        public static ExpressionNode ParseExpression(string source, HLSLParserConfig config) => ParseExpression(source, config, out _, out _);
        public static ExpressionNode ParseExpression(string source, out List<Diagnostic> diagnostics, out List<string> pragmas) => ParseExpression(source, DefaultHLSLConfig, out diagnostics, out pragmas);
        public static ExpressionNode ParseExpression(string source) => ParseExpression(source, DefaultHLSLConfig, out _, out _);

        public static SubShaderNode ParseUnitySubShader(string source, ShaderLabParserConfig config, out List<Diagnostic> diagnostics)
        {
            var tokens = ShaderLabLexer.Lex(source, config.BasePath, config.FileName, config.ThrowExceptionOnError, out var lexerDiags);
            var rootNode = ShaderLabParser.ParseSubShader(tokens, config, out var parserDiags);
            diagnostics = lexerDiags.Concat(parserDiags).ToList();
            return rootNode;
        }
        public static SubShaderNode ParseUnitySubShader(string source, ShaderLabParserConfig config) => ParseUnitySubShader(source, config, out _);
        public static SubShaderNode ParseUnitySubShader(string source, out List<Diagnostic> diagnostics) => ParseUnitySubShader(source, DefaultShaderLabConfig, out diagnostics);
        public static SubShaderNode ParseUnitySubShader(string source) => ParseUnitySubShader(source, DefaultShaderLabConfig, out _);

        public static ShaderPassNode ParseUnityShaderPass(string source, ShaderLabParserConfig config, out List<Diagnostic> diagnostics)
        {
            var tokens = ShaderLabLexer.Lex(source, config.BasePath, config.FileName, config.ThrowExceptionOnError, out var lexerDiags);
            var rootNode = ShaderLabParser.ParseShaderPass(tokens, config, out var parserDiags);
            diagnostics = lexerDiags.Concat(parserDiags).ToList();
            return rootNode;
        }
        public static ShaderPassNode ParseUnityShaderPass(string source, ShaderLabParserConfig config) => ParseUnityShaderPass(source, config, out _);
        public static ShaderPassNode ParseUnityShaderPass(string source, out List<Diagnostic> diagnostics) => ParseUnityShaderPass(source, DefaultShaderLabConfig, out diagnostics);
        public static ShaderPassNode ParseUnityShaderPass(string source) => ParseUnityShaderPass(source, DefaultShaderLabConfig, out _);

        public static ShaderPropertyNode ParseUnityShaderProperty(string source, ShaderLabParserConfig config, out List<Diagnostic> diagnostics)
        {
            var tokens = ShaderLabLexer.Lex(source, config.BasePath, config.FileName, config.ThrowExceptionOnError, out var lexerDiags);
            var rootNode = ShaderLabParser.ParseShaderProperty(tokens, config, out var parserDiags);
            diagnostics = lexerDiags.Concat(parserDiags).ToList();
            return rootNode;
        }
        public static ShaderPropertyNode ParseUnityShaderProperty(string source, ShaderLabParserConfig config) => ParseUnityShaderProperty(source, config, out _);
        public static ShaderPropertyNode ParseUnityShaderProperty(string source, out List<Diagnostic> diagnostics) => ParseUnityShaderProperty(source, DefaultShaderLabConfig, out diagnostics);
        public static ShaderPropertyNode ParseUnityShaderProperty(string source) => ParseUnityShaderProperty(source, DefaultShaderLabConfig, out _);

        public static List<ShaderPropertyNode> ParseUnityShaderProperties(string source, ShaderLabParserConfig config, out List<Diagnostic> diagnostics)
        {
            var tokens = ShaderLabLexer.Lex(source, config.BasePath, config.FileName, config.ThrowExceptionOnError, out var lexerDiags);
            var rootNode = ShaderLabParser.ParseShaderProperties(tokens, config, out var parserDiags);
            diagnostics = lexerDiags.Concat(parserDiags).ToList();
            return rootNode;
        }
        public static List<ShaderPropertyNode> ParseUnityShaderProperties(string source, ShaderLabParserConfig config) => ParseUnityShaderProperties(source, config, out _);
        public static List<ShaderPropertyNode> ParseUnityShaderProperties(string source, out List<Diagnostic> diagnostics) => ParseUnityShaderProperties(source, DefaultShaderLabConfig, out diagnostics);
        public static List<ShaderPropertyNode> ParseUnityShaderProperties(string source) => ParseUnityShaderProperties(source, DefaultShaderLabConfig, out _);

        public static List<ShaderPropertyNode> ParseUnityShaderPropertyBlock(string source, ShaderLabParserConfig config, out List<Diagnostic> diagnostics)
        {
            var tokens = ShaderLabLexer.Lex(source, config.BasePath, config.FileName, config.ThrowExceptionOnError, out var lexerDiags);
            var rootNode = ShaderLabParser.ParseShaderPropertyBlock(tokens, config, out var parserDiags);
            diagnostics = lexerDiags.Concat(parserDiags).ToList();
            return rootNode;
        }
        public static List<ShaderPropertyNode> ParseUnityShaderPropertyBlock(string source, ShaderLabParserConfig config) => ParseUnityShaderPropertyBlock(source, config, out _);
        public static List<ShaderPropertyNode> ParseUnityShaderPropertyBlock(string source, out List<Diagnostic> diagnostics) => ParseUnityShaderPropertyBlock(source, DefaultShaderLabConfig, out diagnostics);
        public static List<ShaderPropertyNode> ParseUnityShaderPropertyBlock(string source) => ParseUnityShaderPropertyBlock(source, DefaultShaderLabConfig, out _);

        public static ShaderLabCommandNode ParseUnityShaderCommand(string source, ShaderLabParserConfig config, out List<Diagnostic> diagnostics)
        {
            var tokens = ShaderLabLexer.Lex(source, config.BasePath, config.FileName, config.ThrowExceptionOnError, out var lexerDiags);
            var rootNode = ShaderLabParser.ParseShaderLabCommand(tokens, config, out var parserDiags);
            diagnostics = lexerDiags.Concat(parserDiags).ToList();
            return rootNode;
        }
        public static ShaderLabCommandNode ParseUnityShaderCommand(string source, ShaderLabParserConfig config) => ParseUnityShaderCommand(source, config, out _);
        public static ShaderLabCommandNode ParseUnityShaderCommand(string source, out List<Diagnostic> diagnostics) => ParseUnityShaderCommand(source, DefaultShaderLabConfig, out diagnostics);
        public static ShaderLabCommandNode ParseUnityShaderCommand(string source) => ParseUnityShaderCommand(source, DefaultShaderLabConfig, out _);

        public static List<ShaderLabCommandNode> ParseUnityShaderCommands(string source, ShaderLabParserConfig config, out List<Diagnostic> diagnostics)
        {
            var tokens = ShaderLabLexer.Lex(source, config.BasePath, config.FileName, config.ThrowExceptionOnError, out var lexerDiags);
            var rootNode = ShaderLabParser.ParseShaderLabCommands(tokens, config, out var parserDiags);
            diagnostics = lexerDiags.Concat(parserDiags).ToList();
            return rootNode;
        }
        public static List<ShaderLabCommandNode> ParseUnityShaderCommands(string source, ShaderLabParserConfig config) => ParseUnityShaderCommands(source, config, out _);
        public static List<ShaderLabCommandNode> ParseUnityShaderCommands(string source, out List<Diagnostic> diagnostics) => ParseUnityShaderCommands(source, DefaultShaderLabConfig, out diagnostics);
        public static List<ShaderLabCommandNode> ParseUnityShaderCommands(string source) => ParseUnityShaderCommands(source, DefaultShaderLabConfig, out _);

        public static List<Token<HLSL.TokenKind>> PreProcessToTokens(string source, HLSLParserConfig config, out List<Diagnostic> diagnostics, out List<string> pragmas)
        {
            var tokens = HLSLLexer.Lex(source, config.BasePath, config.FileName, config.ThrowExceptionOnError, out var lexerDiags);
            var ppTokens = HLSLPreProcessor.PreProcess(
                tokens,
                config.ThrowExceptionOnError,
                config.DiagnosticFilter,
                config.PreProcessorMode,
                config.BasePath,
                config.IncludeResolver,
                config.Defines,
                out pragmas,
                out var ppDiags);
            diagnostics = lexerDiags.Concat(ppDiags).ToList();
            return ppTokens;
        }
        public static List<Token<HLSL.TokenKind>> PreProcessToTokens(string source, HLSLParserConfig config) => PreProcessToTokens(source, config, out _, out _);
        public static List<Token<HLSL.TokenKind>> PreProcessToTokens(string source, out List<Diagnostic> diagnostics, out List<string> pragmas) => PreProcessToTokens(source, DefaultHLSLConfig, out diagnostics, out pragmas);
        public static List<Token<HLSL.TokenKind>> PreProcessToTokens(string source) => PreProcessToTokens(source, DefaultHLSLConfig, out _, out _);

        public static string PreProcessToString(string source, HLSLParserConfig config, out List<Diagnostic> diagnostics, out List<string> pragmas)
        {
            return HLSLSyntaxFacts.TokensToString(PreProcessToTokens(source, config, out diagnostics, out pragmas));
        }
        public static string PreProcessToString(string source, HLSLParserConfig config) => PreProcessToString(source, config, out _, out _);
        public static string PreProcessToString(string source, out List<Diagnostic> diagnostics, out List<string> pragmas) => PreProcessToString(source, DefaultHLSLConfig, out diagnostics, out pragmas);
        public static string PreProcessToString(string source) => PreProcessToString(source, DefaultHLSLConfig, out _, out _);
    }
}
