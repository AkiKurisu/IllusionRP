using System;
using System.Linq;
using Illusion.ShaderTools;
using Illusion.ShaderTools.Syntax.HLSL;
using Illusion.ShaderTools.Syntax.ShaderLab;
using Illusion.Rendering.Editor;

static class Program
{
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    static void Main()
    {
        string sliced = PathTracingCoverageSlice.Apply(@"
struct Surface { float alpha; float3 color; float3 transmissionTint; };
Surface PathTracingEvaluateTemplate();
float PathTracingTemplateCoverage(Surface surface) { return surface.alpha; }
void PathTracingAnyHit() { Surface surface = PathTracingEvaluateTemplate(); float3 transmission = surface.transmissionTint; }
Surface frag(float value) {
    float unusedColor = value * 9;
    float coverage = value * 0.5;
    Surface surface = (Surface)0;
    surface.color = unusedColor;
    surface.alpha = coverage;
    if (value > 0) { float unusedBranch = value * 7; surface.color = unusedBranch; }
    if (coverage < 0.1) clip(coverage - 0.1);
    surface.transmissionTint = value.xxx;
    return surface;
}");
        Check(!sliced.Contains("float unusedColor") && !sliced.Contains("float unusedBranch") && !sliced.Contains("surface.color =") &&
            sliced.Contains("surface.alpha = coverage") && sliced.Contains("clip(coverage - 0.1)") && sliced.Contains("surface.transmissionTint = value.xxx"), "Coverage dependencies and side effects");
        string mutations = PathTracingCoverageSlice.Apply(@"
struct Surface { float alpha; };
Surface PathTracingEvaluateTemplate();
float PathTracingTemplateCoverage(Surface surface) { return surface.alpha; }
void PathTracingAnyHit() {}
void SetAlpha(out float alpha) { alpha = 0.5; }
Surface frag(float value) { float alpha = value; SetAlpha(alpha); alpha++; { float alpha = 0.1; } Surface surface; surface.alpha = alpha; return surface; }");
        Check(mutations.Contains("SetAlpha(alpha)") && mutations.Contains("alpha++") && mutations.Contains("float alpha = value"), "Coverage out parameters, mutation and shadowing");
        string indexed = PathTracingCoverageSlice.Apply(@"
struct Surface { float alpha; };
Surface PathTracingEvaluateTemplate();
float PathTracingTemplateCoverage(Surface surface) { return surface.alpha; }
void PathTracingAnyHit() {}
Surface frag(float value) { float values[2]; int selected = 1; values[selected] = value; Surface surface; surface.alpha = values[1]; return surface; }");
        Check(indexed.Contains("int selected = 1"), "Coverage array write index dependency");
        ShaderSource.ParseHlsl(sliced);
        var shader = ShaderSource.ParseShaderLab(@"Shader ""Test"" {
 HLSLINCLUDE
 float shaderScope;
 ENDHLSL
 SubShader {
 HLSLINCLUDE
 float subScope;
 ENDHLSL
 Pass { HLSLPROGRAM
 float otherPass;
 ENDHLSL }
 Pass { Tags { ""LightMode"" = ""PathTracing"" } HLSLPROGRAM
 float selected;
 ENDHLSL }
 }}");
        var pass = (ShaderCodePassNode)shader.SubShaders[0].Passes[1];
        string program = pass.ProgramBlock.Value.FullCode;
        Check(program.Contains("shaderScope") && program.Contains("subScope") &&
              program.Contains("selected") && !program.Contains("otherPass"), "ShaderLab include scope");
        var compatibility = ShaderSource.ParseHlsl(@"#line 25 ""original.hlsl""
struct sampler2D { float indices; };
cbuffer Values { float cutoff;; };
float4 F(float payload) { sampler2D s; s.indices = payload; return 0.5.xxxx * s.indices; }");
        Check(compatibility.Declarations.Count == 3 &&
              compatibility.Declarations[0].Node.Span.Start.Line == 25 &&
              compatibility.Declarations[0].Node.Span.FileName == "original.hlsl", "DXC syntax and source locations");
        Check(ShaderSource.ParseHlsl("").Declarations.Count == 0, "Empty source");
        ExpressionNode Expression(string text) =>
            ((ReturnStatementNode)((FunctionDefinitionNode)ShaderSource.ParseHlsl(
                "float F() { return " + text + "; }").Nodes[0]).Body.Statements[0]).Expression;
        Check(Expression("a=b=c+d*e") is AssignmentExpressionNode
            { Right: AssignmentExpressionNode { Right: BinaryExpressionNode { Right: BinaryExpressionNode } } },
            "Assignment association and arithmetic precedence");
        Check(Expression("1.#INF") is LiteralExpressionNode { Span.Length: 6 }, "DXC infinity literal");
        Check(Expression("a-b-c") is BinaryExpressionNode { Left: BinaryExpressionNode } &&
              Expression("a?b:c?d:e") is TernaryExpressionNode { FalseCase: TernaryExpressionNode },
            "Left association and nested conditional");
        var recursive = ShaderSource.ParseHlsl("float A() { return B(); } float B() { return A(); }");
        Check(recursive.Reachable(recursive.Find("A")).Count == 2 &&
              recursive.Reachable(recursive.Find("A")).Count == 2, "Lazy cyclic dependencies");
        var modern = ShaderSource.ParseHlsl(@"
enum class Mode : uint { First = 0, Second = 1 };
struct Forward;
struct Forward {
    enum Flags : uint { Visible = 1 };
    struct Nested { float value; };
    static Forward make(float texture, uint pass) { Forward x; return x; }
};
_Static_assert((uint)Mode::First == 0, ""mode"");
float Global() { return 1; }
float Root() { Forward f = Forward::make(1, 0); return ::Global() + 0.xxx.x; }
");
        var modernClosure = modern.Reachable(modern.Find("Root"));
        Check(modernClosure.Any(d => d.Names.Contains("Forward")) &&
              modernClosure.Any(d => d.Names.Contains("Global")), "Enums, nested types, forward declarations and qualified calls");

        var dependencies = ShaderSource.ParseHlsl(@"
static float value = 2;
static float initialized = value;
float Overload(float x) { return x; }
float Overload(float2 x) { return x.x; }
struct Data { float value; };
float Root(Data data) {
    { float value = 0; value++; }
    for (int value = 0; value < 1; value++) {}
    return Overload(initialized) + data.value;
}
float Shadow() { float value = 1; return value; }
float Field(Data data) { return data.value; }
namespace A { float value; float Read() { return value; } }
namespace B { float value; float Read() { return A::Read(); } }
");
        var closure = dependencies.Reachable(dependencies.Find("Root"));
        Check(closure.Count(d => d.Names.Contains("Overload")) == 2 &&
              closure.Any(d => d.Names.Contains("value")), "Overloads and initializer dependencies");
        Check(!dependencies.Find("Shadow").Single().Dependencies.Any(), "Local shadowing");
        Check(dependencies.Find("Field").Single().Dependencies.All(d => d.Names.Contains("Data")), "Member access");
        Check(dependencies.Find("Read", "B").Single().Dependencies.Single().Scope == "A", "Qualified lookup");

        const string source = @"
cbuffer UnityPerMaterial { float4 color; float cutoff; }
Texture2D baseMap;
SamplerState sampler_baseMap;
float Shared(float x) { return x * cutoff; }
float Shared(float2 x) { return x.x; }
float OnlyClosest(float x) { return x + color.x; }
void PathTracingClosestHit(inout IllusionPathPayload payload, AttributeData attributes) { payload.hitT = OnlyClosest(Shared(1)); }
void PathTracingAnyHit(inout IllusionPathPayload payload, AttributeData attributes) { if (Shared(1) < cutoff) IgnoreHit(); }
";
        var unit = PathTracingInlineUnitTransform.Transform(source, "Test");
        Check(unit.Body.Contains("float OnlyClosest(") && !unit.AnyHitBody.Contains("float OnlyClosest("), "Entry-specific reachability");
        Check(unit.AnyHitBody.Contains("float Shared(float2 x)") && unit.AnyHitBody.Contains("IgnoreHit()"), "AnyHit overloads and coverage");
        Check(unit.RecordSize == 48 && unit.Constants.Select(c => c.Offset).SequenceEqual(new[] { 16, 32 }), "Material ABI");
        foreach (string field in new[] { "float4x4 transformMatrix", "double wide", "float values[2]" })
        {
            try { PathTracingInlineUnitTransform.Transform("cbuffer UnityPerMaterial {" + field + ";}", "Test"); }
            catch (InvalidOperationException error) when (error.Message.Contains(field.Split(' ')[1].Split('[')[0])) { continue; }
            throw new Exception("Unsupported field was silently accepted: " + field);
        }
        string remaining = PathTracingInlineUnitTransform.RemoveDeclared(
            "float F(float x){return x;} float F(float2 x){return x.x;} namespace N { float value; }",
            "float F(float x){return x;} float value;");
        Check(!remaining.Contains("F(float x)") && remaining.Contains("F(float2 x)") &&
              remaining.Contains("float value;"), "Declaration identity includes signature and scope");
        Check(!PathTracingInlineUnitTransform.RemoveDeclared(
            "namespace N { cbuffer Shared { float4 values[4]; }; }",
            "cbuffer Shared { float4 values[4]; };").Contains("cbuffer"), "DXC nested cbuffer identity");

        const string geometry = @"namespace Test {
static float g_PathTracingUVFootprint;
float SafeNormalize(float x) { return x; }
float PathTracingFetchVertex(uint i) { return i; }
float PathTracingGetHitContext(uint i) { g_PathTracingUVFootprint = 1; return SafeNormalize(PathTracingFetchVertex(i)); }
}";
        Check(PathTracingInlineUnitTransform.HitContextSignature(geometry) != null, "Known geometry sharing");
        Check(PathTracingInlineUnitTransform.HitContextSignature(geometry.Replace("PathTracingGetHitContext(uint i)",
            "PathTracingGetHitContext(inout uint i)")) == null, "Geometry parameter mutation");
        Check(PathTracingInlineUnitTransform.HitContextSignature(geometry.Replace("return i;", "return Custom(i);")) == null, "Unknown geometry dependency");
        Check(PathTracingInlineUnitTransform.HitContextSignature(geometry.Replace("return i;", "globalState = i; return i;")) == null, "Geometry global writes");
        Console.WriteLine("PASS: syntax compatibility, scoped dependencies, evaluator conversion, material layout and geometry sharing.");
    }
}
