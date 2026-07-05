using SPTarkov.DI.Annotations;
using System;
using System.IO;

namespace YATMMedved;

[Injectable]
public class YATMLogger
{
    private const string Prefix = "[YATM Medved]";

    private readonly Lock _fileLock = new();

    private string? _logPath;
    private bool _initialized;

    public bool IsDebugEnabled { get; set; } = false;
    public bool IsRealDebugEnabled { get; set; } = false;

    public void Init(string modPath)
    {
        if (!IsDebugEnabled)
            return;

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

    /// <summary>
    /// Verbose/info logging.
    /// Use this for noisy loader, zone, wave, and support logs.
    /// Requires BOTH debug flags.
    /// </summary>
    public void Info(string message)
    {
        RealDebug(message);
    }

    /// <summary>
    /// Normal warning.
    /// Only prints/writes when debug is enabled.
    /// </summary>
    public void Warning(string message)
    {
        if (!IsDebugEnabled)
            return;

        Console.WriteLine($"{Prefix} WARNING: {message}");
        WriteToFile("WARNING", message);
    }

    /// <summary>
    /// Error logging.
    /// Only prints/writes when debug is enabled, so Medved stays fully silent when debug is off.
    /// </summary>
    public void Error(string message)
    {
        if (!IsDebugEnabled)
            return;

        Console.WriteLine($"{Prefix} ERROR: {message}");
        WriteToFile("ERROR", message);
    }

    /// <summary>
    /// Normal debug.
    /// Use this for important one-line summary logs.
    /// Example: Medved Cell stage resolved...
    /// </summary>
    public void Debug(string message)
    {
        if (!IsDebugEnabled)
            return;

        Console.WriteLine($"{Prefix} {message}");
        WriteToFile("DEBUG", message);
    }

    /// <summary>
    /// Real debug / verbose debug.
    /// Requires BOTH IsDebugEnabled and IsRealDebugEnabled.
    /// </summary>
    public void RealDebug(string message)
    {
        if (!IsDebugEnabled || !IsRealDebugEnabled)
            return;

        Console.WriteLine($"{Prefix} {message}");
        WriteToFile("REAL DEBUG", message);
    }

    // Tony-style aliases
    public void Log(string message)
    {
        Info(message);
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