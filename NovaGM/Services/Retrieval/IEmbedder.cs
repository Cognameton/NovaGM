using System.Threading.Tasks;

namespace NovaGM.Services
{
    public interface IEmbedder
    {
        int Dimension { get; }
        float[] Embed(string text);
        Task<float[]> EmbedAsync(string text) => Task.FromResult(Embed(text));
    }
}
