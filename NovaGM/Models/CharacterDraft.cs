namespace NovaGM.Models
{
    public sealed class CharacterDraft
    {
        public string Name { get; set; } = string.Empty;
        public string Race { get; set; } = string.Empty;
        public string Class { get; set; } = string.Empty;
        public int Level { get; set; } = 1;
        public Stats Stats { get; set; } = new();
    }
}
