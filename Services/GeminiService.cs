using Newtonsoft.Json;
using System.Net.Http;
using System.Text;
using System.Windows.Forms;

namespace Friday
{
    public class GeminiService
    {
        private static readonly string serverUrl = "https://friday-assistant.ru/generate";

        public static async Task<string> GenerateTextAsync(string prompt)
        {
            using (var client = new HttpClient())
            {
                try
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
                catch (HttpRequestException e)
                {
                    MessageBox.Show($"Ошибка при отправке запроса: {e.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return null;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Произошла ошибка: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return null;
                }
            }
        }

    }
}
