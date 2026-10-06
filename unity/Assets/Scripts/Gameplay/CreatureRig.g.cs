// 自動生成ファイル（blender/scripts/build_creatures.py）。手で編集しないこと。
using System.Collections.Generic;
using UnityEngine;

namespace Shakutori
{
    public static partial class CreatureRig
    {
        static readonly Dictionary<string, LegMount[]> Generated = new Dictionary<string, LegMount[]>
        {
            { "Ladybug", new[] { new LegMount("Ladybug_Leg1", new Vector3(-0.0900f, 0.0400f, -0.1200f), new Vector3(-0.2100f, 0.0000f, -0.1500f)), new LegMount("Ladybug_Leg2", new Vector3(-0.0900f, 0.0400f, -0.0000f), new Vector3(-0.2100f, 0.0000f, -0.0000f)), new LegMount("Ladybug_Leg3", new Vector3(-0.0900f, 0.0400f, 0.1200f), new Vector3(-0.2100f, 0.0000f, 0.1500f)) } },
            { "Ant", new[] { new LegMount("Ant_Leg1", new Vector3(-0.0250f, 0.0600f, -0.0600f), new Vector3(-0.1500f, 0.0000f, -0.1100f)), new LegMount("Ant_Leg2", new Vector3(-0.0250f, 0.0600f, -0.0300f), new Vector3(-0.1500f, 0.0000f, -0.0300f)), new LegMount("Ant_Leg3", new Vector3(-0.0250f, 0.0600f, -0.0000f), new Vector3(-0.1500f, 0.0000f, 0.0600f)) } },
            { "Beetle", new[] { new LegMount("Beetle_Leg1", new Vector3(-0.2500f, 0.1800f, -0.4000f), new Vector3(-0.6200f, 0.0000f, -0.4800f)), new LegMount("Beetle_Leg2", new Vector3(-0.2500f, 0.1800f, -0.1500f), new Vector3(-0.6200f, 0.0000f, -0.2300f)), new LegMount("Beetle_Leg3", new Vector3(-0.2500f, 0.1800f, 0.2000f), new Vector3(-0.6200f, 0.0000f, 0.1200f)) } },
            { "WaterStrider", new[] { new LegMount("WaterStrider_Leg1", new Vector3(-0.0300f, 0.1300f, -0.2200f), new Vector3(-0.1200f, 0.0500f, -0.3800f)), new LegMount("WaterStrider_Leg2", new Vector3(-0.0300f, 0.1200f, -0.1000f), new Vector3(-0.8500f, 0.0000f, -0.7500f)), new LegMount("WaterStrider_Leg3", new Vector3(-0.0300f, 0.1200f, 0.0500f), new Vector3(-0.7000f, 0.0000f, 0.7500f)) } },
            { "Crab", new[] { new LegMount("Crab_Leg1", new Vector3(-0.3500f, 0.2600f, -0.1000f), new Vector3(-0.7200f, 0.0000f, -0.1000f)), new LegMount("Crab_Leg2", new Vector3(-0.3500f, 0.2600f, 0.0200f), new Vector3(-0.7200f, 0.0000f, 0.0800f)), new LegMount("Crab_Leg3", new Vector3(-0.3500f, 0.2600f, 0.1400f), new Vector3(-0.7200f, 0.0000f, 0.2600f)), new LegMount("Crab_Leg4", new Vector3(-0.3500f, 0.2600f, 0.2400f), new Vector3(-0.7200f, 0.0000f, 0.4200f)) } },
            { "Grasshopper", new[] { new LegMount("Grasshopper_Leg1", new Vector3(-0.0800f, 0.1500f, -0.3500f), new Vector3(-0.2400f, 0.0000f, -0.4500f)), new LegMount("Grasshopper_Leg2", new Vector3(-0.0800f, 0.1500f, -0.1800f), new Vector3(-0.2400f, 0.0000f, -0.2800f)) } },
            { "Otoshibumi", new[] { new LegMount("Otoshibumi_Leg1", new Vector3(-0.0500f, 0.0600f, -0.0500f), new Vector3(-0.1400f, 0.0000f, -0.0800f)), new LegMount("Otoshibumi_Leg2", new Vector3(-0.0500f, 0.0600f, 0.0200f), new Vector3(-0.1400f, 0.0000f, -0.0100f)), new LegMount("Otoshibumi_Leg3", new Vector3(-0.0500f, 0.0600f, 0.0900f), new Vector3(-0.1400f, 0.0000f, 0.0600f)) } },
            { "Spider", new[] { new LegMount("Spider_Leg1", new Vector3(-0.0600f, 0.1200f, -0.1100f), new Vector3(-0.3000f, 0.0000f, -0.3000f)), new LegMount("Spider_Leg2", new Vector3(-0.0600f, 0.1200f, -0.0700f), new Vector3(-0.3000f, 0.0000f, -0.1300f)), new LegMount("Spider_Leg3", new Vector3(-0.0600f, 0.1200f, -0.0300f), new Vector3(-0.3000f, 0.0000f, 0.0600f)), new LegMount("Spider_Leg4", new Vector3(-0.0600f, 0.1200f, 0.0100f), new Vector3(-0.3000f, 0.0000f, 0.2500f)) } },
            { "Mantis", new[] { new LegMount("Mantis_Leg1", new Vector3(-0.0500f, 0.4400f, 0.0200f), new Vector3(-0.5000f, 0.0000f, -0.2600f)), new LegMount("Mantis_Leg2", new Vector3(-0.0500f, 0.4400f, 0.1800f), new Vector3(-0.5600f, 0.0000f, 0.7000f)) } },
        };

        public static readonly Vector3 GrasshopperHip = new Vector3(-0.1000f, 0.2500f, 0.0500f);
        public static readonly Vector3 MantisShoulder = new Vector3(-0.0500f, 0.6600f, -0.4200f);
    }
}
