namespace FastScriptReload.Tests.Runtime.Integration.CodePatterns
{
    // Fixture for BiomeSourcePatchAdapter's AssemblyChangesLoaderExactTargetTests
    // (Editor test asmdef). "Changing" is the method the test mutates and
    // patches; "Other" must never be detoured -- it proves the exact-target
    // overload skips every sibling declared method.
    public class ExactTargetPatchFixture
    {
        public int Changing(int x)
        {
            return x;
        }

        public int Other(int x)
        {
            return x;
        }
    }
}
