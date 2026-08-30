using System;
using System.Linq;
using System.Reflection;
using ImmersiveVRTools.Runtime.Common.Extensions;

internal sealed class ExistingMethodTarget
{
    private int Change(int value) { return value; }
    private int Change(string value) { return value.Length; }
    private void Change(ref int value) { value++; }
    private T Generic<T>(T value) { return value; }
    private static int StaticChange(int value) { return value; }
}

internal sealed class ExistingMethodTarget__Patched_
{
    private int Change(int value) { return value + 1; }
    private int Change(string value) { return value.Length + 1; }
    private void Change(ref int value) { value += 2; }
    private T Generic<T>(T value) { return value; }
    private static int StaticChange(int value) { return value + 1; }
}

internal static class MethodIdentityHarness
{
    private const string PatchedPostfix = "__Patched_";

    private static string Identity(MethodBase method, string mode)
    {
        if (mode == "name-only") return method.Name;
        if (mode == "zero") return string.Empty;
        return method.ResolveFullName().Replace(PatchedPostfix, string.Empty);
    }

    private static int Main(string[] args)
    {
        var mode = args.Length == 0 ? "exact" : args[0];
        var existing = typeof(ExistingMethodTarget)
            .GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic |
                        BindingFlags.DeclaredOnly)
            .Select(method => Identity(method, mode)).OrderBy(value => value).ToArray();
        var created = typeof(ExistingMethodTarget__Patched_)
            .GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic |
                        BindingFlags.DeclaredOnly)
            .Select(method => Identity(method, mode)).OrderBy(value => value).ToArray();
        var valid = existing.SequenceEqual(created) &&
                    existing.Distinct().Count() == 5;
        if (mode != "exact")
        {
            if (valid) return 3;
            Console.WriteLine("REJECTED:" + mode);
            return 0;
        }
        if (!valid)
            return 1;
        if (!existing.Any(value => value.EndsWith("Change(System.Int32)")) ||
            !existing.Any(value => value.EndsWith("Change(System.String)")) ||
            !existing.Any(value => value.EndsWith("Change(System.Int32&)")) ||
            !existing.Any(value => value.EndsWith("Generic(T)")) ||
            !existing.Any(value => value.EndsWith("StaticChange(System.Int32)")))
            return 2;
        Console.WriteLine("METHOD-IDENTITIES-VALID");
        return 0;
    }
}
