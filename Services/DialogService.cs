using Newtonsoft.Json;
using System.ComponentModel;
using System.Net.Http;
using System.Text;

namespace Friday
{
    /// <summary>
    /// Элемент списка диалогов (левая панель на вкладке Home).
    /// Аналог .dialog-item из веб-версии.
    /// </summary>
    public class DialogItem : INotifyPropertyChanged
    {
        [JsonProperty("id")]
        public long Id { get; set; }

        private string _name;
        [JsonProperty("name")]
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(nameof(Name)); }
        }

        [JsonProperty("created_at")]
        public string CreatedAt { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    /// <summary>
    /// Сообщение из истории диалога (ответ /api/get_history).
    /// </summary>
    public class HistoryMessage
    {
        [JsonProperty("id")] public long Id { get; set; }
        [JsonProperty("sender")] public string Sender { get; set; }
        [JsonProperty("text")] public string Text { get; set; }
        [JsonProperty("time")] public string Time { get; set; }
    }

    public class DialogsResponse
    {
        [JsonProperty("status")] public string Status { get; set; }
        [JsonProperty("message")] public string Message { get; set; }
        [JsonProperty("dialogs")] public List<DialogItem> Dialogs { get; set; }
    }

    public class HistoryResponse
    {
        [JsonProperty("status")] public string Status { get; set; }
        [JsonProperty("message")] public string Message { get; set; }
        [JsonProperty("history")] public List<HistoryMessage> History { get; set; }
    }

    public class SimpleResponse
    {
        [JsonProperty("status")] public string Status { get; set; }
        [JsonProperty("message")] public string Message { get; set; }
        [JsonProperty("dialog_id")] public long? DialogId { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
    }

    /// <summary>
    /// REST-клиент для работы с диалогами аккаунта.
    /// Полный аналог fetch-вызовов из script.js:
    ///   loadDialogs()   -> POST /api/get_dialogs   { token }
    ///   selectDialog()  -> POST /api/get_history   { token, dialog_id }
    ///   deleteDialog()  -> POST /api/delete_dialog { token, dialog_id }
    /// ВАЖНО: все эти эндпоинты авторизуются по JWT-ТОКЕНУ, а не по MAC.
    /// </summary>
    public static class DialogService
    {
        private const string BaseUrl = "https://friday-assistant.ru";

        // Один HttpClient на приложение — иначе выжираются сокеты (socket exhaustion).
        private static readonly HttpClient _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        private static StringContent Json(object o) =>
            new StringContent(JsonConvert.SerializeObject(o), Encoding.UTF8, "application/json");

        /// <summary>Список диалогов пользователя (DESC по дате создания).</summary>
        public static async Task<List<DialogItem>> GetDialogsAsync(string token)
        {
            if (string.IsNullOrEmpty(token)) return new List<DialogItem>();
            try
            {
                var resp = await _http.PostAsync($"{BaseUrl}/api/get_dialogs", Json(new { token }));
                var body = await resp.Content.ReadAsStringAsync();
                var data = JsonConvert.DeserializeObject<DialogsResponse>(body);
                if (data?.Status == "success" && data.Dialogs != null) return data.Dialogs;
            }
            catch (Exception ex) { Console.WriteLine($"[DialogService] GetDialogs: {ex.Message}"); }
            return new List<DialogItem>();
        }

        /// <summary>История конкретного диалога. 403, если диалог принадлежит другому юзеру.</summary>
        public static async Task<List<HistoryMessage>> GetHistoryAsync(string token, long dialogId)
        {
            if (string.IsNullOrEmpty(token)) return new List<HistoryMessage>();
            try
            {
                var resp = await _http.PostAsync($"{BaseUrl}/api/get_history", Json(new { token, dialog_id = dialogId }));
                var body = await resp.Content.ReadAsStringAsync();
                var data = JsonConvert.DeserializeObject<HistoryResponse>(body);
                if (data?.Status == "success" && data.History != null) return data.History;
            }
            catch (Exception ex) { Console.WriteLine($"[DialogService] GetHistory: {ex.Message}"); }
            return new List<HistoryMessage>();
        }

        /// <summary>Удаление диалога вместе со всеми его сообщениями (каскад в БД).</summary>
        public static async Task<bool> DeleteDialogAsync(string token, long dialogId)
        {
            if (string.IsNullOrEmpty(token)) return false;
            try
            {
                var resp = await _http.PostAsync($"{BaseUrl}/api/delete_dialog", Json(new { token, dialog_id = dialogId }));
                var body = await resp.Content.ReadAsStringAsync();
                var data = JsonConvert.DeserializeObject<SimpleResponse>(body);
                return data?.Status == "success";
            }
            catch (Exception ex) { Console.WriteLine($"[DialogService] DeleteDialog: {ex.Message}"); return false; }
        }

        /// <summary>
        /// Явное создание диалога. Веб-клиент этим НЕ пользуется (диалог создаётся
        /// автоматически сервером при первом сообщении с dialog_id = null),
        /// метод оставлен для полноты API.
        /// </summary>
        public static async Task<long?> CreateDialogAsync(string token, string name = "Новый чат")
        {
            if (string.IsNullOrEmpty(token)) return null;
            try
            {
                var resp = await _http.PostAsync($"{BaseUrl}/api/create_dialog", Json(new { token, name }));
                var body = await resp.Content.ReadAsStringAsync();
                var data = JsonConvert.DeserializeObject<SimpleResponse>(body);
                if (data?.Status == "success") return data.DialogId;
            }
            catch (Exception ex) { Console.WriteLine($"[DialogService] CreateDialog: {ex.Message}"); }
            return null;
        }

        /// <summary>
        /// Разворачивает текст сообщения бота из БД в читабельный вид.
        /// Формат хранения: несколько действий через '⸵', внутри — "тип|значение".
        /// Веб делает то же самое в selectDialog().
        /// </summary>
        public static string CleanBotText(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            var parts = new List<string>();
            foreach (var action in raw.Split('⸵'))
            {
                int sep = action.IndexOf('|');
                string piece = sep != -1 ? action.Substring(sep + 1).Trim() : action.Trim();
                if (!string.IsNullOrWhiteSpace(piece)) parts.Add(piece);
            }
            return string.Join(Environment.NewLine + Environment.NewLine, parts).Trim();
        }
    }
}
