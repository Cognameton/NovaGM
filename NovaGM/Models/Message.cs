namespace NovaGM.Models
{
    public sealed class Message
    {
        public string Role { get; }
        public string Content { get; }
        public Message(string role, string content) { Role = role; Content = content; }
    }
}
