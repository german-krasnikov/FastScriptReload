using System.Collections.Generic;
using Biome.SourcePatch.FSRAdapter;
using NUnit.Framework;

namespace FastScriptReload.Tests.Editor.Integration.BiomeSourcePatchAdapter
{
    /// <summary>
    /// NUnit mirror of qualification/test_automatic_mode_guard_harness.py --
    /// same scenarios, run through Unity's own test runner once this
    /// package compiles in a disposable worker (P0-80). Pure logic: no
    /// Unity/FSR types touched, so a fake IAutomaticModeSwitch is enough.
    /// </summary>
    public class BiomeAutomaticModeGuardLogicTests
    {
        private sealed class FakeSwitch : IAutomaticModeSwitch
        {
            public string Name { get; }
            public int DisableCallCount { get; private set; }
            private bool _confirmsDisabled;
            private readonly bool _everConfirms;

            public FakeSwitch(string name, bool everConfirms)
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

        [Test]
        public void DisableAndVerify_AllModesConfirm_ReportsAllConfirmed()
        {
            var switches = new List<IAutomaticModeSwitch>
            {
                new FakeSwitch("a", true), new FakeSwitch("b", true), new FakeSwitch("c", true),
            };

            var result = AutomaticModeGuardLogic.DisableAndVerify(switches);

            Assert.IsTrue(result.AllConfirmed);
            Assert.IsEmpty(result.UnconfirmedModes);
        }

        [Test]
        public void DisableAndVerify_OneModeNeverConfirms_NamesExactlyThatMode()
        {
            var switches = new List<IAutomaticModeSwitch>
            {
                new FakeSwitch("a", false), new FakeSwitch("b", true), new FakeSwitch("c", true),
            };

            var result = AutomaticModeGuardLogic.DisableAndVerify(switches);

            Assert.IsFalse(result.AllConfirmed);
            CollectionAssert.AreEqual(new[] { "a" }, result.UnconfirmedModes);
        }

        [Test]
        public void DisableAndVerify_EarlyFailure_StillAttemptsEveryRemainingMode()
        {
            var a = new FakeSwitch("a", false);
            var b = new FakeSwitch("b", true);
            var c = new FakeSwitch("c", true);

            AutomaticModeGuardLogic.DisableAndVerify(new List<IAutomaticModeSwitch> { a, b, c });

            Assert.AreEqual(1, a.DisableCallCount);
            Assert.AreEqual(1, b.DisableCallCount);
            Assert.AreEqual(1, c.DisableCallCount);
        }
    }
}
