using UnityEngine;

namespace Shakutori
{
    /// <summary>しゃくとりむしが水に落ちたとき（泳ぐ・浮いた物に乗る）。</summary>
    public partial class InchwormController
    {
        bool _swimming;

        /// <summary>水の上で泳いでいる。</summary>
        public bool IsSwimming => _swimming;
    }
}
