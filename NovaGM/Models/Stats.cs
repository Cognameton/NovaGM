namespace NovaGM.Models
{
    public sealed class Stats
    {
        public int STR { get; set; } = 10;
        public int DEX { get; set; } = 10;
        public int CON { get; set; } = 10;
        public int INT { get; set; } = 10;
        public int WIS { get; set; } = 10;
        public int CHA { get; set; } = 10;

        public static int Mod(int score) => (int)System.Math.Floor((score - 10) / 2.0);
    }
}
