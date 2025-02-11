using System.Threading.Tasks;

namespace Friday
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            var voiceService = new VoiceService();
            await voiceService.StartListening();
        }
    }
}