using UnityEngine;

namespace RailTracer
{
    // The skill table (Resources/SkillTable): unlocks, Bloodied points, cooldowns and HUD text, in hotkey order.
    // Retune here after playtesting; the rules and the HUD both read it.
    [CreateAssetMenu(menuName = "Rail Tracer/Skill Table")]
    public class SkillTable : ScriptableObject
    {
        public SkillDef[] skills = SkillDef.Defaults();
    }
}
