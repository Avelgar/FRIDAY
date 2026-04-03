using System.Reflection;

namespace Friday.Services
{
    public static class AppServices
    {
        private static readonly Dictionary<Type, Service> _services = new Dictionary<Type, Service>();

        public static void Init()
        {
            _services.Clear();

            var serviceTypes = Assembly.GetExecutingAssembly()
                                       .GetTypes()
                                       .Where(t => t.IsSubclassOf(typeof(Service)) && !t.IsAbstract);

            foreach (var type in serviceTypes)
            {
                var instance = (Service)Activator.CreateInstance(type);
                _services[type] = instance;
            }

            foreach (var service in _services.Values)
            {
                service.Init();
                Console.WriteLine($"{service.GetType().Name} Initialized");
            }
        }

        public static T Get<T>() where T : Service
        {
            if (_services.TryGetValue(typeof(T), out var service))
            {
                return (T)service;
            }

            throw new Exception($"Сервис типа {typeof(T).Name} не найден или не инициализирован.");
        }

        public static void UpdateVariables()
        {
            foreach (var service in _services.Values)
            {
                service.UpdateVariables();
            }
        }
    }
}