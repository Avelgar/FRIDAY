using System.Net.Http;
using System.Text;
using Newtonsoft.Json;

namespace Friday
{
    public class GeminiService
    {
        private static readonly string serverUrl = "http://blue.fnode.me:25534/generate";

        public static async Task<string> GenerateTextAsync(string prompt)
        {
            using (var client = new HttpClient())
            {
                var requestBody = new { prompt };
                var json = JsonConvert.SerializeObject(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await client.PostAsync(serverUrl, content);
                response.EnsureSuccessStatusCode();

                var responseJson = await response.Content.ReadAsStringAsync();
                var responseObject = JsonConvert.DeserializeObject<dynamic>(responseJson);

                return responseObject.response;
            }
        }
    }
}
