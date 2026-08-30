using Biome.SourcePatch.FSRAdapter;
using NUnit.Framework;

namespace FastScriptReload.Tests.Editor.Integration.BiomeSourcePatchAdapter
{
    /// <summary>
    /// NUnit mirror of qualification/test_body_only_classifier_harness.py.
    /// </summary>
    public class BiomeBodyOnlyMethodClassifierTests
    {
        [Test]
        public void Classify_BodyOnlyChangeOnInstanceMethod_Admits()
        {
            var result = BiomeBodyOnlyMethodClassifier.Classify(
                "class Foo { int Bar(int x) { return x; } }",
                "class Foo { int Bar(int x) { return x + 1; } }");

            Assert.AreEqual(BodyOnlyClassification.Admitted, result.Classification);
            Assert.AreEqual("Foo", result.DeclaringTypeFullName);
            Assert.AreEqual("Bar", result.MethodName);
        }

        [Test]
        public void Classify_NamespacedType_ReportsFullyQualifiedName()
        {
            var result = BiomeBodyOnlyMethodClassifier.Classify(
                "namespace My.Deep.Namespace { class Foo { int Bar(int x) { return x; } } }",
                "namespace My.Deep.Namespace { class Foo { int Bar(int x) { return x + 1; } } }");

            Assert.AreEqual(BodyOnlyClassification.Admitted, result.Classification);
            Assert.AreEqual("My.Deep.Namespace.Foo", result.DeclaringTypeFullName);
        }

        [Test]
        public void Classify_NoBodyChange_Rejects()
        {
            var result = BiomeBodyOnlyMethodClassifier.Classify(
                "class Foo { int Bar(int x) { return x; } }",
                "class Foo { int Bar(int x) { return x; } }");

            Assert.AreEqual(BodyOnlyClassification.Rejected, result.Classification);
            Assert.AreEqual("no-body-change", result.RejectReason);
        }

        [Test]
        public void Classify_NewMethodAdded_Rejects()
        {
            var result = BiomeBodyOnlyMethodClassifier.Classify(
                "class Foo { int Bar(int x) { return x; } }",
                "class Foo { int Bar(int x) { return x; } int Baz() { return 1; } }");

            Assert.AreEqual("method-count-changed", result.RejectReason);
        }

        [Test]
        public void Classify_SignatureChanged_Rejects()
        {
            var result = BiomeBodyOnlyMethodClassifier.Classify(
                "class Foo { int Bar(int x) { return x; } }",
                "class Foo { int Bar(string x) { return x.Length; } }");

            Assert.AreEqual("signature-changed", result.RejectReason);
        }

        [Test]
        public void Classify_GenericMethod_Rejects()
        {
            var result = BiomeBodyOnlyMethodClassifier.Classify(
                "class Foo { T Bar<T>(T x) { return x; } }",
                "class Foo { T Bar<T>(T x) { return default(T); } }");

            Assert.AreEqual("generic-method", result.RejectReason);
        }

        [Test]
        public void Classify_GenericContainingType_Rejects()
        {
            var result = BiomeBodyOnlyMethodClassifier.Classify(
                "class Foo<T> { int Bar(int x) { return x; } }",
                "class Foo<T> { int Bar(int x) { return x + 1; } }");

            Assert.AreEqual("generic-type", result.RejectReason);
        }

        [Test]
        public void Classify_AsyncMethod_Rejects()
        {
            var result = BiomeBodyOnlyMethodClassifier.Classify(
                "class Foo { async System.Threading.Tasks.Task Bar() { await System.Threading.Tasks.Task.Delay(1); } }",
                "class Foo { async System.Threading.Tasks.Task Bar() { await System.Threading.Tasks.Task.Delay(2); } }");

            Assert.AreEqual("async-method", result.RejectReason);
        }

        [Test]
        public void Classify_IteratorMethod_Rejects()
        {
            var result = BiomeBodyOnlyMethodClassifier.Classify(
                "class Foo { System.Collections.Generic.IEnumerable<int> Bar() { yield return 1; } }",
                "class Foo { System.Collections.Generic.IEnumerable<int> Bar() { yield return 2; } }");

            Assert.AreEqual("iterator-method", result.RejectReason);
        }

        [Test]
        public void Classify_LambdaIntroducedInBody_Rejects()
        {
            var result = BiomeBodyOnlyMethodClassifier.Classify(
                "class Foo { int Bar(int x) { return x; } }",
                "class Foo { int Bar(int x) { System.Func<int,int> f = y => y + 1; return f(x); } }");

            Assert.AreEqual("closure-shape", result.RejectReason);
        }

        [Test]
        public void Classify_LocalFunctionIntroducedInBody_Rejects()
        {
            var result = BiomeBodyOnlyMethodClassifier.Classify(
                "class Foo { int Bar(int x) { return x; } }",
                "class Foo { int Bar(int x) { int Local() { return x; } return Local(); } }");

            Assert.AreEqual("closure-shape", result.RejectReason);
        }

        [Test]
        public void Classify_MoreThanOneMethodBodyChanged_Rejects()
        {
            var result = BiomeBodyOnlyMethodClassifier.Classify(
                "class Foo { int Bar(int x) { return x; } int Baz(int x) { return x; } }",
                "class Foo { int Bar(int x) { return x + 1; } int Baz(int x) { return x + 1; } }");

            Assert.AreEqual("multiple-methods-changed", result.RejectReason);
        }

        [Test]
        public void Classify_SyntaxErrorInNewSource_Rejects()
        {
            var result = BiomeBodyOnlyMethodClassifier.Classify(
                "class Foo { int Bar(int x) { return x; } }",
                "class Foo { int Bar(int x) { return x + ; } }");

            Assert.AreEqual("syntax-error", result.RejectReason);
        }

        [Test]
        public void Classify_NestedTypeMethod_Rejects()
        {
            var result = BiomeBodyOnlyMethodClassifier.Classify(
                "class Outer { class Inner { int Bar(int x) { return x; } } }",
                "class Outer { class Inner { int Bar(int x) { return x + 1; } } }");

            Assert.AreEqual("nested-type", result.RejectReason);
        }
    }
}
