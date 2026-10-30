using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace OmniEurope.Installer.Analyzers;

/// <summary>
/// GEN008 (Universal): forbids <c>partial</c> on type declarations unless technically required.
/// One class per file; if a file is too large, extract a separate class rather than splitting
/// into partials. The three legitimate exceptions are detected and skipped:
/// <list type="bullet">
/// <item>EF Core - anything under a <c>Migrations</c> folder.</item>
/// <item>Code-behind - files ending in <c>.razor.cs</c> (Blazor) or <c>.xaml.cs</c> (MAUI/XAML,
/// whose generated half lives in <c>*.xaml.g.cs</c> under <c>obj/</c>).</item>
/// <item>Source generators - a type that declares a bodiless <c>partial</c> member
/// (the half a generator implements, e.g. <c>[LoggerMessage]</c>, <c>[GeneratedRegex]</c>).</item>
/// </list>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NoPartialTypeAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "GEN008";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Avoid partial classes",
        "Type '{0}' is partial - use one class per file; extract a separate class instead of splitting into partials",
        "OmniEurope.Installer.Structure",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "partial spreads one type across files, hiding size and weakening the one-class-per-file convention. " +
                     "Only EF generated models, Blazor code-behind, and source-generator pairings legitimately need it.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(
            AnalyzeType,
            SyntaxKind.ClassDeclaration,
            SyntaxKind.StructDeclaration,
            SyntaxKind.RecordDeclaration,
            SyntaxKind.RecordStructDeclaration,
            SyntaxKind.InterfaceDeclaration);
    }

    private static void AnalyzeType(SyntaxNodeAnalysisContext context)
    {
        var type = (TypeDeclarationSyntax)context.Node;

        // Only types carrying the partial modifier are in scope.
        var partialToken = type.Modifiers.FirstOrDefault(m => m.IsKind(SyntaxKind.PartialKeyword));
        if (partialToken == default) return;

        // Exception 1 - EF Core: anything under a Migrations folder (IndexOf: netstandard2.0-safe).
        var filePath = type.SyntaxTree.FilePath;
        if (filePath.IndexOf("Migrations", System.StringComparison.OrdinalIgnoreCase) >= 0) return;

        // Exception 2 - code-behind: the *.razor.cs (Blazor) or *.xaml.cs (MAUI/XAML) half, whose
        // generated counterpart lives in *.razor.g.cs / *.xaml.g.cs under obj/.
        if (filePath.EndsWith(".razor.cs", System.StringComparison.OrdinalIgnoreCase) ||
            filePath.EndsWith(".xaml.cs", System.StringComparison.OrdinalIgnoreCase)) return;

        // Exception 3 - source generators: the type declares a bodiless partial member the
        // generator implements (e.g. [LoggerMessage], [GeneratedRegex], [JsonSerializable]).
        if (DeclaresGeneratedPartialMember(type)) return;

        var diagnostic = Diagnostic.Create(Rule, partialToken.GetLocation(), type.Identifier.Text);
        context.ReportDiagnostic(diagnostic);
    }

    private static bool DeclaresGeneratedPartialMember(TypeDeclarationSyntax type)
    {
        foreach (var member in type.Members)
        {
            switch (member)
            {
                case MethodDeclarationSyntax { Body: null, ExpressionBody: null } method
                    when method.Modifiers.Any(SyntaxKind.PartialKeyword):
                    return true;
                case PropertyDeclarationSyntax property
                    when property.Modifiers.Any(SyntaxKind.PartialKeyword):
                    return true;
            }
        }

        return false;
    }
}
