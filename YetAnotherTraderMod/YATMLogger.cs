using SPTarkov.DI.Annotations;
using System;

namespace YATMMedved;

[Injectable]
public class YATMLogger
{
    private const string Prefix = "[YATM Medved]";

    public void Info(string message) => Console.WriteLine($"{Prefix} {message}");
    public void Warning(string message) => Console.WriteLine($"{Prefix} WARNING: {message}");
    public void Error(string message) => Console.WriteLine($"{Prefix} ERROR: {message}");
}
