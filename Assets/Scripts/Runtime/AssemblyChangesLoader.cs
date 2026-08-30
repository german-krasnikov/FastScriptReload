#if UNITY_EDITOR || LiveScriptReload_Enabled

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using ImmersiveVRTools.Runtime.Common;
using ImmersiveVRTools.Runtime.Common.Extensions;
using ImmersiveVrToolsCommon.Runtime.Logging;
using UnityEngine;
using Debug = UnityEngine.Debug;

using Memory = FastScriptReload.Runtime.Polyfills.Memory;

namespace FastScriptReload.Runtime
{
    [PreventHotReload]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoad]
#endif
    public class AssemblyChangesLoader: IAssemblyChangesLoader
    {
        const BindingFlags ALL_BINDING_FLAGS = BindingFlags.Public | BindingFlags.NonPublic |
                                               BindingFlags.Static | BindingFlags.Instance |
                                               BindingFlags.FlattenHierarchy;
            
        const BindingFlags ALL_DECLARED_METHODS_BINDING_FLAGS = BindingFlags.Public | BindingFlags.NonPublic |
                                                                BindingFlags.Static | BindingFlags.Instance |
                                                                BindingFlags.DeclaredOnly; //only declared methods can be redirected, otherwise it'll result in hang
        
        public const string ClassnamePatchedPostfix = "__Patched_";
        public const string ON_HOT_RELOAD_METHOD_NAME = "OnScriptHotReload";
        public const string ON_HOT_RELOAD_NO_INSTANCE_STATIC_METHOD_NAME = "OnScriptHotReloadNoInstance";

        private static readonly List<Type> ExcludeMethodsDefinedOnTypes = new List<Type>
        {
            typeof(MonoBehaviour),
            typeof(Behaviour),
            typeof(UnityEngine.Object),
            typeof(Component),
            typeof(System.Object)
        }; //TODO: move out and possibly define a way to exclude all non-client created code? as this will crash editor
        
        private static AssemblyChangesLoader _instance;
        public static AssemblyChangesLoader Instance => _instance ?? (_instance = new AssemblyChangesLoader());

        private Dictionary<Type, Type> _existingTypeToRedirectedType = new Dictionary<Type, Type>();

        public void DynamicallyUpdateMethodsForCreatedAssembly(Assembly dynamicallyLoadedAssemblyWithUpdates, AssemblyChangesLoaderEditorOptionsNeededInBuild editorOptions)
        {
            try
            {
                var sw = new Stopwatch();
                sw.Start();

                foreach (var createdType in dynamicallyLoadedAssemblyWithUpdates.GetTypes()
                             .Where(t => (t.IsClass
                                         && !typeof(Delegate).IsAssignableFrom(t)) //don't redirect delegates
                                         // || (t.IsValueType && !t.IsPrimitive) //struct check, ensure works
                             )
                        )
                {
                    if (createdType.GetCustomAttribute<PreventHotReload>() != null)
                    {
                        //TODO: ideally type would be excluded from compilation not just from detour
                        LoggerScoped.Log($"Type: {createdType.Name} marked as {nameof(PreventHotReload)} - ignoring change.");
                        continue;
                    }
                    
                    var createdTypeNameWithoutPatchedPostfix = RemoveClassPostfix(createdType.FullName);
                    if (ProjectTypeCache.AllTypesInNonDynamicGeneratedAssemblies.TryGetValue(createdTypeNameWithoutPatchedPostfix, out var matchingTypeInExistingAssemblies))
                    {
                        _existingTypeToRedirectedType[matchingTypeInExistingAssemblies] = createdType;
                        
                        if (!editorOptions.IsDidFieldsOrPropertyCountChangedCheckDisabled 
                            && !editorOptions.EnableExperimentalAddedFieldsSupport
                            && DidFieldsOrPropertyCountChanged(createdType,  matchingTypeInExistingAssemblies))
                        {
                            continue;
                        }

                        var allDeclaredMethodsInExistingType = matchingTypeInExistingAssemblies.GetMethods(ALL_DECLARED_METHODS_BINDING_FLAGS)
                            .Where(m => !ExcludeMethodsDefinedOnTypes.Contains(m.DeclaringType))
                            .ToList();
                        foreach (var createdTypeMethodToUpdate in createdType.GetMethods(ALL_DECLARED_METHODS_BINDING_FLAGS)
                                     .Where(m => !ExcludeMethodsDefinedOnTypes.Contains(m.DeclaringType)))
                        {
                            var createdTypeMethodToUpdateFullDescriptionWithoutPatchedClassPostfix = ResolveMethodIdentity(createdTypeMethodToUpdate);
                            var matchingMethodInExistingType = allDeclaredMethodsInExistingType
                                .SingleOrDefault(m => string.Equals(ResolveMethodIdentity(m), createdTypeMethodToUpdateFullDescriptionWithoutPatchedClassPostfix, StringComparison.Ordinal));
                            if (matchingMethodInExistingType != null)
                            {
                                if (matchingMethodInExistingType.IsGenericMethod)
                                {
                                    LoggerScoped.LogWarning($"Method: '{matchingMethodInExistingType.ResolveFullName()}' is generic. Hot-Reload for generic methods is not supported yet, you won't see changes for that method.");
                                    continue;
                                }

                                if (matchingMethodInExistingType.DeclaringType != null && matchingMethodInExistingType.DeclaringType.IsGenericType)
                                {
                                    LoggerScoped.LogWarning($"Type for method: '{matchingMethodInExistingType.ResolveFullName()}' is generic. Hot-Reload for generic types is not supported yet, you won't see changes for that type.");
                                    continue;
                                }

                                LoggerScoped.LogDebug($"Trying to detour method, from: '{matchingMethodInExistingType.ResolveFullName()}' to: '{createdTypeMethodToUpdate.ResolveFullName()}'");
                                DetourCrashHandler.LogDetour(matchingMethodInExistingType.ResolveFullName());
                                Memory.DetourMethod(matchingMethodInExistingType, createdTypeMethodToUpdate);
                            }
                            else 
                            {
                                LoggerScoped.LogWarning($"Method: {createdTypeMethodToUpdate.ResolveFullName()} does not exist in initially compiled type: {matchingTypeInExistingAssemblies.FullName}. " +
                                                 $"Adding new methods at runtime is not fully supported. \r\n" +
                                                 $"It'll only work new method is only used by declaring class (eg private method)\r\n" +
                                                 $"Make sure to add method before initial compilation.");
                            }
                        }
                        
                        FindAndExecuteStaticOnScriptHotReloadNoInstance(createdType);
                        FindAndExecuteOnScriptHotReload(matchingTypeInExistingAssemblies, createdType);
                    }
                    else
                    {
                        LoggerScoped.LogWarning($"FSR: Unable to find existing type for: '{createdType.FullName}', this is not an issue if you added new type. <color=orange>If it's an existing type please do a full domain-reload - one of optimisations is to cache existing types for later lookup on first call.</color>");
                        FindAndExecuteStaticOnScriptHotReloadNoInstance(createdType);
                        FindAndExecuteOnScriptHotReload(createdType, createdType);
                    }
                }
                
                LoggerScoped.Log($"Hot-reload completed (took {sw.ElapsedMilliseconds}ms)");
            }
            finally
            {
                DetourCrashHandler.ClearDetourLog();
            }
        }
        
        /// <summary>
        /// P0-60 exact-target overload: applies exactly one already-admitted
        /// method from <paramref name="dynamicallyLoadedAssemblyWithUpdates"/>
        /// onto <paramref name="exactExistingMethodToUpdate"/>, records every
        /// other declared method on the matching created type as skipped
        /// (never detoured), and deliberately bypasses both
        /// OnScriptHotReload/OnScriptHotReloadNoInstance lifecycle callback
        /// paths. The existing <see cref="DynamicallyUpdateMethodsForCreatedAssembly"/>
        /// preserve-all overload above is completely unchanged by this
        /// addition. See Plans/HotReload/V2/FSR-MVP-CLEAN/
        /// 04-PARETO-COMPLETION-HANDOFF.md SS6 P0-60.
        /// </summary>
        public ExactTargetPatchResult DynamicallyUpdateSingleMethodForCreatedAssembly(
            Assembly dynamicallyLoadedAssemblyWithUpdates, MethodBase exactExistingMethodToUpdate)
        {
            var resolution = ResolveExactTarget(dynamicallyLoadedAssemblyWithUpdates, exactExistingMethodToUpdate);
            if (resolution.FailureReason != null)
            {
                return ExactTargetPatchResult.Failed(resolution.FailureReason);
            }

            try
            {
                LoggerScoped.LogDebug($"Trying to detour method (exact-target), from: '{exactExistingMethodToUpdate.ResolveFullName()}' to: '{resolution.CreatedMethod.ResolveFullName()}'");
                DetourCrashHandler.LogDetour(exactExistingMethodToUpdate.ResolveFullName());
                Memory.DetourMethod(exactExistingMethodToUpdate, resolution.CreatedMethod);
            }
            catch (Exception)
            {
                return ExactTargetPatchResult.Failed("detour-exception");
            }
            finally
            {
                DetourCrashHandler.ClearDetourLog();
            }

            // Deliberately does not call FindAndExecuteStaticOnScriptHotReloadNoInstance
            // or FindAndExecuteOnScriptHotReload: the exact-target path bypasses
            // both lifecycle callback paths (P0-60 RED).
            return ExactTargetPatchResult.Succeeded(resolution.TargetIdentity, resolution.SkippedIdentities);
        }

        /// <summary>
        /// Pure selection step for the exact-target overload above: finds
        /// the one created method matching <paramref name="exactExistingMethodToUpdate"/>'s
        /// identity and records every sibling declared method as skipped,
        /// without ever calling Memory.DetourMethod. Kept separate so this
        /// selection/rejection logic is exercisable offline (see
        /// qualification/ExactTargetLoaderHarness.cs) without invoking a
        /// real Harmony detour outside Unity's own runtime.
        /// </summary>
        internal static ExactTargetResolution ResolveExactTarget(
            Assembly dynamicallyLoadedAssemblyWithUpdates, MethodBase exactExistingMethodToUpdate)
        {
            if (dynamicallyLoadedAssemblyWithUpdates == null || exactExistingMethodToUpdate == null)
            {
                return ExactTargetResolution.Failed("null-argument");
            }

            var existingDeclaringType = exactExistingMethodToUpdate.DeclaringType;
            if (existingDeclaringType == null)
            {
                return ExactTargetResolution.Failed("no-declaring-type");
            }

            // Checked before any type-name search: generic type FullName
            // matching across two independently loaded assemblies is not
            // reliable (assembly-qualified generic-argument text can differ
            // even for the "same" closed type), and generic types/methods
            // are out of the hard body-only scope anyway (SS1.2) -- reject
            // up front rather than attempt a fragile match.
            if (existingDeclaringType.IsGenericType)
            {
                return ExactTargetResolution.Failed("generic-type");
            }

            var existingTypeFullName = existingDeclaringType.FullName;
            var matchingCreatedTypes = dynamicallyLoadedAssemblyWithUpdates.GetTypes()
                .Where(t => !t.IsGenericType && RemoveClassPostfix(t.FullName) == existingTypeFullName)
                .ToList();

            if (matchingCreatedTypes.Count == 0)
            {
                return ExactTargetResolution.Failed("created-type-not-found");
            }
            if (matchingCreatedTypes.Count > 1)
            {
                return ExactTargetResolution.Failed("created-type-ambiguous");
            }

            var createdType = matchingCreatedTypes[0];

            var targetIdentity = ResolveMethodIdentity(exactExistingMethodToUpdate);
            var declaredCreatedMethods = createdType.GetMethods(ALL_DECLARED_METHODS_BINDING_FLAGS)
                .Where(m => !ExcludeMethodsDefinedOnTypes.Contains(m.DeclaringType))
                .ToList();

            var matches = declaredCreatedMethods
                .Where(m => string.Equals(ResolveMethodIdentity(m), targetIdentity, StringComparison.Ordinal))
                .ToList();

            if (matches.Count == 0)
            {
                return ExactTargetResolution.Failed("created-method-not-found");
            }
            if (matches.Count > 1)
            {
                return ExactTargetResolution.Failed("created-method-ambiguous");
            }

            var createdTypeMethodToUpdate = matches[0];
            if (exactExistingMethodToUpdate.IsGenericMethod || createdTypeMethodToUpdate.IsGenericMethod)
            {
                return ExactTargetResolution.Failed("generic-method");
            }

            var skippedIdentities = declaredCreatedMethods
                .Where(m => m != createdTypeMethodToUpdate)
                .Select(ResolveMethodIdentity)
                .ToList();

            return ExactTargetResolution.Resolved(createdTypeMethodToUpdate, targetIdentity, skippedIdentities);
        }

        public Type GetRedirectedType(Type forExistingType)
        {
            return _existingTypeToRedirectedType[forExistingType];
        }

        private static bool DidFieldsOrPropertyCountChanged(Type createdType, Type matchingTypeInExistingAssemblies)
        {
            var createdTypeFieldAndProperties = createdType.GetFields(ALL_BINDING_FLAGS).Concat(createdType.GetProperties(ALL_BINDING_FLAGS).Cast<MemberInfo>()).ToList();
            var matchingTypeFieldAndProperties = matchingTypeInExistingAssemblies.GetFields(ALL_BINDING_FLAGS).Concat(matchingTypeInExistingAssemblies.GetProperties(ALL_BINDING_FLAGS).Cast<MemberInfo>()).ToList();
            if (createdTypeFieldAndProperties.Count != matchingTypeFieldAndProperties.Count)
            {
                var addedMemberNames = createdTypeFieldAndProperties.Select(m => m.Name).Except(matchingTypeFieldAndProperties.Select(m => m.Name)).ToList();
                LoggerScoped.LogError($"It seems you've added/removed field to changed script. This is not supported and will result in undefined behaviour. Hot-reload will not be performed for type: {matchingTypeInExistingAssemblies.Name}" +
                               $"\r\n\r\nYou can skip the check and force reload anyway if needed, to do so go to: 'Window -> Fast Script Reload -> Start Screen -> Reload -> tick 'Disable added/removed fields check'" +
                               (addedMemberNames.Any() ? $"\r\nAdded: {string.Join(", ", addedMemberNames)}" : ""));
                LoggerScoped.Log(
                    $"<color=orange>There's an experimental feature that allows to add new fields (which are adjustable in editor), to enable please:</color>" +
                    $"\r\n - Open Settings 'Window -> Fast Script Reload -> Start Screen -> New Fields -> tick 'Enable experimental added field support'");
                return true;
            }

            return false;
        }

        private static void FindAndExecuteStaticOnScriptHotReloadNoInstance(Type createdType)
        {
            var onScriptHotReloadStaticFnForType = createdType.GetMethod(ON_HOT_RELOAD_NO_INSTANCE_STATIC_METHOD_NAME,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (onScriptHotReloadStaticFnForType != null)
            {
                UnityMainThreadDispatcher.Instance.Enqueue(() =>
                {
                    onScriptHotReloadStaticFnForType.Invoke(null, null);
                });
            }
        }

        private static void FindAndExecuteOnScriptHotReload(Type originalType, Type detourType)
        {
            var onScriptHotReloadFnForType = originalType.GetMethod(ON_HOT_RELOAD_METHOD_NAME, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (onScriptHotReloadFnForType != null)
            {
                ExecuteFnOnMainThread(originalType, onScriptHotReloadFnForType);
            }
            else
            { 
                //When OnScriptHotReload method is not present in original type reflection can not use method from new type (as instance types are not matching and will cause exception)
                //creating dynamic method and dotouring that solves the issue
                //On some 2020 Unity versions, eg 2020.3.27f DynamicMethod can not be resolved. Using reflection to ensure it can be compiled and potentially run if methods exist
                
                var onScriptHotReloadFnForCreatedType = detourType.GetMethod(ON_HOT_RELOAD_METHOD_NAME, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (onScriptHotReloadFnForCreatedType != null)
                {
                    //PERF: could potentially cache, negligible overhead
                    var dynamicMethodType = Type.GetType("System.Reflection.Emit.DynamicMethod");
                    if (dynamicMethodType == null)
                    {
                        LoggerScoped.LogWarning($"Unable to find DynamicMethod, added {ON_HOT_RELOAD_METHOD_NAME} won't be called. Make sure to add method before initial compilation.");
                        return;
                    }
                    
                    var dynamicMethodCtor = dynamicMethodType.GetConstructor(new Type[] { typeof(string), typeof(Type), typeof(Type[]) });
                    var dynamicMethodDynamicallyAdded = (MethodInfo)dynamicMethodCtor.Invoke(new object[] { ON_HOT_RELOAD_METHOD_NAME + "_DynamicallyAdded", typeof(void), new Type[] { } });
                
                    var getILGeneratorMethod = dynamicMethodType.GetMethod("GetILGenerator", new Type[] { });
                    var gen = getILGeneratorMethod.Invoke(dynamicMethodDynamicallyAdded, new object[]{ });
                
                    var emitMethod = gen.GetType().GetMethod("Emit", new [] { typeof(OpCode) });
                    emitMethod.Invoke(gen, new object[] { OpCodes.Ret }); //simple return to ensure IL is valid
                    
                    Memory.DetourMethod(dynamicMethodDynamicallyAdded, onScriptHotReloadFnForCreatedType);

                    ExecuteFnOnMainThread(originalType, dynamicMethodDynamicallyAdded);
                }
            }
        }

        private static void ExecuteFnOnMainThread(Type originalType, MethodInfo onScriptHotReloadFn)
        {
            UnityMainThreadDispatcher.Instance.Enqueue(() =>
            {
                if (!typeof(MonoBehaviour).IsAssignableFrom(originalType))
                {
                    LoggerScoped.LogWarning($"Type: {originalType.Name} is not {nameof(MonoBehaviour)}, {ON_HOT_RELOAD_METHOD_NAME} method can't be executed. You can still use static version: {ON_HOT_RELOAD_NO_INSTANCE_STATIC_METHOD_NAME}");
                    return;
                }
                       //TODO: perf - could find them in different way?
#if UNITY_6000_0_OR_NEWER // added new FindObjectsByType
                foreach (var instanceOfType in UnityEngine.Object.FindObjectsByType(originalType, FindObjectsSortMode.None))
                    onScriptHotReloadFn.Invoke(instanceOfType, null);
#elif UNITY_2021_1_OR_NEWER // keeping FindObjectOfType for older unity versions
                foreach (var instanceOfType in UnityEngine.Object.FindObjectsOfType(originalType))
                    onScriptHotReloadFn.Invoke(instanceOfType, null);
#endif
            });
        }

        private static string RemoveClassPostfix(string fqdn)
        {
            return fqdn.Replace(ClassnamePatchedPostfix, string.Empty);
        }

        private static string ResolveMethodIdentity(MethodBase method)
        {
            return RemoveClassPostfix(method.ResolveFullName());
        }
    }
    
    
    [AttributeUsage(AttributeTargets.Assembly)]
    public class DynamicallyCreatedAssemblyAttribute : Attribute
    {
        public DynamicallyCreatedAssemblyAttribute()
        {
        }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class PreventHotReload : Attribute
    {
        
    }
    
    public interface IAssemblyChangesLoader
    {
        void DynamicallyUpdateMethodsForCreatedAssembly(Assembly dynamicallyLoadedAssemblyWithUpdates, AssemblyChangesLoaderEditorOptionsNeededInBuild editorOptions);
    }

    /// <summary>
    /// Receipt of one <see cref="AssemblyChangesLoader.DynamicallyUpdateSingleMethodForCreatedAssembly"/>
    /// call (P0-60). Never partially applied: either exactly one detour
    /// happened (<see cref="Applied"/> true, <see cref="FailureReason"/>
    /// null) or nothing happened (<see cref="Applied"/> false, a non-null
    /// <see cref="FailureReason"/>). Missing/ambiguous/exception outcomes are
    /// always failures here -- the caller (the Biome adapter) is responsible
    /// for never treating a failure as a retryable state.
    /// </summary>
    public sealed class ExactTargetPatchResult
    {
        public bool Applied { get; }
        public string AppliedMethodIdentity { get; }
        public IReadOnlyList<string> SkippedMethodIdentities { get; }
        public string FailureReason { get; }

        private ExactTargetPatchResult(
            bool applied, string appliedMethodIdentity, IReadOnlyList<string> skippedMethodIdentities, string failureReason)
        {
            Applied = applied;
            AppliedMethodIdentity = appliedMethodIdentity;
            SkippedMethodIdentities = skippedMethodIdentities ?? new List<string>();
            FailureReason = failureReason;
        }

        public static ExactTargetPatchResult Succeeded(string appliedMethodIdentity, IReadOnlyList<string> skippedMethodIdentities) =>
            new ExactTargetPatchResult(true, appliedMethodIdentity, skippedMethodIdentities, null);

        public static ExactTargetPatchResult Failed(string reason) =>
            new ExactTargetPatchResult(false, null, null, reason);
    }

    /// <summary>
    /// Pure result of <see cref="AssemblyChangesLoader.ResolveExactTarget"/>:
    /// either a resolved created method plus the identities of every sibling
    /// declared method that will be skipped (never detoured), or a failure
    /// reason. Never touches Memory.DetourMethod -- kept reflection-only so
    /// it is safe and fully deterministic to exercise outside Unity.
    /// </summary>
    internal sealed class ExactTargetResolution
    {
        public MethodInfo CreatedMethod { get; }
        public string TargetIdentity { get; }
        public IReadOnlyList<string> SkippedIdentities { get; }
        public string FailureReason { get; }

        private ExactTargetResolution(
            MethodInfo createdMethod, string targetIdentity, IReadOnlyList<string> skippedIdentities, string failureReason)
        {
            CreatedMethod = createdMethod;
            TargetIdentity = targetIdentity;
            SkippedIdentities = skippedIdentities ?? new List<string>();
            FailureReason = failureReason;
        }

        public static ExactTargetResolution Resolved(MethodInfo createdMethod, string targetIdentity, IReadOnlyList<string> skippedIdentities) =>
            new ExactTargetResolution(createdMethod, targetIdentity, skippedIdentities, null);

        public static ExactTargetResolution Failed(string reason) =>
            new ExactTargetResolution(null, null, null, reason);
    }
    
    [Serializable]
    public class AssemblyChangesLoaderEditorOptionsNeededInBuild
    {
        public bool IsDidFieldsOrPropertyCountChangedCheckDisabled;
        public bool EnableExperimentalAddedFieldsSupport;

        public AssemblyChangesLoaderEditorOptionsNeededInBuild(bool isDidFieldsOrPropertyCountChangedCheckDisabled, bool enableExperimentalAddedFieldsSupport)
        {
            IsDidFieldsOrPropertyCountChangedCheckDisabled = isDidFieldsOrPropertyCountChangedCheckDisabled;
            EnableExperimentalAddedFieldsSupport = enableExperimentalAddedFieldsSupport;
        }
        
#pragma warning disable 0618
        [Obsolete("Needed for network serialization")]
#pragma warning restore 0618
        public AssemblyChangesLoaderEditorOptionsNeededInBuild()
        {
        }

        //WARN: make sure it has same params as ctor
        public void UpdateValues(bool isDidFieldsOrPropertyCountChangedCheckDisabled, bool enableExperimentalAddedFieldsSupport)
        {
            IsDidFieldsOrPropertyCountChangedCheckDisabled = isDidFieldsOrPropertyCountChangedCheckDisabled;
            EnableExperimentalAddedFieldsSupport = enableExperimentalAddedFieldsSupport;
        }
    }
}
#endif
