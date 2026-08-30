// The freshly compiled replacement assembly FSR would produce for one
// changed file, compiled here into a genuinely separate DLL (not merged
// into the harness executable) so AssemblyChangesLoader really receives two
// distinct assemblies -- one already loaded in the AppDomain (the harness
// exe, see ExactTargetLoaderHarness.cs) and one freshly "dynamically
// loaded" (this DLL) -- exactly matching production shape.
public class Existing__Patched_
{
    public int Changing(int x) { return x + 100; }
    public int Other(int x) { return x + 999; } // must never be selected
    public static bool StaticNoInstanceCalled;
    public static void OnScriptHotReloadNoInstance() { StaticNoInstanceCalled = true; }
}

public class MissingTarget__Patched_
{
    public int SomeOtherMethod(int x) { return x; } // no "Changing" counterpart
}

public class GenericType__Patched_<T>
{
    public T Changing(T x) { return x; }
}

public class HasGenericMethod__Patched_
{
    public T Changing<T>(T x) { return x; }
}

public class AmbiguousType__Patched_
{
    public int Changing(int x) { return x; }
}

// Postfix-stripping ("AmbiguousType") collides with AmbiguousType__Patched_
// above, simulating a malformed/duplicate dynamically compiled assembly
// where two created types resolve to the same existing-type identity.
public class AmbiguousType
{
    public int Changing(int x) { return x; }
}
