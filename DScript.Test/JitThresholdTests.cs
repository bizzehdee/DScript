using NUnit.Framework;
using DScript.Vm;

namespace DScript.Test
{
    [TestFixture]
    public class JitThresholdTests
    {
        [Test]
        public void IsHot_BelowBothThresholds_IsFalse()
        {
            var chunk = new Chunk
            {
                InvocationCount = JitThresholds.InvocationThreshold - 1,
                BackEdgeCount   = JitThresholds.BackEdgeThreshold - 1,
            };
            Assert.That(chunk.IsHot(), Is.False,
                "a cold chunk below both thresholds is not hot");
        }

        [Test]
        public void IsHot_AtInvocationThreshold_IsTrue()
        {
            var chunk = new Chunk { InvocationCount = JitThresholds.InvocationThreshold };
            Assert.That(chunk.IsHot(), Is.True,
                "reaching the invocation threshold makes the chunk hot");
        }

        [Test]
        public void IsHot_AtBackEdgeThreshold_IsTrue()
        {
            var chunk = new Chunk { BackEdgeCount = JitThresholds.BackEdgeThreshold };
            Assert.That(chunk.IsHot(), Is.True,
                "reaching the back-edge threshold makes the chunk hot");
        }

        [Test]
        public void IsHot_AlreadyCompiled_IsFalse()
        {
            var chunk = new Chunk
            {
                InvocationCount = JitThresholds.InvocationThreshold,
                JitState        = Chunk.JitStatus.Compiled,
            };
            Assert.That(chunk.IsHot(), Is.False,
                "an already-compiled chunk is never re-flagged as hot");
        }

        [Test]
        public void IsHot_Failed_IsFalse()
        {
            var chunk = new Chunk
            {
                BackEdgeCount = JitThresholds.BackEdgeThreshold,
                JitState      = Chunk.JitStatus.Failed,
            };
            Assert.That(chunk.IsHot(), Is.False,
                "a chunk whose compilation failed is never retried");
        }

        [Test]
        public void IsHot_Compiling_IsFalse()
        {
            var chunk = new Chunk
            {
                InvocationCount = JitThresholds.InvocationThreshold,
                JitState        = Chunk.JitStatus.Compiling,
            };
            Assert.That(chunk.IsHot(), Is.False,
                "a chunk mid-compilation is not flagged hot again (no re-entrancy)");
        }

        [Test]
        public void JitState_DefaultsToCold()
        {
            Assert.That(new Chunk().JitState, Is.EqualTo(Chunk.JitStatus.Cold));
        }
    }
}
