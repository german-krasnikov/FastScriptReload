using System;
using System.Threading;
using Biome.SourcePatch.FSRAdapter;

internal static class SingleFlightGateHarness
{
    private static int Main(string[] args)
    {
        var mode = args.Length == 0 ? "sequential" : args[0];

        if (mode == "sequential")
        {
            var gate = new BiomeSingleFlightGate();
            if (!gate.TryEnter()) return 1;
            gate.Exit();
            if (!gate.TryEnter()) return 2;
            gate.Exit();
            Console.WriteLine("SEQUENTIAL-OK");
            return 0;
        }

        if (mode == "reentrant")
        {
            var gate = new BiomeSingleFlightGate();
            if (!gate.TryEnter()) return 3;
            if (gate.TryEnter()) return 4; // must be rejected: still occupied
            Console.WriteLine("REJECTED:reentrant");
            return 0;
        }

        if (mode == "concurrent")
        {
            const int count = 32;
            var gate = new BiomeSingleFlightGate();
            var start = new ManualResetEvent(false);
            var entered = new bool[count];
            var threads = new Thread[count];
            for (var i = 0; i < count; i++)
            {
                var index = i;
                threads[i] = new Thread(() =>
                {
                    start.WaitOne();
                    entered[index] = gate.TryEnter();
                });
                threads[i].Start();
            }
            start.Set();
            foreach (var thread in threads) thread.Join();

            var successCount = 0;
            foreach (var value in entered) if (value) successCount++;
            if (successCount != 1) return 5;
            Console.WriteLine("CONCURRENT-EXACTLY-ONE");
            return 0;
        }

        return 64;
    }
}
