// Uses a real compiled UnityEngine.CoreModule.dll stub (see
// qualification/test_exact_target_loader_harness.py) so binary type
// identity matches what ImmersiveVRTools.Common.Runtime.dll expects,
// plus qualification/LoaderHarness.cs's UnityEditor stubs for Polyfills.cs.
//
// This harness deliberately never triggers a real Memory.DetourMethod call:
// a bare `mono` CLI process is not Unity's own Mono runtime, and attempting
// a live Harmony detour outside it crashed natively during development.
// qualification/LoaderHarnessTests already follows the same discipline
// (it proves the Harmony blob loads and exposes the right identity, but
// never performs a live detour offline). So this harness exercises
// AssemblyChangesLoader.ResolveExactTarget -- the pure selection/rejection
// step -- plus the public overload's failure-delegation path, which never
// reaches Memory.DetourMethod. The actual detour is proven only in Unity
// (P0-80 and Assets/Tests/Editor/Integration/BiomeSourcePatchAdapter/
// AssemblyChangesLoaderExactTargetTests.cs).

namespace UnityEditor
{
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public class InitializeOnLoadAttribute : System.Attribute { }
}

// ---- "Already loaded in the AppDomain" fixtures --------------------------
public class Existing
{
    public int Changing(int x) { return x; }
    public int Other(int x) { return x; }
}

public class MissingTarget
{
    public int Changing(int x) { return x; }
}

public class NoSuchType
{
    public int Changing(int x) { return x; }
}

public class GenericType<T>
{
    public T Changing(T x) { return x; }
}

public class HasGenericMethod
{
    public T Changing<T>(T x) { return x; }
}

public class AmbiguousType
{
    public int Changing(int x) { return x; }
}

internal static class ExactTargetLoaderHarness
{
    private static int Main(string[] args)
    {
        var mode = args.Length == 0 ? "resolves-exactly-one" : args[0];

        switch (mode)
        {
            case "resolves-exactly-one":
            {
                var exactTarget = typeof(Existing).GetMethod("Changing");
                var resolution = FastScriptReload.Runtime.AssemblyChangesLoader.ResolveExactTarget(
                    typeof(Existing__Patched_).Assembly, exactTarget);

                if (resolution.FailureReason != null) return 1;
                if (resolution.CreatedMethod == null) return 2;
                if (resolution.CreatedMethod.Name != "Changing") return 3;
                // Existing__Patched_ declares three methods (Changing, Other,
                // OnScriptHotReloadNoInstance); exactly one is the target, so
                // exactly two must be recorded as skipped -- never detoured.
                if (resolution.SkippedIdentities.Count != 2) return 4;
                if (System.Linq.Enumerable.Any(resolution.SkippedIdentities, id => id.Contains("Changing"))) return 5;
                System.Console.WriteLine("RESOLVED:" + resolution.CreatedMethod.Name);
                return 0;
            }

            case "missing-created-method":
            {
                var exactTarget = typeof(MissingTarget).GetMethod("Changing");
                var resolution = FastScriptReload.Runtime.AssemblyChangesLoader.ResolveExactTarget(
                    typeof(MissingTarget__Patched_).Assembly, exactTarget);
                if (resolution.FailureReason == null) return 6;
                System.Console.WriteLine("REJECTED:" + resolution.FailureReason);
                return 0;
            }

            case "type-not-found":
            {
                var exactTarget = typeof(NoSuchType).GetMethod("Changing");
                var resolution = FastScriptReload.Runtime.AssemblyChangesLoader.ResolveExactTarget(
                    typeof(Existing__Patched_).Assembly, exactTarget);
                if (resolution.FailureReason == null) return 7;
                System.Console.WriteLine("REJECTED:" + resolution.FailureReason);
                return 0;
            }

            case "generic-type-rejected":
            {
                var exactTarget = typeof(GenericType<int>).GetMethod("Changing");
                var resolution = FastScriptReload.Runtime.AssemblyChangesLoader.ResolveExactTarget(
                    typeof(GenericType__Patched_<int>).Assembly, exactTarget);
                if (resolution.FailureReason == null) return 8;
                System.Console.WriteLine("REJECTED:" + resolution.FailureReason);
                return 0;
            }

            case "generic-method-rejected":
            {
                var exactTarget = typeof(HasGenericMethod).GetMethod("Changing");
                var resolution = FastScriptReload.Runtime.AssemblyChangesLoader.ResolveExactTarget(
                    typeof(HasGenericMethod__Patched_).Assembly, exactTarget);
                if (resolution.FailureReason == null) return 9;
                System.Console.WriteLine("REJECTED:" + resolution.FailureReason);
                return 0;
            }

            case "ambiguous-created-type":
            {
                var exactTarget = typeof(AmbiguousType).GetMethod("Changing");
                // The fixtures dll contains both AmbiguousType (unstripped)
                // and AmbiguousType__Patched_ -- both strip to "AmbiguousType",
                // so two created types match one existing-type identity.
                var resolution = FastScriptReload.Runtime.AssemblyChangesLoader.ResolveExactTarget(
                    typeof(AmbiguousType__Patched_).Assembly, exactTarget);
                if (resolution.FailureReason == null) return 10;
                System.Console.WriteLine("REJECTED:" + resolution.FailureReason);
                return 0;
            }

            case "public-overload-delegates-failure-without-detour":
            {
                // Exercises the public DynamicallyUpdateSingleMethodForCreatedAssembly
                // wrapper on a failure path: it must return Failed and must
                // never reach Memory.DetourMethod (no Harmony blob wiring is
                // configured in this mode -- a real detour attempt would
                // throw/crash, proving this path truly never gets there).
                var exactTarget = typeof(NoSuchType).GetMethod("Changing");
                var loader = FastScriptReload.Runtime.AssemblyChangesLoader.Instance;
                var result = loader.DynamicallyUpdateSingleMethodForCreatedAssembly(
                    typeof(Existing__Patched_).Assembly, exactTarget);
                if (result.Applied) return 11;
                if (string.IsNullOrEmpty(result.FailureReason)) return 12;
                System.Console.WriteLine("REJECTED:" + result.FailureReason);
                return 0;
            }

            default:
                return 64;
        }
    }
}
