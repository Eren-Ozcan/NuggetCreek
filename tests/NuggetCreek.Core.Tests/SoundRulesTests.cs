using NUnit.Framework;
using NuggetCreek.Core;

namespace NuggetCreek.Core.Tests
{
    public class SoundRulesTests
    {
        [Test]
        public void CatchRunClimbsTheScaleAndStartsOver()
        {
            Assert.That(SoundRules.PickPitch(0), Is.EqualTo(1f));
            // D mixolydian: whole tone, whole tone, half tone, whole tone.
            Assert.That(SoundRules.PickPitch(1), Is.EqualTo(1.1225f).Within(0.0001f));
            Assert.That(SoundRules.PickPitch(4), Is.EqualTo(1.4983f).Within(0.0001f));
            Assert.That(SoundRules.PickPitch(SoundRules.PickSteps), Is.EqualTo(1f));
            for (int i = 1; i < SoundRules.PickSteps; i++)
                Assert.That(SoundRules.PickPitch(i), Is.GreaterThan(SoundRules.PickPitch(i - 1)));
        }

        [Test]
        public void DecibelsTurnIntoGain()
        {
            Assert.That(SoundRules.Gain(0), Is.EqualTo(1f));
            Assert.That(SoundRules.Gain(-6), Is.EqualTo(0.501f).Within(0.001f));
        }
    }
}
