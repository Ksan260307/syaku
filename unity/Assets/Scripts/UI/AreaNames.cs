using System.Collections.Generic;
using System.Linq;

namespace Shakutori
{
    /// <summary>
    /// 画面に出すエリアの名前。まだ行ったことがないエリアの名前は出さない（「？？？」）。
    /// 図鑑のすみか・ヒント・しぼりこみ、記録、トンネルの前のヒント、移動中の字で使う。
    /// </summary>
    public static class AreaNames
    {
        public const string Unknown = "？？？";

        /// <summary>行ったことがある（いまいる）エリアか。</summary>
        public static bool Known(AreaLayout area) =>
            area != null && (area == Areas.Current || (SaveSystem.Data != null && SaveSystem.Data.visited.Contains(area.Id)));

        public static bool Visited(AreaLayout area) => area != null && SaveSystem.Data != null && SaveSystem.Data.visited.Contains(area.Id);

        /// <summary>エリアの名前（行ったことがなければ「？？？」）。</summary>
        public static string Name(AreaLayout area) => Known(area) ? area.DisplayName : Unknown;

        /// <summary>名前（森・川辺…）のエリア。</summary>
        public static AreaLayout ByName(string name) => Areas.All.FirstOrDefault(a => a.DisplayName == name);

        /// <summary>
        /// 「森・川辺・神社」のような、すみかの名前のならび：行ったことがないエリアは、まとめて最後に「？？？」ひとつ。
        /// </summary>
        public static string Label(string label)
        {
            if (string.IsNullOrEmpty(label)) return label;
            var shown = new List<string>();
            bool hidden = false;
            foreach (var name in label.Split('・'))
            {
                var area = ByName(name);
                if (area == null || Known(area)) shown.Add(name);
                else hidden = true;
            }
            if (hidden) shown.Add(Unknown);
            return string.Join("・", shown);
        }

        /// <summary>
        /// 文（ヒント・説明）の中の、行ったことがないエリアの名前を「？？？」にする。
        /// 1 文字の名前（森・山）は、ほかのことばの中にも出てくる（「まきの山」など）ので、かえない。
        /// </summary>
        public static string Mask(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            foreach (var area in Areas.All.OrderByDescending(a => a.DisplayName.Length))
                if (area.DisplayName.Length >= 2 && !Known(area)) text = text.Replace(area.DisplayName, Unknown);
            return text;
        }
    }
}
