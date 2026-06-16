/*
Copyright (c) 2014 - 2020 Darren Horrocks

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DScript.Generators
{
    /// <summary>
    /// Emits a reflection-free <c>GeneratedFunctionRegistrar</c> for every
    /// assembly that declares <c>[ScriptClass]</c> providers. At compile time it
    /// reads the same <c>[ScriptMethod]</c>/<c>[ScriptProperty]</c> metadata the
    /// runtime <c>EngineFunctionLoader</c> used to scan for, and produces direct
    /// <c>engine.AddNative(...)</c> / <c>engine.AddNativeProperty(...)</c> calls
    /// with method-group references — no assembly scanning, no
    /// <c>Delegate.CreateDelegate</c>, so the result is trim- and AOT-safe.
    /// </summary>
    [Generator(LanguageNames.CSharp)]
    public sealed class FunctionRegistrationGenerator : IIncrementalGenerator
    {
        private const string ScriptClassAttribute = "DScript.Extras.ScriptClassAttribute";
        private const string ScriptMethodAttribute = "DScript.Extras.ScriptMethodAttribute";
        private const string ScriptPropertyAttribute = "DScript.Extras.ScriptPropertyAttribute";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var providers = context.SyntaxProvider.ForAttributeWithMetadataName(
                    ScriptClassAttribute,
                    predicate: static (node, _) => node is ClassDeclarationSyntax,
                    transform: static (ctx, _) => Extract(ctx))
                .Where(static p => p is not null);

            context.RegisterSourceOutput(providers.Collect(),
                static (spc, models) => Emit(spc, models));
        }

        // One native binding to emit.
        private sealed class Registration
        {
            public string MethodFullName = "";
            public bool IsProperty;
            public string Definition = ""; // "function Class.Name(a,b)" for methods
            public string PropertyName = ""; // dotted name for properties
        }

        private sealed class ProviderModel
        {
            public List<Registration> Registrations = new();
        }

        private static ProviderModel? Extract(GeneratorAttributeSyntaxContext ctx)
        {
            if (ctx.TargetSymbol is not INamedTypeSymbol type)
            {
                return null;
            }

            // ClassName comes from the [ScriptClass("...")] constructor argument.
            var className = "";
            foreach (var attr in ctx.Attributes)
            {
                if (attr.ConstructorArguments.Length > 0 &&
                    attr.ConstructorArguments[0].Value is string cn)
                {
                    className = cn;
                }
            }

            var model = new ProviderModel();

            foreach (var member in type.GetMembers())
            {
                if (member is not IMethodSymbol method) continue;
                if (method.IsStatic == false) continue;
                if (method.DeclaredAccessibility != Accessibility.Public) continue;

                var methodFullName =
                    type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + method.Name;

                foreach (var a in method.GetAttributes())
                {
                    var attrName = a.AttributeClass?.ToDisplayString();

                    if (attrName == ScriptMethodAttribute)
                    {
                        model.Registrations.Add(BuildMethod(a, className, methodFullName));
                    }
                    else if (attrName == ScriptPropertyAttribute)
                    {
                        model.Registrations.Add(BuildProperty(a, className, methodFullName));
                    }
                }
            }

            return model.Registrations.Count > 0 ? model : null;
        }

        private static Registration BuildMethod(AttributeData a, string className, string methodFullName)
        {
            // ctor: (string methodName) or (string methodName, params string[] parameters)
            var methodName = a.ConstructorArguments.Length > 0
                ? a.ConstructorArguments[0].Value as string ?? ""
                : "";

            string[] parameters = System.Array.Empty<string>();
            if (a.ConstructorArguments.Length > 1 && !a.ConstructorArguments[1].IsNull)
            {
                parameters = a.ConstructorArguments[1].Values
                    .Select(v => v.Value as string ?? "")
                    .ToArray();
            }

            var appearAtRoot = false;
            foreach (var na in a.NamedArguments)
            {
                switch (na.Key)
                {
                    case "AppearAtRoot": appearAtRoot = na.Value.Value is true; break;
                    case "MethodName": methodName = na.Value.Value as string ?? methodName; break;
                    case "MethodParameters":
                        if (!na.Value.IsNull)
                        {
                            parameters = na.Value.Values.Select(v => v.Value as string ?? "").ToArray();
                        }
                        break;
                }
            }

            var name = appearAtRoot ? methodName : className + "." + methodName;
            var definition = "function " + name + "(" + string.Join(",", parameters) + ")";

            return new Registration
            {
                MethodFullName = methodFullName,
                IsProperty = false,
                Definition = definition,
            };
        }

        private static Registration BuildProperty(AttributeData a, string className, string methodFullName)
        {
            var propertyName = a.ConstructorArguments.Length > 0
                ? a.ConstructorArguments[0].Value as string ?? ""
                : "";

            var appearAtRoot = false;
            foreach (var na in a.NamedArguments)
            {
                switch (na.Key)
                {
                    case "AppearAtRoot": appearAtRoot = na.Value.Value is true; break;
                    case "PropertyName": propertyName = na.Value.Value as string ?? propertyName; break;
                }
            }

            var name = appearAtRoot ? propertyName : className + "." + propertyName;

            return new Registration
            {
                MethodFullName = methodFullName,
                IsProperty = true,
                PropertyName = name,
            };
        }

        private static void Emit(SourceProductionContext spc, ImmutableArray<ProviderModel?> models)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#nullable enable");
            sb.AppendLine("namespace DScript.Extras");
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>");
            sb.AppendLine("    /// Compile-time generated, reflection-free registration of every");
            sb.AppendLine("    /// [ScriptClass] provider in this assembly. Trim- and AOT-safe.");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine("    public static class GeneratedFunctionRegistrar");
            sb.AppendLine("    {");
            sb.AppendLine("        public static void RegisterAll(global::DScript.ScriptEngine engine, object? userData)");
            sb.AppendLine("        {");

            foreach (var model in models)
            {
                if (model is null) continue;
                foreach (var r in model.Registrations)
                {
                    if (r.IsProperty)
                    {
                        sb.Append("            engine.AddNativeProperty(\"")
                            .Append(Escape(r.PropertyName))
                            .Append("\", ")
                            .Append(r.MethodFullName)
                            .AppendLine(", userData);");
                    }
                    else
                    {
                        sb.Append("            engine.AddNative(\"")
                            .Append(Escape(r.Definition))
                            .Append("\", ")
                            .Append(r.MethodFullName)
                            .AppendLine(", userData);");
                    }
                }
            }

            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            spc.AddSource("GeneratedFunctionRegistrar.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
        }

        private static string Escape(string s) =>
            s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
