using EFT;

namespace LootingBots.Utilities;

[Flags]
public enum LogLevel
{
    /// <summary>
    ///     No level selected.
    /// </summary>
    None = 0,

    /// <summary>
    ///     An error has occurred, but can be recovered from.
    /// </summary>
    Error = 2,

    /// <summary>
    ///     A warning has been produced, but does not necessarily mean that something wrong has happened.
    /// </summary>
    Warning = 4,

    /// <summary>
    ///     A message of low importance.
    /// </summary>
    Info = 16,

    /// <summary>
    ///     A message that would likely only interest a developer.
    /// </summary>
    Debug = 32,

    /// <summary>
    ///     All log levels.
    /// </summary>
    All = Error | Warning | Info | Debug,
}

public class BotLog
{
    private readonly Log _log;
    private readonly BotOwner _botOwner;
    private readonly string _botString;

    private string _currentBotFilter
    {
        get { return LootingBots.FilterLogsOnBot.Value.ToString(); }
    }

    private bool _isLogShown
    {
        get { return _currentBotFilter == "0" || _botOwner.name.Equals("Bot" + _currentBotFilter); }
    }

    public bool DebugEnabled
    {
        get { return _log.DebugEnabled; }
    }
    public bool WarningEnabled
    {
        get { return _log.WarningEnabled; }
    }
    public bool InfoEnabled
    {
        get { return _log.InfoEnabled; }
    }
    public bool ErrorEnabled
    {
        get { return _log.ErrorEnabled; }
    }

    public BotLog(Log log, BotOwner botOwner)
    {
        _log = log;
        _botOwner = botOwner;
        _botString = $"([{_botOwner.Profile.Info.Settings.Role}] {_botOwner.Name()})";
    }

    public void LogDebug(object msg)
    {
        if (_isLogShown)
        {
            _log.LogDebug(FormatMessage(msg));
        }
    }

    public void LogInfo(object msg)
    {
        if (_isLogShown)
        {
            _log.LogInfo(FormatMessage(msg));
        }
    }

    public void LogWarning(object msg)
    {
        if (_isLogShown)
        {
            _log.LogWarning(FormatMessage(msg));
        }
    }

    public void LogError(object msg)
    {
        if (_isLogShown)
        {
            _log.LogError(FormatMessage(msg));
        }
    }

    private string FormatMessage(object data)
    {
        return $"{_botString} {data}";
    }
}

public class Log(BepInEx.Logging.ManualLogSource logger, BepInEx.Configuration.ConfigEntry<LogLevel> logLevels)
{
    public bool DebugEnabled
    {
        get { return logLevels.Value.HasDebug(); }
    }
    public bool WarningEnabled
    {
        get { return logLevels.Value.HasWarning(); }
    }
    public bool InfoEnabled
    {
        get { return logLevels.Value.HasInfo(); }
    }
    public bool ErrorEnabled
    {
        get { return logLevels.Value.HasError(); }
    }

    public void LogDebug(object data)
    {
        logger.LogDebug(data);
    }

    public void LogInfo(object data)
    {
        logger.LogInfo(data);
    }

    public void LogWarning(object data)
    {
        logger.LogWarning(data);
    }

    public void LogError(object data)
    {
        logger.LogError(data);
    }
}

public static class LogUtils
{
    public static bool HasError(this LogLevel logLevel)
    {
        return (logLevel & LogLevel.Error) != 0;
    }

    public static bool HasWarning(this LogLevel logLevel)
    {
        return (logLevel & LogLevel.Warning) != 0;
    }

    public static bool HasInfo(this LogLevel logLevel)
    {
        return (logLevel & LogLevel.Info) != 0;
    }

    public static bool HasDebug(this LogLevel logLevel)
    {
        return (logLevel & LogLevel.Debug) != 0;
    }

    public static string Name(this BotOwner botOwner)
    {
        return $"[{botOwner.name}] {botOwner.Profile.GetCorrectedNickname()}";
    }
}
