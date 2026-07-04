using SPTarkov.DI.Annotations;
using System;
using System.IO;

namespace YATMMedved;

[Injectable]
public class YATMLogger
{
    private const string Prefix = "[YATM Medved]";

    private readonly object _fileLock = new();

    private string? _logPath;
    private bool _initialized;

    public bool IsDebugEnabled { get; set; } = false;
    public bool IsRealDebugEnabled { get; set; } = false;

    public void Init(string modPath)
    {
        _logPath = Path.Combine(modPath, "debug.log");

        try
        {
            Directory.CreateDirectory(modPath);

            File.WriteAllText(
                _logPath,
                $"{Prefix} Debug Log Started at {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
                "------------------------------------------------\n"
            );

            _initialized = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{Prefix} Failed to initialize log file: {ex.Message}");
        }
    }

    public void Info(string message)
    {
        Console.WriteLine($"{Prefix} {message}");
        WriteToFile("INFO", message);
    }

    public void Warning(string message)
    {
        Console.WriteLine($"{Prefix} WARNING: {message}");
        WriteToFile("WARNING", message);
    }

    public void Error(string message)
    {
        Console.WriteLine($"{Prefix} ERROR: {message}");
        WriteToFile("ERROR", message);
    }

    public void Debug(string message)
    {
        if (!IsDebugEnabled)
            return;

        Console.WriteLine($"{Prefix} DEBUG: {message}");
        WriteToFile("DEBUG", message);
    }

    public void RealDebug(string message)
    {
        if (!IsRealDebugEnabled)
            return;

        Console.WriteLine($"{Prefix} REAL DEBUG: {message}");
        WriteToFile("REAL DEBUG", message);
    }

    // Tony-style aliases if you want to copy code over from YATM
    public void Log(string message)
    {
        WriteToFile("LOG", message);
    }

    public void LogDebug(string message)
    {
        Debug(message);
    }

    public void LogRealDebug(string message)
    {
        RealDebug(message);
    }

    private void WriteToFile(string level, string message)
    {
        if (!_initialized || _logPath == null)
            return;

        try
        {
            var formatted = $"[{DateTime.Now:HH:mm:ss}] [{level}] {message}";

            lock (_fileLock)
            {
                File.AppendAllText(_logPath, formatted + "\n");
            }
        }
        catch
        {
            // Fail silently so logging never breaks the server
        }
    }
}