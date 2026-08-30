using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Biome.SourcePatch.FSRAdapter
{
    internal enum BodyOnlyClassification
    {
        Admitted,
        Rejected,
    }

    /// <summary>
    /// Result of one <see cref="BiomeBodyOnlyMethodClassifier.Classify"/>
    /// call. Admitted carries the identity of the one admitted method;
    /// Rejected carries a short machine-stable reason code (never a free
    /// text message) so callers/tests can assert on it.
    /// </summary>
    internal sealed class BodyOnlyClassificationResult
    {
        public BodyOnlyClassification Classification { get; }
        public string DeclaringTypeFullName { get; }
        public string MethodName { get; }
        public string RejectReason { get; }

        private BodyOnlyClassificationResult(
            BodyOnlyClassification classification, string declaringTypeFullName, string methodName, string rejectReason)
        {
            Classification = classification;
            DeclaringTypeFullName = declaringTypeFullName;
            MethodName = methodName;
            RejectReason = rejectReason;
        }

        internal static BodyOnlyClassificationResult AdmittedFor(string declaringTypeFullName, string methodName) =>
            new BodyOnlyClassificationResult(BodyOnlyClassification.Admitted, declaringTypeFullName, methodName, null);

        internal static BodyOnlyClassificationResult RejectedBecause(string reason) =>
            new BodyOnlyClassificationResult(BodyOnlyClassification.Rejected, null, null, reason);
    }

    /// <summary>
    /// Canonical-Roslyn classifier for the hard body-only scope
    /// (Plans/HotReload/V2/FSR-MVP-CLEAN/04-PARETO-COMPLETION-HANDOFF.md
    /// SS1.1/SS1.2): admits only an existing, non-generic, synchronous
    /// instance/static method whose body changed with every other
    /// declaration (type, signature, attributes, layout) byte-for-byte
    /// unchanged. Deliberately conservative -- anything ambiguous is
    /// rejected, never guessed at. Pure: no Unity/FSR types, so this is
    /// fully offline-testable via
    /// qualification/BodyOnlyClassifierHarness.cs.
    /// </summary>
    internal static class BiomeBodyOnlyMethodClassifier
    {
        internal static BodyOnlyClassificationResult Classify(string beforeSource, string afterSource)
        {
            var beforeTree = CSharpSyntaxTree.ParseText(beforeSource);
            var afterTree = CSharpSyntaxTree.ParseText(afterSource);

            if (HasSyntaxError(beforeTree) || HasSyntaxError(afterTree))
            {
                return BodyOnlyClassificationResult.RejectedBecause("syntax-error");
            }

            var beforeRoot = beforeTree.GetCompilationUnitRoot();
            var afterRoot = afterTree.GetCompilationUnitRoot();

            var beforeMethods = beforeRoot.DescendantNodes().OfType<MethodDeclarationSyntax>().ToList();
            var afterMethods = afterRoot.DescendantNodes().OfType<MethodDeclarationSyntax>().ToList();

            if (beforeMethods.Count != afterMethods.Count)
            {
                return BodyOnlyClassificationResult.RejectedBecause("method-count-changed");
            }

            var beforeKeyed = KeyByDeclaration(beforeMethods);
            var afterKeyed = KeyByDeclaration(afterMethods);
            if (beforeKeyed == null || afterKeyed == null)
            {
                return BodyOnlyClassificationResult.RejectedBecause("ambiguous-declaration");
            }

            if (!new HashSet<string>(beforeKeyed.Keys).SetEquals(afterKeyed.Keys))
            {
                return BodyOnlyClassificationResult.RejectedBecause("signature-changed");
            }

            MethodDeclarationSyntax changedBefore = null;
            MethodDeclarationSyntax changedAfter = null;
            var changedCount = 0;
            foreach (var key in beforeKeyed.Keys)
            {
                var before = beforeKeyed[key];
                var after = afterKeyed[key];
                if (!BodyEquivalent(before, after))
                {
                    changedCount++;
                    changedBefore = before;
                    changedAfter = after;
                }
            }

            if (changedCount == 0)
            {
                return BodyOnlyClassificationResult.RejectedBecause("no-body-change");
            }
            if (changedCount > 1)
            {
                return BodyOnlyClassificationResult.RejectedBecause("multiple-methods-changed");
            }

            var rejection = RejectIfOutOfHardScope(changedBefore, changedAfter);
            if (rejection != null)
            {
                return rejection;
            }

            if (changedAfter.Ancestors().OfType<TypeDeclarationSyntax>().Count() > 1)
            {
                return BodyOnlyClassificationResult.RejectedBecause("nested-type");
            }

            return BodyOnlyClassificationResult.AdmittedFor(
                QualifiedTypeName(changedAfter), changedAfter.Identifier.Text);
        }

        private static string QualifiedTypeName(MethodDeclarationSyntax method)
        {
            var declaringType = method.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
            var typeName = declaringType != null ? declaringType.Identifier.Text : string.Empty;
            var namespaces = method.Ancestors().OfType<BaseNamespaceDeclarationSyntax>()
                .Select(n => n.Name.ToString())
                .Reverse()
                .ToList();
            return namespaces.Count == 0 ? typeName : string.Join(".", namespaces) + "." + typeName;
        }

        private static bool HasSyntaxError(SyntaxTree tree) =>
            tree.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error);

        private static Dictionary<string, MethodDeclarationSyntax> KeyByDeclaration(
            List<MethodDeclarationSyntax> methods)
        {
            var keyed = new Dictionary<string, MethodDeclarationSyntax>();
            foreach (var method in methods)
            {
                var key = DeclarationKey(method);
                if (keyed.ContainsKey(key))
                {
                    return null; // ambiguous: two declarations resolve to the same identity
                }
                keyed[key] = method;
            }
            return keyed;
        }

        private static string DeclarationKey(MethodDeclarationSyntax method)
        {
            var declaringType = method.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
            var typeName = declaringType != null ? declaringType.Identifier.Text : string.Empty;
            var attributes = string.Concat(method.AttributeLists.Select(a => a.ToString()));
            return string.Join(
                "|",
                typeName,
                method.Modifiers.ToString(),
                method.ReturnType.ToString(),
                method.Identifier.Text,
                method.TypeParameterList?.ToString() ?? string.Empty,
                method.ParameterList.ToString(),
                attributes);
        }

        private static bool BodyEquivalent(MethodDeclarationSyntax before, MethodDeclarationSyntax after)
        {
            SyntaxNode beforeBody = (SyntaxNode)before.Body ?? before.ExpressionBody;
            SyntaxNode afterBody = (SyntaxNode)after.Body ?? after.ExpressionBody;
            if (beforeBody == null && afterBody == null) return true;
            if (beforeBody == null || afterBody == null) return false;
            return beforeBody.IsEquivalentTo(afterBody);
        }

        private static BodyOnlyClassificationResult RejectIfOutOfHardScope(
            MethodDeclarationSyntax before, MethodDeclarationSyntax after)
        {
            if (before.Modifiers.Any(SyntaxKind.AsyncKeyword) || after.Modifiers.Any(SyntaxKind.AsyncKeyword))
            {
                return BodyOnlyClassificationResult.RejectedBecause("async-method");
            }
            if (before.TypeParameterList != null || after.TypeParameterList != null)
            {
                return BodyOnlyClassificationResult.RejectedBecause("generic-method");
            }

            var declaringTypeBefore = before.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
            var declaringTypeAfter = after.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
            if (declaringTypeBefore?.TypeParameterList != null || declaringTypeAfter?.TypeParameterList != null)
            {
                return BodyOnlyClassificationResult.RejectedBecause("generic-type");
            }

            if (before.Body == null && before.ExpressionBody == null)
            {
                return BodyOnlyClassificationResult.RejectedBecause("no-existing-body");
            }
            if (after.Body == null && after.ExpressionBody == null)
            {
                return BodyOnlyClassificationResult.RejectedBecause("no-new-body");
            }

            if (ContainsIteratorYield(after) || ContainsIteratorYield(before))
            {
                return BodyOnlyClassificationResult.RejectedBecause("iterator-method");
            }
            if (ContainsAwait(after) || ContainsAwait(before))
            {
                return BodyOnlyClassificationResult.RejectedBecause("await-expression");
            }
            if (ContainsClosureShape(after) || ContainsClosureShape(before))
            {
                return BodyOnlyClassificationResult.RejectedBecause("closure-shape");
            }

            return null;
        }

        private static bool ContainsIteratorYield(MethodDeclarationSyntax method) =>
            method.DescendantNodes().OfType<YieldStatementSyntax>().Any();

        private static bool ContainsAwait(MethodDeclarationSyntax method) =>
            method.DescendantNodes().OfType<AwaitExpressionSyntax>().Any();

        private static bool ContainsClosureShape(MethodDeclarationSyntax method) =>
            method.DescendantNodes().Any(n =>
                n is LocalFunctionStatementSyntax
                || n is SimpleLambdaExpressionSyntax
                || n is ParenthesizedLambdaExpressionSyntax
                || n is AnonymousMethodExpressionSyntax);
    }
}
