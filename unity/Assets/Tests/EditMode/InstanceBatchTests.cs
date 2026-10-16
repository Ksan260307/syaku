using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Shakutori.Tests
{
    /// <summary>
    /// まとめ描きの数をそろえる（WebGL）：1 回に描く数は 2〜32 か 224 だけ。頂点の少ない形は、見えない物で 224 にうめ、
    /// 頂点の多い形は 32 個までずつに分ける（うめると GPU が重くなる）。
    /// （WebGL では、まとめる数ごとにシェーダーが用意されるので、そろえないと読み込みがとても長くなった）
    /// </summary>
    public class InstanceBatchTests
    {
        bool _was;
        readonly List<(int start, int n)> _calls = new List<(int, int)>();

        [SetUp]
        public void SetUp()
        {
            _was = InstanceBatch.Enabled;
            InstanceBatch.Enabled = true;
        }

        [TearDown]
        public void TearDown() => InstanceBatch.Enabled = _was;

        void AssertSizes(int count, int verts)
        {
            InstanceBatch.Split(count, verts, _calls);
            int covered = 0;
            foreach (var (start, n) in _calls)
            {
                Assert.AreEqual(covered, start, "すき間なく、順に");
                Assert.IsTrue((n >= 2 && n <= InstanceBatch.Small) || n == InstanceBatch.Full, $"{count} 個（頂点 {verts}）を {n} 個ずつ");
                covered += n;
            }
            Assert.GreaterOrEqual(covered, count, "ぜんぶ描く");
        }

        [Test]
        public void EveryCount_IsDrawnInTwoSizes()
        {
            foreach (int verts in new[] { 12, 60, 400, 3000 })
                for (int c = 1; c <= InstanceBatch.Full; c++) AssertSizes(c, verts);
        }

        [Test]
        public void SmallShapes_ArePadded_BigShapesAreSplit()
        {
            Assert.AreEqual(InstanceBatch.Full, InstanceBatch.Split(200, 60, _calls), "頂点の少ない形は、224 にうめる");
            Assert.AreEqual(1, _calls.Count);
            Assert.AreEqual(100, InstanceBatch.Split(100, 3000, _calls), "頂点の多い形は、うめない");
            Assert.AreEqual(4, _calls.Count, "32 個までずつ、4 回に分ける");
            Assert.AreEqual(2, InstanceBatch.Split(1, 3000, _calls), "1 個は 2 個に（まとめない描き方にならない）");
            InstanceBatch.Enabled = false;
            Assert.AreEqual(37, InstanceBatch.Split(37, 60, _calls), "そろえないとき（エディタ・PC）は、そのまま");
            Assert.AreEqual(1, _calls.Count);
            Assert.AreEqual(1023, InstanceBatch.MaxPerCall);
        }
    }
}
