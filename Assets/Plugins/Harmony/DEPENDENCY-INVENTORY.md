# Thin Harmony qualification dependency inventory

This experimental branch is based on Fast Script Reload commit
`51140b71d9e5df1de231b33ec20ee089b18bebec`. It replaces the malformed bundled
fat Harmony assembly with the unmodified official `Lib.Harmony.Thin` 2.4.2
`.NETFramework4.5.2` asset and its exact official `MonoMod.Core` 1.3.3 closure.
All binaries are imported for the Unity Editor only.

## Source packages

| Package | Version | NuGet URL | Package SHA-256 | License/source |
|---|---:|---|---|---|
| Lib.Harmony.Thin | 2.4.2 | https://api.nuget.org/v3-flatcontainer/lib.harmony.thin/2.4.2/lib.harmony.thin.2.4.2.nupkg | `1e52bc1e4c8b939742998d5220213536df43c13a89c809a591a446cf910e18f0` | MIT; https://github.com/pardeike/Harmony/tree/a264a1bf1ce689e4589e8dcc54b1e2818602a90a |
| MonoMod.Core | 1.3.3 | https://api.nuget.org/v3-flatcontainer/monomod.core/1.3.3/monomod.core.1.3.3.nupkg | `cf9d8082a64ce02e1d11b0ee220bbc2ca76645405b4e3c460d3caba50cc53dcb` | MIT; https://github.com/MonoMod/MonoMod/tree/aa4a8474906ded4423d222cdf9ea5cf884c32b5f |
| MonoMod.Backports | 1.1.2 | https://api.nuget.org/v3-flatcontainer/monomod.backports/1.1.2/monomod.backports.1.1.2.nupkg | `a1785c9cca34ac36437299a119585085ac7494579bf434f719f4a8a1eb0ea37f` | MIT; https://github.com/MonoMod/MonoMod/tree/a1b82852b2574742776af08818487b90b0bfab93 |
| MonoMod.ILHelpers | 1.1.0 | https://api.nuget.org/v3-flatcontainer/monomod.ilhelpers/1.1.0/monomod.ilhelpers.1.1.0.nupkg | `b1ea044f97eab32398f20ec37cda4b407353754558dd4ff10a86050b852b38ac` | MIT; https://github.com/MonoMod/MonoMod/tree/a1b82852b2574742776af08818487b90b0bfab93 |
| MonoMod.Utils | 25.0.11 | https://api.nuget.org/v3-flatcontainer/monomod.utils/25.0.11/monomod.utils.25.0.11.nupkg | `544f7c22a0ac05c345dec524c2f225066302947678d9df4bf81174353ccef50a` | MIT; https://github.com/MonoMod/MonoMod/tree/aa4a8474906ded4423d222cdf9ea5cf884c32b5f |
| Mono.Cecil | 0.11.6 | https://api.nuget.org/v3-flatcontainer/mono.cecil/0.11.6/mono.cecil.0.11.6.nupkg | `d2a23832aaa948ba9a01acc42b5726e34c5f995958f1b30d45c0e7c70b3a72d5` | MIT; https://github.com/jbevain/cecil/tree/0.11.6 |

`System.ValueTuple` 4.5.0 is a declared net452 transitive package dependency,
but that package provides no net45 implementation asset. The shipped MonoMod
assemblies reference `System.ValueTuple, Version=4.0.3.0`; Unity 6 supplies the
matching Editor facade on macOS, Windows, and Linux. No duplicate
`System.ValueTuple.dll` is vendored.

## Shipped assembly assets

| Assembly | Package asset / TFM | SHA-256 |
|---|---|---|
| 0Harmony.dll | Lib.Harmony.Thin 2.4.2 `lib/net452` | `227cbddb0586bddcf1ecef8c33a94aa818e25682874b2d48917cc1779fb3c17f` |
| MonoMod.Core.dll | MonoMod.Core 1.3.3 `lib/net452` | `4bb34dd557481564105e279ee92912d2fa615ba9f39e5212f0031bf62eff9571` |
| MonoMod.Iced.dll | MonoMod.Core 1.3.3 `lib/net452` | `7890f9eeac088c52796c1ae73fa8feb1e7967a36e63f71d84cfb0445045a19c0` |
| MonoMod.Backports.dll | MonoMod.Backports 1.1.2 `lib/net452` | `6db3486eb3bfc458c770d61a1391569000cd871dcc7160fb3da2cbfe8d98601a` |
| MonoMod.ILHelpers.dll | MonoMod.ILHelpers 1.1.0 `lib/net452` | `20df341949b81662e09787e42b66ac3fea601d4cf4b717294f2d72a80860e4ec` |
| MonoMod.Utils.dll | MonoMod.Utils 25.0.11 `lib/net452` | `f0bdd7717cca42312e55ccd23e3ee7b61df104fe9440881d2e8abbebaf8fe644` |
| Mono.Cecil.dll | Mono.Cecil 0.11.6 `lib/net40` | `c41bdb9ffd3c5f6e17d2382c1012d73703e035e3f1100245fdd4e08c8dc6eb5b` |
| Mono.Cecil.Mdb.dll | Mono.Cecil 0.11.6 `lib/net40` | `570a437dea0271d1d5c8b7d6a408b0b2635bdb0e8b8d5051878f3e7fca087f89` |
| Mono.Cecil.Pdb.dll | Mono.Cecil 0.11.6 `lib/net40` | `50a1a1a79dc86fcfb8b51249b5325a10dd93d193c52999cf6775d25030a4e606` |
| Mono.Cecil.Rocks.dll | Mono.Cecil 0.11.6 `lib/net40` | `842e09959084eda733aab1a5354d7af79e29594f4d8b91c8792103e5c755ed9b` |

Mono.Cecil has no net452 asset. `lib/net40` is NuGet's compatible asset for the
net452 closure. No DLL has been rebuilt, patched, merged, or trimmed.

## Fast Script Reload API compatibility inventory

Static IL inspection of the shipped thin `0Harmony.dll` confirms:

- public `HarmonyLib.Harmony`;
- public `HarmonyLib.HarmonyMethod`;
- public `HarmonyLib.AccessTools`;
- private/internal `HarmonyLib.PatchTools`;
- assembly-visible static `PatchTools.DetourMethod(MethodBase, MethodBase)`.

The last two names and signature are the exact reflection seam used by
`Assets/Scripts/Runtime/Polyfills.cs`. The public types cover FSR's direct
Harmony calls. This is static API evidence only; Unity/Burst and the semantic
same-instance canary remain mandatory.

## Metadata risk evidence

`pedump --verify metadata` was run against all ten shipped DLLs. Some official
MonoMod assemblies use metadata rejected by the installed strict Mono
`pedump`; dependent assemblies consequently also return non-zero. The complete
record is in `PEDUMP-REPORT.txt`. This branch does **not** claim metadata-clean.
The single bounded Unity/Burst worker qualification is the deciding oracle.
