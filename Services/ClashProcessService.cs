using System;
using System.Diagnostics;
using System.IO;

namespace ClashXW.Services
{
    public class ClashProcessService : IDisposable
    {
        private Process? _clashProcess;
        private readonly string _executablePath;

        public ClashProcessService(string executablePath)
        {
            _executablePath = executablePath;
        }

        public void Start(string configPath)
        {
            if (string.IsNullOrEmpty(_executablePath) || !File.Exists(_executablePath))
            {
                throw new FileNotFoundException($"Clash executable not found at: {_executablePath}");
            }

            try
            {
                var assetsDir = Path.GetDirectoryName(_executablePath);
                _clashProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = _executablePath,
                        Arguments = $"-d \"{assetsDir}\" -f \"{configPath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                _clashProcess.Start();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to start Clash process: {ex.Message}", ex);
            }
        }

        public void Stop()
        {
            if (_clashProcess != null && !_clashProcess.HasExited)
            {
                _clashProcess.Kill();
            }
        }

        public bool IsRunning => _clashProcess != null && !_clashProcess.HasExited;

        public void Dispose()
        {
            Stop();
            _clashProcess?.Dispose();
        }
    }
}
