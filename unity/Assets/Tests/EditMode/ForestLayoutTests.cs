using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Shakutori.Tests
{
    public class ForestLayoutTests
    {
        [Test]
        public void Height_IsFiniteAndDeterministic()
        {
            for (float x = -95f; x <= 95f; x += 7.3f)
            for (float z = -95f; z <= 95f; z += 7.3f)
            {
                float h = ForestLayout.Height(x, z);
                Assert.IsFalse(float.IsNaN(h) || float.IsInfinity(h), $"({x},{z})");
                Assert.AreEqual(h, ForestLayout.Height(x, z));
                Assert.That(h, Is.InRange(-6f, 20f));
            }
        }

        [Test]
        public void Edge_RisesToFormNaturalWall()
        {
            float inner = 0f, outer = 0f;
            for (int i = 0; i < 16; i++)
            {
                float a = i / 16f * Mathf.PI * 2f;
                inner += ForestLayout.Height(Mathf.Cos(a) * 30f, Mathf.Sin(a) * 30f);
                outer += ForestLayout.Height(Mathf.Cos(a) * 92f, Mathf.Sin(a) * 92f);
            }
            Assert.Greater(outer / 16f, inner / 16f + 5f);
        }

        [Test]
        public void Pond_IsBelowWaterLevelAndShoreIsAbove()
        {
            Vector2 p = ForestLayout.Pond;
            Assert.Less(ForestLayout.Height(p.x, p.y), ForestLayout.WaterLevel - 1f);
            Assert.Greater(ForestLayout.Height(p.x, p.y + ForestLayout.PondRadius * 1.3f), ForestLayout.WaterLevel);
        }

        [Test]
        public void Spawn_IsDryLandInsidePlayArea()
        {
            var s = ForestLayout.Ground(ForestLayout.Spawn.x, ForestLayout.Spawn.y);
            Assert.IsTrue(ForestLayout.InPlayArea(s));
            Assert.IsFalse(ForestLayout.IsUnderwater(s));
        }

        [Test]
        public void Landmarks_AreNineUniqueAndWellFormed()
        {
            var lms = ForestLayout.Landmarks;
            Assert.AreEqual(9, lms.Count);
            CollectionAssert.AreEquivalent(Enumerable.Range(0, 9), lms.Select(l => l.id));
            Assert.AreEqual(lms.Count, lms.Select(l => l.name).Distinct().Count(), "名前が重複しています");
            foreach (var lm in lms)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(lm.name));
                Assert.IsFalse(string.IsNullOrWhiteSpace(lm.description));
                Assert.Less(lm.position.magnitude, ForestLayout.PlayRadius, lm.name);
                Assert.Greater(lm.radius, 0f);
            }
            Assert.IsTrue(lms[8].useCapsule, "丸太のトンネルはカプセル判定");
            Assert.Greater(lms[8].capsuleRadius, 2f);
            Assert.Greater(lms[6].minHeightAboveGround, 3f, "切り株は頂上に登らないと発見できない");
        }

        [Test]
        public void StartLandmark_ContainsSpawn()
        {
            var first = ForestLayout.Landmarks[0];
            var s = ForestLayout.Ground(ForestLayout.Spawn.x, ForestLayout.Spawn.y);
            Assert.IsTrue(Collectibles.IsInside(first, s + Vector3.up * 0.1f));
        }

        [Test]
        public void TrailMask_IsHighOnTrailsAndLowFarAway()
        {
            foreach (var t in ForestLayout.Trails)
            {
                Assert.GreaterOrEqual(t.Length, 2);
                Vector2 mid = (t[0] + t[1]) * 0.5f;
                Assert.Greater(ForestLayout.TrailMask(mid.x, mid.y), 0.4f, $"trail at {mid}");
            }
            Assert.Less(ForestLayout.TrailMask(-60f, 40f), 0.05f);
        }

        [Test]
        public void Normal_IsUnitAndPointsUp()
        {
            for (int i = 0; i < 50; i++)
            {
                float x = -60f + i * 2.4f, z = 30f - i * 1.7f;
                var n = ForestLayout.Normal(x, z);
                Assert.AreEqual(1f, n.magnitude, 1e-3f);
                Assert.Greater(n.y, 0.2f);
            }
        }

        [Test]
        public void GroundColor_IsValidOpaqueColor()
        {
            for (int i = 0; i < 60; i++)
            {
                float x = -80f + i * 2.7f, z = -70f + i * 2.3f;
                float h = ForestLayout.Height(x, z);
                var c = ForestLayout.GroundColor(x, z, h, ForestLayout.Normal(x, z));
                Assert.AreEqual(1f, c.a);
                Assert.That(c.r, Is.InRange(0f, 1f));
                Assert.That(c.g, Is.InRange(0f, 1f));
                Assert.That(c.b, Is.InRange(0f, 1f));
            }
        }

        [Test]
        public void IsUnderwater_OnlyInsidePondBelowSurface()
        {
            Vector2 p = ForestLayout.Pond;
            Assert.IsTrue(ForestLayout.IsUnderwater(new Vector3(p.x, ForestLayout.WaterLevel - 0.5f, p.y)));
            Assert.IsFalse(ForestLayout.IsUnderwater(new Vector3(p.x, ForestLayout.WaterLevel + 0.2f, p.y)));
            Assert.IsFalse(ForestLayout.IsUnderwater(new Vector3(0f, -5f, 0f)), "水たまりの外は水中ではない");
        }

        [Test]
        public void InPlayArea_LimitsRadiusAndHeight()
        {
            Assert.IsTrue(ForestLayout.InPlayArea(Vector3.zero));
            Assert.IsFalse(ForestLayout.InPlayArea(new Vector3(ForestLayout.PlayRadius + 1f, 0f, 0f)));
            Assert.IsFalse(ForestLayout.InPlayArea(new Vector3(0f, ForestLayout.MaxClimbHeight + 1f, 0f)));
        }

        [Test]
        public void LandAroundEachLandmark_ExceptPond()
        {
            foreach (var lm in ForestLayout.Landmarks)
            {
                if (lm.id == 5) continue;
                Assert.Greater(ForestLayout.Height(lm.position.x, lm.position.y), ForestLayout.WaterLevel, lm.name);
            }
        }
    }
}
