// A minimal, source-built stand-in for UnityEngine.CoreModule.dll (assembly
// name matters here, not just namespace/type names): Assets/Plugins/
// ImmersiveVrToolsCommon/ImmersiveVRTools.Common.Runtime.dll's own compiled
// metadata references UnityEngine types by that exact assembly identity
// (UnityMainThreadDispatcher extends MonoBehaviour), so a source-level
// stub type of the same name in a different assembly cannot satisfy it
// (CS0012) -- only a real, separately compiled assembly with this exact
// name can. Used by qualification/test_exact_target_loader_harness.py.
namespace UnityEngine
{
    public class Object { }
    public class Component : Object { }
    public class Behaviour : Component { }
    public class MonoBehaviour : Behaviour { }

    public static class Debug
    {
        public static void Log(object message) { }
        public static void LogWarning(object message) { }
        public static void LogError(object message) { }
    }

    public static class Application
    {
        public static string productName = "BiomeExactTargetLoaderHarness";
    }

    public enum RuntimeInitializeLoadType
    {
        AfterAssembliesLoaded,
    }

    [System.AttributeUsage(System.AttributeTargets.Method)]
    public class RuntimeInitializeOnLoadMethodAttribute : System.Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType loadType) { }
    }
}
