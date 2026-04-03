using Newtonsoft.Json;
using System.IO;

namespace Friday.Managers
{
    public class AppItem
    {
        public string Name { get; set; }
        public string Path { get; set; }
    }

    public static class AppPathManager
    {
        private static readonly string FilePath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\Assets\app_paths.json"));

        public static List<AppItem> LoadApps()
        {
            if (!File.Exists(FilePath))
                return new List<AppItem>();

            try
            {
                var content = File.ReadAllText(FilePath);
                return JsonConvert.DeserializeObject<List<AppItem>>(content) ?? new List<AppItem>();
            }
            catch
            {
                return new List<AppItem>();
            }
        }

        public static void SaveApps(List<AppItem> apps)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                var content = JsonConvert.SerializeObject(apps, Formatting.Indented);
                File.WriteAllText(FilePath, content);
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"Ошибка сохранения путей: {ex.Message}");
            }
        }
    }
}