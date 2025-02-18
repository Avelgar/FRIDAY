using System;
using System.Diagnostics;
using System.Linq;

namespace Friday
{
    public class AppProcessService
    {
        private bool secureSystemProcesses = true; //мб оно и так не будет их завершать
        public bool KillProcess(string processName)
        {
            bool isKilled = false;
            foreach (var process in Process.GetProcessesByName(processName))
            {
                try
                {
                    process.Kill();
                    isKilled = true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                    isKilled = false;
                }
            }
            return isKilled;
        }

        public bool IsProcessRunning(string processName)
        {
            return Process.GetProcessesByName(processName).Any();
        }
    }
}