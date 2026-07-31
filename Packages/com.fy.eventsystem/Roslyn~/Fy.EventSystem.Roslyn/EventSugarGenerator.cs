using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Fy.EventSystem.Roslyn
{
    /// <summary>
    /// Generates the per-event call-site API: a static <c>AddListener</c> on the event type itself and an
    /// <c>Invoke</c> extension method, both forwarding to the <c>IEventService</c> resolved from the service locator.
    /// </summary>
    /// <remarks>
    /// Turns <c>ServiceLocator.GetChecked&lt;IEventService&gt;().Invoke(this, new FooEvent())</c> into
    /// <c>new FooEvent().Invoke(this)</c>. Only the call site changes; the service API it forwards to is untouched.
    /// </remarks>
    [Generator]
    public sealed class EventSugarGenerator : IIncrementalGenerator
    {
        private const string EventInterfaceFullName = "Fy.EventSystem.IEvent";
        private const string ServiceExpression =
            "global::Fy.Services.ServiceLocator.GetChecked<global::Fy.EventSystem.IEventService>()";

        private const string GeneratorName = "Fy.EventSystem.Roslyn.EventSugarGenerator";
        private const string GeneratorVersion = "1.0.0.0";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            IncrementalValuesProvider<EventTypeInfo> eventTypes = context.SyntaxProvider
                .CreateSyntaxProvider(
                    static (node, _) => node is StructDeclarationSyntax,
                    static (syntaxContext, _) => TryCreateEventTypeInfo(syntaxContext))
                .Where(static info => info != null);

            context.RegisterSourceOutput(eventTypes, static (sourceContext, info) => Execute(sourceContext, info));
        }

        private static EventTypeInfo TryCreateEventTypeInfo(GeneratorSyntaxContext context)
        {
            var declaration = (StructDeclarationSyntax)context.Node;

            if (!(context.SemanticModel.GetDeclaredSymbol(declaration) is INamedTypeSymbol symbol)
             || !ImplementsEventInterface(symbol))
            {
                return null;
            }

            string unsupportedReason = null;

            if (symbol.ContainingType != null)
            {
                unsupportedReason = "a nested type";
            }
            else if (symbol.IsGenericType)
            {
                unsupportedReason = "a generic type";
            }

            string containingNamespace = symbol.ContainingNamespace.IsGlobalNamespace
                ? null
                : symbol.ContainingNamespace.ToDisplayString();

            return new EventTypeInfo(
                containingNamespace,
                symbol.Name,
                symbol.DeclaredAccessibility == Accessibility.Public ? "public" : "internal",
                declaration.Modifiers.Any(SyntaxKind.ReadOnlyKeyword),
                declaration.Modifiers.Any(SyntaxKind.PartialKeyword),
                unsupportedReason,
                declaration.Identifier.GetLocation());
        }

        private static bool ImplementsEventInterface(INamedTypeSymbol symbol)
        {
            foreach (INamedTypeSymbol interfaceSymbol in symbol.AllInterfaces)
            {
                if (interfaceSymbol.ToDisplayString() == EventInterfaceFullName)
                {
                    return true;
                }
            }

            return false;
        }

        private static void Execute(SourceProductionContext context, EventTypeInfo info)
        {
            if (info.UnsupportedReason != null)
            {
                if (info.IsPartial)
                {
                    context.ReportDiagnostic(Diagnostic.Create(EventSugarDiagnostics.UnsupportedEventShape,
                        info.Location, info.Name, info.UnsupportedReason));
                }

                return;
            }

            if (!info.IsPartial)
            {
                context.ReportDiagnostic(Diagnostic.Create(EventSugarDiagnostics.MissingPartialKeyword,
                    info.Location, info.Name));

                return;
            }

            string hintName = string.IsNullOrEmpty(info.Namespace)
                ? $"{info.Name}.EventSugar.g.cs"
                : $"{info.Namespace}.{info.Name}.EventSugar.g.cs";

            context.AddSource(hintName, SourceText.From(BuildSource(info), Encoding.UTF8));
        }

        private static string BuildSource(EventTypeInfo info)
        {
            var builder = new StringBuilder();
            bool hasNamespace = !string.IsNullOrEmpty(info.Namespace);
            string indent = hasNamespace ? "    " : string.Empty;
            string memberIndent = indent + "    ";
            string bodyIndent = memberIndent + "    ";
            string qualifiedName = hasNamespace ? $"global::{info.Namespace}.{info.Name}" : $"global::{info.Name}";
            string readOnlyModifier = info.IsReadOnly ? "readonly " : string.Empty;

            builder.AppendLine("// <auto-generated/>");
            builder.AppendLine();

            if (hasNamespace)
            {
                builder.AppendLine($"namespace {info.Namespace}");
                builder.AppendLine("{");
            }

            builder.AppendLine($"{indent}{info.Accessibility} {readOnlyModifier}partial struct {info.Name}");
            builder.AppendLine($"{indent}{{");
            AppendMethodAttributes(builder, memberIndent, "AddListener");
            builder.AppendLine($"{memberIndent}{info.Accessibility} static " +
                               "global::Fy.EventSystem.EventHandle AddListener(" +
                               $"global::Fy.EventSystem.EventContextHandler<{qualifiedName}> eventHandler)");
            builder.AppendLine($"{memberIndent}{{");
            builder.AppendLine($"{bodyIndent}return {ServiceExpression}.AddListener<{qualifiedName}>(eventHandler);");
            builder.AppendLine($"{memberIndent}}}");
            builder.AppendLine($"{indent}}}");
            builder.AppendLine();

            builder.AppendLine($"{indent}/// <summary>");
            builder.AppendLine($"{indent}/// Generated invocation API for <see cref=\"{info.Name}\"/>.");
            builder.AppendLine($"{indent}/// </summary>");
            builder.AppendLine($"{indent}[global::System.CodeDom.Compiler.GeneratedCode(" +
                               $"\"{GeneratorName}\", \"{GeneratorVersion}\")]");
            builder.AppendLine($"{indent}{info.Accessibility} static class Generated{info.Name}Utility");
            builder.AppendLine($"{indent}{{");
            AppendMethodAttributes(builder, memberIndent, "Invoke");
            builder.AppendLine($"{memberIndent}{info.Accessibility} static bool Invoke(" +
                               $"in this {qualifiedName} e, object sender)");
            builder.AppendLine($"{memberIndent}{{");
            builder.AppendLine($"{bodyIndent}return {ServiceExpression}.Invoke<{qualifiedName}>(sender, in e);");
            builder.AppendLine($"{memberIndent}}}");
            builder.AppendLine($"{indent}}}");

            if (hasNamespace)
            {
                builder.AppendLine("}");
            }

            return builder.ToString();
        }

        private static void AppendMethodAttributes(StringBuilder builder, string indent, string forwardedMethod)
        {
            builder.AppendLine($"{indent}/// <inheritdoc cref=\"global::Fy.EventSystem.IEventService." +
                               $"{forwardedMethod}{{TEvent}}\"/>");
            builder.AppendLine($"{indent}[global::System.CodeDom.Compiler.GeneratedCode(" +
                               $"\"{GeneratorName}\", \"{GeneratorVersion}\")]");
            builder.AppendLine($"{indent}[global::System.Runtime.CompilerServices.MethodImpl(" +
                               "global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");
        }
    }
}
