// Copyright (c) Richasy. All rights reserved.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

namespace Rodel.MinecraftLauncher.SettingsGenerator;

/// <summary>Checks theme capture boundaries in immutable Sprout views and application style defaults.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ViewThemeReadAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor HiddenThemeRead = new(
        "RMLUI001", "Handwritten view reads the live theme",
        "'{0}' reads the live theme through '{1}'; capture immutable appearance in a tracked Control.Body and pass it into the view, or use paint-time theme tokens",
        "Theming", DiagnosticSeverity.Error, true);

    private static readonly DiagnosticDescriptor FrozenApplicationStyle = new(
        "RMLUI002", "Application default captures the live theme",
        "Application style registration reads the live theme through '{0}'; register theme-independent geometry or paint-time tokens and resolve product appearance in a tracked owner",
        "Theming", DiagnosticSeverity.Error, true);

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [HiddenThemeRead, FrozenApplicationStyle];

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeBody, SyntaxKind.PropertyDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeRegistration, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeRegistration(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (invocation.Expression is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Add" or "UseDefaultStyle" }
            || context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol method
            || (!(method.Name == "Add" && method.ContainingType.ToDisplayString() == "Sprout.Styling.AppStyleRegistry")
                && !(method.Name == "UseDefaultStyle" && method.ContainingType.ToDisplayString() == "Sprout.SproutAppBuilder")))
        {
            return;
        }

        var search = new ThemeReadSearch(context.Compilation, context.CancellationToken);
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            foreach (var (expression, symbol) in search.Reads(argument.Expression))
            {
                if (search.ReachesTheme(symbol))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        FrozenApplicationStyle, expression.GetLocation(), expression.WithoutTrivia().ToString()));
                    return;
                }
            }
        }
    }

    private static void AnalyzeBody(SyntaxNodeAnalysisContext context)
    {
        var body = (PropertyDeclarationSyntax)context.Node;
        if (context.SemanticModel.GetDeclaredSymbol(body, context.CancellationToken) is not { } property)
        {
            return;
        }

        var owner = property.ContainingType;
        var authoredView = owner.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == "Sprout.Reconcile.ViewAttribute");
        var manualView = !authoredView && owner.IsReadOnly && owner.TypeKind == TypeKind.Struct
            && owner.AllInterfaces.Any(contract => contract.ToDisplayString() == "Sprout.Reconcile.IView");
        // Generated [View] bodies belong to Sprout's SPROUT074 gate. The application only extends that boundary
        // to its handwritten readonly IView projections, which do not have a generated Body.
        if (!manualView)
        {
            return;
        }

        var search = new ThemeReadSearch(context.Compilation, context.CancellationToken);
        foreach (var (Expression, Symbol) in search.Reads(body))
        {
            if (search.ReachesTheme(Symbol))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    HiddenThemeRead, Expression.GetLocation(), property.ContainingType.Name,
                    Expression.WithoutTrivia().ToString()));
                return;
            }
        }
    }

    private sealed class ThemeReadSearch(Compilation compilation, CancellationToken cancellationToken)
    {
        private readonly HashSet<ISymbol> _visited = new(SymbolEqualityComparer.Default);

        internal bool ReachesTheme(ISymbol symbol)
        {
            cancellationToken.ThrowIfCancellationRequested();
            symbol = symbol.OriginalDefinition;
            if (symbol is IPropertySymbol { IsStatic: false } property
                && property.ContainingType.ToDisplayString() == "Sprout.Theming.Theme"
                && property.Name is "Colors" or "Mode" or "System")
            {
                return true;
            }

            if (!_visited.Add(symbol))
            {
                return false;
            }

            // Reading an instance field or auto-property consumes an already captured value. Its initializer
            // executes when the owner is constructed, which is checked separately at constructor call sites.
            if (symbol is IFieldSymbol { IsStatic: false })
            {
                return false;
            }

            if (symbol is IMethodSymbol { MethodKind: MethodKind.Constructor } constructor)
            {
                foreach (var reference in constructor.ContainingType.DeclaringSyntaxReferences)
                {
                    if (reference.GetSyntax(cancellationToken) is not TypeDeclarationSyntax owner)
                    {
                        continue;
                    }

                    var initializers = owner.Members.OfType<FieldDeclarationSyntax>()
                        .Where(field => !field.Modifiers.Any(SyntaxKind.StaticKeyword))
                        .SelectMany(field => field.Declaration.Variables.Select(variable => variable.Initializer))
                        .Concat(owner.Members.OfType<PropertyDeclarationSyntax>()
                            .Where(property => !property.Modifiers.Any(SyntaxKind.StaticKeyword))
                            .Select(property => property.Initializer));
                    foreach (var initializer in initializers.Where(initializer => initializer is not null))
                    {
                        if (Reads(initializer!).Any(read => ReachesTheme(read.Symbol)))
                        {
                            return true;
                        }
                    }
                }
            }

            foreach (var reference in symbol.DeclaringSyntaxReferences)
            {
                var declaration = reference.GetSyntax(cancellationToken);
                if (declaration is not (BaseMethodDeclarationSyntax or PropertyDeclarationSyntax or VariableDeclaratorSyntax or LocalFunctionStatementSyntax))
                {
                    continue;
                }

                foreach (var (Expression, Symbol) in Reads(declaration))
                {
                    if (ReachesTheme(Symbol))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        internal IEnumerable<(ExpressionSyntax Expression, ISymbol Symbol)> Reads(SyntaxNode declaration)
        {
            // IDE project references can expose another compilation's syntax. This guard only owns local source.
            if (!compilation.ContainsSyntaxTree(declaration.SyntaxTree))
            {
                yield break;
            }

            var model = compilation.GetSemanticModel(declaration.SyntaxTree);
            IEnumerable<SyntaxNode> executable = [declaration];
            if (declaration is PropertyDeclarationSyntax property)
            {
                executable = property.ExpressionBody is { } expressionBody
                    ? [expressionBody]
                    : property.AccessorList?.Accessors.Where(accessor => accessor.IsKind(SyntaxKind.GetAccessorDeclaration)) ?? [];
                if (property.Modifiers.Any(SyntaxKind.StaticKeyword) && property.Initializer is { } initializer)
                {
                    executable = executable.Append(initializer);
                }
            }

            foreach (var expression in executable.SelectMany(node => node.DescendantNodesAndSelf(Descend)).OfType<ExpressionSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (expression is not (MemberAccessExpressionSyntax or IdentifierNameSyntax
                    or InvocationExpressionSyntax or ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax))
                {
                    continue;
                }

                if (expression.Ancestors().TakeWhile(node => node != declaration).Any(node => IsDeferred(node, model)))
                {
                    continue;
                }

                var symbol = model.GetSymbolInfo(expression, cancellationToken).Symbol;
                // A method group supplies a callback. Only an actual invocation executes its body here.
                if (symbol is IMethodSymbol && expression is not (InvocationExpressionSyntax
                    or ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax))
                {
                    continue;
                }

                if (symbol is IPropertySymbol or IMethodSymbol or IFieldSymbol or ILocalSymbol)
                {
                    yield return (expression, symbol);
                }
            }

            bool Descend(SyntaxNode node) => node == declaration || node is not LocalFunctionStatementSyntax;
        }

        private bool IsDeferred(SyntaxNode node, SemanticModel model)
        {
            if (node is InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" } })
            {
                return true;
            }

            if (node is not AnonymousFunctionExpressionSyntax lambda)
            {
                return false;
            }

            if (model.GetTypeInfo(lambda, cancellationToken).ConvertedType is INamedTypeSymbol { DelegateInvokeMethod.ReturnsVoid: true })
            {
                return true;
            }

            return lambda.Parent is ArgumentSyntax
            {
                Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax invocation }
            } && model.GetSymbolInfo(invocation, cancellationToken).Symbol is IMethodSymbol
            {
                Name: "Defer", ContainingType: { Name: "Markup" } containingType
            } && containingType.ToDisplayString() == "Sprout.Markup";
        }
    }
}