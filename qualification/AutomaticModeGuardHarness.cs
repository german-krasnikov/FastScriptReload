using System;
using System.Collections.Generic;
using System.Linq;
using Biome.SourcePatch.FSRAdapter;

internal sealed class FakeAutomaticModeSwitch : IAutomaticModeSwitch
{
    public string Name { get; }
    public int DisableCallCount { get; private set; }
    private bool _confirmsDisabled;
    private readonly bool _everConfirms;

    public FakeAutomaticModeSwitch(string name, bool everConfirms)
    {
        Name = name;
        _everConfirms = everConfirms;
    }

    public void Disable()
    {
        DisableCallCount++;
        if (_everConfirms) _confirmsDisabled = true;
    }

    public bool IsDisabled() => _confirmsDisabled;
}

internal static class AutomaticModeGuardHarness
{
    private static readonly string[] FiveModeNames =
    {
        "enable-auto-reload-for-changed-files",
        "enable-on-demand-reload",
        "watch-only-specified",
        "enable-experimental-editor-hot-reload-support",
        "enable-custom-file-watcher",
    };

    private static int Main(string[] args)
    {
        var mode = args.Length == 0 ? "all-confirmed" : args[0];

        if (mode == "all-confirmed")
        {
            var switches = FiveModeNames.Select(n => new FakeAutomaticModeSwitch(n, true)).ToList();
            var result = AutomaticModeGuardLogic.DisableAndVerify(switches);
            if (!result.AllConfirmed) return 1;
            if (switches.Any(s => s.DisableCallCount != 1)) return 2;
            Console.WriteLine("ALL-CONFIRMED");
            return 0;
        }

        if (mode == "one-unconfirmed")
        {
            var switches = FiveModeNames
                .Select((n, i) => new FakeAutomaticModeSwitch(n, everConfirms: i != 0))
                .ToList();
            var result = AutomaticModeGuardLogic.DisableAndVerify(switches);
            if (result.AllConfirmed) return 3;
            if (result.UnconfirmedModes.Count != 1) return 4;
            Console.WriteLine("REJECTED:" + result.UnconfirmedModes[0]);
            return 0;
        }

        if (mode == "no-short-circuit")
        {
            // The first mode never confirms; every other mode must still be
            // disabled (Disable() called) even though the pass will fail
            // overall -- fail-closed must not mean "stop early".
            var switches = FiveModeNames
                .Select((n, i) => new FakeAutomaticModeSwitch(n, everConfirms: i != 0))
                .ToList();
            AutomaticModeGuardLogic.DisableAndVerify(switches);
            if (switches.Any(s => s.DisableCallCount != 1)) return 5;
            Console.WriteLine("NO-SHORT-CIRCUIT");
            return 0;
        }

        return 64;
    }
}
