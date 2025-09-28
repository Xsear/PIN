using System.Collections.Generic;
using Serilog.Core;
using Serilog.Events;

public class FirefallLogLevelEnricher : ILogEventEnricher
{
    private static readonly Dictionary<LogEventLevel, string> CustomLevelMap = new()
    {
        [LogEventLevel.Verbose] = "TRACE",
        [LogEventLevel.Debug] = "DEBUG",
        [LogEventLevel.Information] = "INFO ",
        [LogEventLevel.Warning] = "WARN ",
        [LogEventLevel.Error] = "ERROR",
        [LogEventLevel.Fatal] = "FATAL"
    };

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var level = CustomLevelMap.TryGetValue(logEvent.Level, out var value)
            ? value
            : logEvent.Level.ToString().ToUpperInvariant();

        var property = propertyFactory.CreateProperty("FirefallLogLevel", level);
        logEvent.AddPropertyIfAbsent(property);
    }
}
