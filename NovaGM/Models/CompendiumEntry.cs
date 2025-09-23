namespace NovaGM.Models
{
    public sealed class CompendiumEntry
    {
        public string Category { get; set; } = "";   // Race, Class, Skill, Ability, Weapon...
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
    }
}
