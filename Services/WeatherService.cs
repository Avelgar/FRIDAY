using System.Net.Http;
using Newtonsoft.Json.Linq;

namespace Friday
{
    public class WeatherService
    {
        private static readonly string apiKey = "bd5e378503939ddaee76f12ad7a97608";
        private static readonly string city = "Moscow";
        private static readonly string url = $"https://api.openweathermap.org/data/2.5/forecast?q={city}&units=metric&appid={apiKey}&lang=ru";
        public string GetWeatherForecast(int dayOffset)
        {
            using (HttpClient client = new HttpClient())
            {
                HttpResponseMessage response = client.GetAsync(url).GetAwaiter().GetResult();
                if (!response.IsSuccessStatusCode)
                    return "Ошибка получения данных о погоде";

                string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                JObject data = JObject.Parse(json);

                int index = dayOffset * 8;

                if (index >= data["list"].Count())
                    return "Прогноз недоступен на такой день";

                var weatherData = data["list"][index];
                string description = weatherData["weather"][0]["description"].ToString();
                double temp = (double)weatherData["main"]["temp"];
                string dayText = dayOffset switch
                {
                    0 => "сегодня",
                    1 => "завтра",
                    2 => "послезавтра",
                    _ => "указанный день"
                };

                return $"Погода в {city} на {dayText}: {description}, {temp}°C";
            }
        }
    }
}