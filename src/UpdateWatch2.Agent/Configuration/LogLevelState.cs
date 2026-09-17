namespace UpdateWatch2.Agent.Configuration;

public class LogLevelState : ILogLevelState
{
    private volatile int _current;

    public LogLevelState(string initialRawLevel)
    {
        _current = (int)Map(initialRawLevel);
    }

    public LogLevel Current => (LogLevel)_current;

    public void Update(string rawLevel) => _current = (int)Map(rawLevel);

    // Same mapping Program.cs's own MapLogLevel used before this replaced
    // it — kept here so both the initial value and every later push go
    // through the identical rule.
    private static LogLevel Map(string value) => value.Trim().ToUpperInvariant() switch
    {
        "DEBUG" => LogLevel.Debug,
        "INFO" => LogLevel.Information,
        "WARNING" => LogLevel.Warning,
        "ERROR" => LogLevel.Error,
        _ => LogLevel.Information,
    };
}
