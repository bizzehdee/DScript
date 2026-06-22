using System.Collections.Generic;
using DScript;
using DScript.Debugger;
using NUnit.Framework;

namespace DScript.Test
{
    /// <summary>
    /// Tests for the step-debugger interface. Uses a recording debugger that
    /// collects events rather than interacting with a user.
    /// </summary>
    public class DebuggerTests
    {
        // Debugger that records every pause and always returns the same action.
        private sealed class RecordingDebugger : IDebugger
        {
            private readonly DebugAction _action;
            public readonly List<DebugEvent> Events = [];

            public RecordingDebugger(DebugAction action) => _action = action;

            public DebugAction OnPause(DebugEvent ev)
            {
                Events.Add(ev);
                return _action;
            }
        }

        // Debugger that returns actions from a queue; falls back to Continue.
        private sealed class ScriptedDebugger : IDebugger
        {
            private readonly Queue<DebugAction> _actions;
            public readonly List<DebugEvent> Events = [];

            public ScriptedDebugger(params DebugAction[] actions) =>
                _actions = new Queue<DebugAction>(actions);

            public DebugAction OnPause(DebugEvent ev)
            {
                Events.Add(ev);
                return _actions.Count > 0 ? _actions.Dequeue() : DebugAction.Continue;
            }
        }

        private static ScriptEngine MakeEngine() => new();

        // ── basic step-in ──────────────────────────────────────────────────

        [Test]
        public void StepInPausesAtEveryNewLine()
        {
            var engine = MakeEngine();
            var debugger = new RecordingDebugger(DebugAction.StepIn);
            engine.AttachDebugger(debugger, DebugAction.StepIn);

            engine.Execute("var x = 1;\nvar y = 2;\nvar z = x + y;");

            // Three source lines → three distinct pause events.
            Assert.That(debugger.Events.Count, Is.EqualTo(3));
        }

        [Test]
        public void PauseEventCarriesCorrectLineNumber()
        {
            var engine = MakeEngine();
            var debugger = new RecordingDebugger(DebugAction.StepIn);
            engine.AttachDebugger(debugger, DebugAction.StepIn);

            engine.Execute("var a = 10;\nvar b = 20;");

            Assert.That(debugger.Events[0].Location.Line, Is.EqualTo(1));
            Assert.That(debugger.Events[1].Location.Line, Is.EqualTo(2));
        }

        [Test]
        public void PauseEventExposesCurrentLocals()
        {
            var engine = MakeEngine();

            // Step through both lines; x is declared and assigned on line 1.
            // The hook fires BEFORE the instruction, so x appears from line 2 onward.
            var debugger = new RecordingDebugger(DebugAction.StepIn);
            engine.AttachDebugger(debugger, DebugAction.StepIn);

            engine.Execute("var x = 42;\nvar y = 0;");

            // After line 1 executed, the frame at the line-2 pause contains x=42.
            var line2Frame = debugger.Events[1].CallStack[0];
            Assert.That(line2Frame.Locals, Has.Some.Matches<(string Name, ScriptVar Value)>(
                l => l.Name == "x" && l.Value.Int == 42));
        }

        [Test]
        public void CallStackContainsCurrentFrame()
        {
            var engine = MakeEngine();
            var debugger = new RecordingDebugger(DebugAction.Continue);
            engine.AttachDebugger(debugger, DebugAction.StepIn);

            engine.Execute("var x = 1;");

            Assert.That(debugger.Events[0].CallStack.Count, Is.GreaterThanOrEqualTo(1));
            Assert.That(debugger.Events[0].CallStack[0].Location.Line, Is.EqualTo(1));
        }

        // ── continue / breakpoints ─────────────────────────────────────────

        [Test]
        public void ContinueWithNoBreakpointsNeverPauses()
        {
            var engine = MakeEngine();
            var debugger = new RecordingDebugger(DebugAction.Continue);
            engine.AttachDebugger(debugger, DebugAction.Continue);

            engine.Execute("var x = 1;\nvar y = 2;\nvar z = 3;");

            Assert.That(debugger.Events.Count, Is.EqualTo(0));
        }

        [Test]
        public void BreakpointPausesOnTheCorrectLine()
        {
            var engine = MakeEngine();
            var debugger = new RecordingDebugger(DebugAction.Continue);
            engine.AttachDebugger(debugger, DebugAction.Continue);
            engine.AddBreakpoint("<main>", 2);

            engine.Execute("var x = 1;\nvar y = 2;\nvar z = 3;");

            Assert.That(debugger.Events.Count, Is.EqualTo(1));
            Assert.That(debugger.Events[0].Location.Line, Is.EqualTo(2));
        }

        [Test]
        public void MultipleBreakpointsAllFire()
        {
            var engine = MakeEngine();
            var debugger = new RecordingDebugger(DebugAction.Continue);
            engine.AttachDebugger(debugger, DebugAction.Continue);
            engine.AddBreakpoint("<main>", 1);
            engine.AddBreakpoint("<main>", 3);

            engine.Execute("var a = 1;\nvar b = 2;\nvar c = 3;");

            Assert.That(debugger.Events.Count, Is.EqualTo(2));
            Assert.That(debugger.Events[0].Location.Line, Is.EqualTo(1));
            Assert.That(debugger.Events[1].Location.Line, Is.EqualTo(3));
        }

        [Test]
        public void RemoveBreakpointStopsItFiring()
        {
            var engine = MakeEngine();
            var debugger = new RecordingDebugger(DebugAction.Continue);
            engine.AttachDebugger(debugger, DebugAction.Continue);
            engine.AddBreakpoint("<main>", 2);
            engine.RemoveBreakpoint("<main>", 2);

            engine.Execute("var x = 1;\nvar y = 2;");

            Assert.That(debugger.Events.Count, Is.EqualTo(0));
        }

        // ── step-over ─────────────────────────────────────────────────────

        [Test]
        public void StepOverDoesNotEnterCalledFunction()
        {
            var engine = MakeEngine();
            // StepOver from the start: should not pause inside the function body.
            var debugger = new RecordingDebugger(DebugAction.StepOver);
            engine.AttachDebugger(debugger, DebugAction.StepOver);

            engine.Execute(
                "function add(a, b) {\n" +   // line 1  — function definition
                "  return a + b;\n" +         // line 2  — inside function (should NOT pause)
                "}\n" +                        // line 3
                "var r = add(1, 2);\n" +       // line 4  — call site
                "var s = r + 1;");             // line 5

            var lines = debugger.Events.ConvertAll(e => e.Location.Line);
            Assert.That(lines, Has.No.Member(2), "Should not stop inside function body during StepOver");
        }

        // ── step-out ──────────────────────────────────────────────────────

        [Test]
        public void StepOutReturnsToCallerLine()
        {
            var engine = MakeEngine();
            // Start StepIn, then StepOut from inside the function.
            var debugger = new ScriptedDebugger(
                DebugAction.StepIn,   // pause at line 1 (function decl) → StepIn
                DebugAction.StepIn,   // pause at line 4 (call) → StepIn (enters function)
                DebugAction.StepOut,  // pause at line 2 (inside fn) → StepOut
                DebugAction.Continue  // resume after returning to caller
            );
            engine.AttachDebugger(debugger, DebugAction.StepIn);

            engine.Execute(
                "function inc(n) {\n" +   // line 1
                "  return n + 1;\n" +     // line 2
                "}\n" +                   // line 3
                "var r = inc(5);\n" +     // line 4
                "var s = 0;");            // line 5

            // After StepOut from line 2 we resume in the caller. The first pause
            // is still on line 4 (completing the call-site assignment of r), then
            // Continue runs to end. Verify we're back in <main> (not the function).
            var afterStepOut = debugger.Events[3];
            Assert.That(afterStepOut.Location.Source, Is.EqualTo("<main>"));
            Assert.That(afterStepOut.Location.Line, Is.GreaterThanOrEqualTo(4));
        }

        // ── call stack depth ──────────────────────────────────────────────

        [Test]
        public void CallStackGrowsOnFunctionEntry()
        {
            var engine = MakeEngine();
            // Collect events, stepping into function calls.
            var debugger = new RecordingDebugger(DebugAction.StepIn);
            engine.AttachDebugger(debugger, DebugAction.StepIn);

            engine.Execute(
                "function inner() {\n" +   // line 1
                "  var x = 1;\n" +         // line 2  ← inside function
                "}\n" +                    // line 3
                "inner();");               // line 4

            // Find an event that paused inside the function (line 2).
            var innerPause = debugger.Events.Find(e => e.Location.Line == 2);
            Assert.That(innerPause, Is.Not.Null);
            // Call stack should have at least 2 frames: inner + top-level script.
            Assert.That(innerPause.CallStack.Count, Is.GreaterThanOrEqualTo(2));
        }

        [Test]
        public void DetachDebuggerStopsAllPauses()
        {
            var engine = MakeEngine();
            var debugger = new RecordingDebugger(DebugAction.StepIn);
            engine.AttachDebugger(debugger, DebugAction.StepIn);
            engine.DetachDebugger();

            engine.Execute("var x = 1;\nvar y = 2;");

            Assert.That(debugger.Events.Count, Is.EqualTo(0));
        }
    }
}
