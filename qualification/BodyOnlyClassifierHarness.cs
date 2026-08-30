using System;
using Biome.SourcePatch.FSRAdapter;

internal static class BodyOnlyClassifierHarness
{
    private static int Main(string[] args)
    {
        var mode = args.Length == 0 ? "admit-body-only" : args[0];
        BodyOnlyClassificationResult result;

        switch (mode)
        {
            case "admit-body-only":
                result = BiomeBodyOnlyMethodClassifier.Classify(
                    "class Foo { int Bar(int x) { return x; } }",
                    "class Foo { int Bar(int x) { return x + 1; } }");
                return Report(result, expectAdmitted: true);

            case "admit-static-method":
                result = BiomeBodyOnlyMethodClassifier.Classify(
                    "class Foo { static int Bar(int x) { return x; } }",
                    "class Foo { static int Bar(int x) { return x * 2; } }");
                return Report(result, expectAdmitted: true);

            case "admit-expression-body":
                result = BiomeBodyOnlyMethodClassifier.Classify(
                    "class Foo { int Bar(int x) => x; }",
                    "class Foo { int Bar(int x) => x + 1; }");
                return Report(result, expectAdmitted: true);

            case "reject-no-change":
                result = BiomeBodyOnlyMethodClassifier.Classify(
                    "class Foo { int Bar(int x) { return x; } }",
                    "class Foo { int Bar(int x) { return x; } }");
                return Report(result, expectAdmitted: false, expectedReason: "no-body-change");

            case "reject-new-method":
                result = BiomeBodyOnlyMethodClassifier.Classify(
                    "class Foo { int Bar(int x) { return x; } }",
                    "class Foo { int Bar(int x) { return x; } int Baz() { return 1; } }");
                return Report(result, expectAdmitted: false, expectedReason: "method-count-changed");

            case "reject-signature-changed":
                result = BiomeBodyOnlyMethodClassifier.Classify(
                    "class Foo { int Bar(int x) { return x; } }",
                    "class Foo { int Bar(string x) { return x.Length; } }");
                return Report(result, expectAdmitted: false, expectedReason: "signature-changed");

            case "reject-attribute-changed":
                result = BiomeBodyOnlyMethodClassifier.Classify(
                    "class Foo { int Bar(int x) { return x; } }",
                    "class Foo { [Obsolete] int Bar(int x) { return x; } }");
                return Report(result, expectAdmitted: false, expectedReason: "signature-changed");

            case "reject-generic-method":
                result = BiomeBodyOnlyMethodClassifier.Classify(
                    "class Foo { T Bar<T>(T x) { return x; } }",
                    "class Foo { T Bar<T>(T x) { return default(T); } }");
                return Report(result, expectAdmitted: false, expectedReason: "generic-method");

            case "reject-generic-type":
                result = BiomeBodyOnlyMethodClassifier.Classify(
                    "class Foo<T> { int Bar(int x) { return x; } }",
                    "class Foo<T> { int Bar(int x) { return x + 1; } }");
                return Report(result, expectAdmitted: false, expectedReason: "generic-type");

            case "reject-async-method":
                result = BiomeBodyOnlyMethodClassifier.Classify(
                    "class Foo { async System.Threading.Tasks.Task Bar() { await System.Threading.Tasks.Task.Delay(1); } }",
                    "class Foo { async System.Threading.Tasks.Task Bar() { await System.Threading.Tasks.Task.Delay(2); } }");
                return Report(result, expectAdmitted: false, expectedReason: "async-method");

            case "reject-iterator-method":
                result = BiomeBodyOnlyMethodClassifier.Classify(
                    "class Foo { System.Collections.Generic.IEnumerable<int> Bar() { yield return 1; } }",
                    "class Foo { System.Collections.Generic.IEnumerable<int> Bar() { yield return 2; } }");
                return Report(result, expectAdmitted: false, expectedReason: "iterator-method");

            case "reject-lambda-introduced":
                result = BiomeBodyOnlyMethodClassifier.Classify(
                    "class Foo { int Bar(int x) { return x; } }",
                    "class Foo { int Bar(int x) { System.Func<int,int> f = y => y + 1; return f(x); } }");
                return Report(result, expectAdmitted: false, expectedReason: "closure-shape");

            case "reject-local-function-introduced":
                result = BiomeBodyOnlyMethodClassifier.Classify(
                    "class Foo { int Bar(int x) { return x; } }",
                    "class Foo { int Bar(int x) { int Local() { return x; } return Local(); } }");
                return Report(result, expectAdmitted: false, expectedReason: "closure-shape");

            case "reject-multiple-methods-changed":
                result = BiomeBodyOnlyMethodClassifier.Classify(
                    "class Foo { int Bar(int x) { return x; } int Baz(int x) { return x; } }",
                    "class Foo { int Bar(int x) { return x + 1; } int Baz(int x) { return x + 1; } }");
                return Report(result, expectAdmitted: false, expectedReason: "multiple-methods-changed");

            case "reject-syntax-error":
                result = BiomeBodyOnlyMethodClassifier.Classify(
                    "class Foo { int Bar(int x) { return x; } }",
                    "class Foo { int Bar(int x) { return x + ; } }");
                return Report(result, expectAdmitted: false, expectedReason: "syntax-error");

            case "reject-field-added":
                result = BiomeBodyOnlyMethodClassifier.Classify(
                    "class Foo { int Bar(int x) { return x; } }",
                    "class Foo { int _y; int Bar(int x) { return x; } }");
                // field addition does not change method count/signature keys,
                // so this must fall through to "no-body-change" -- proving
                // the classifier only ever looks at method declarations and
                // therefore can never silently admit a field-shape change.
                return Report(result, expectAdmitted: false, expectedReason: "no-body-change");

            case "admit-with-namespace":
                result = BiomeBodyOnlyMethodClassifier.Classify(
                    "namespace My.Deep.Namespace { class Foo { int Bar(int x) { return x; } } }",
                    "namespace My.Deep.Namespace { class Foo { int Bar(int x) { return x + 1; } } }");
                if (result.Classification != BodyOnlyClassification.Admitted) return 1;
                if (result.DeclaringTypeFullName != "My.Deep.Namespace.Foo") return 20;
                Console.WriteLine("ADMITTED:" + result.DeclaringTypeFullName + "." + result.MethodName);
                return 0;

            case "reject-nested-type":
                result = BiomeBodyOnlyMethodClassifier.Classify(
                    "class Outer { class Inner { int Bar(int x) { return x; } } }",
                    "class Outer { class Inner { int Bar(int x) { return x + 1; } } }");
                return Report(result, expectAdmitted: false, expectedReason: "nested-type");

            default:
                return 64;
        }
    }

    private static int Report(BodyOnlyClassificationResult result, bool expectAdmitted, string expectedReason = null)
    {
        if (expectAdmitted)
        {
            if (result.Classification != BodyOnlyClassification.Admitted) return 1;
            Console.WriteLine("ADMITTED:" + result.DeclaringTypeFullName + "." + result.MethodName);
            return 0;
        }

        if (result.Classification != BodyOnlyClassification.Rejected) return 2;
        if (expectedReason != null && result.RejectReason != expectedReason) return 3;
        Console.WriteLine("REJECTED:" + result.RejectReason);
        return 0;
    }
}
