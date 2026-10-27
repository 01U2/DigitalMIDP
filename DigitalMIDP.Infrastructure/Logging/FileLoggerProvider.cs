using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace DigitalMIDP.Infrastructure.Logging;

/// <summary>
/// Writes log entries to a daily file on the user's machine
/// (by default <c>%LocalAppData%\DigitalMIDP\Logs\DigitalMIDP-yyyyMMdd.log</c>).
///
/// <para>
/// Before this, the application only logged to the Debug and Console providers. It is a WinExe, so
/// on a user's machine neither went anywhere and a support case started with nothing to look at.
/// </para>
///
/// <para>
/// Deliberately small and dependency-free rather than a logging framework: one lock, one appending
/// writer, roll-over at midnight, and old files purged on startup. Volume is a desktop session's
/// worth of Information-level lines, so contention is not a concern. Writing a log line must never
/// take the application down, so any I/O failure disables the sink instead of throwing.
/// </para>
///
/// <para>
/// Every line passes through <see cref="Redact"/> before it reaches disk. Nothing in the application
/// is meant to log a token, but the file outlives the session and may be emailed to support, so a
/// bearer token or OAuth secret that slipped into an exception message is masked as a last line of
/// defence.
/// </para>
/// </summary>
public sealed partial class FileLoggerProvider : ILoggerProvider
{
    public const int DefaultRetentionDays = 14;

    private const string FilePrefix = "DigitalMIDP-";
    private const string FileExtension = ".log";

    private readonly string _directory;
    private readonly LogLevel _minimumLevel;
    private readonly object _gate = new();
    private StreamWriter? _writer;
    private DateTime _currentDate;
    private bool _disabled;

    public FileLoggerProvider(
        string directory,
        LogLevel minimumLevel = LogLevel.Information,
        int retentionDays = DefaultRetentionDays)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        _directory = directory;
        _minimumLevel = minimumLevel;

        try
        {
            Directory.CreateDirectory(_directory);
            PurgeOldFiles(retentionDays);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Disable(ex);
        }
    }

    /// <summary>The default per-user log folder: <c>%LocalAppData%\DigitalMIDP\Logs</c>.</summary>
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DigitalMIDP", "Logs");

    /// <summary>The file today's entries go to.</summary>
    public string CurrentFilePath => GetFilePath(DateTime.Now.Date);

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose()
    {
        lock (_gate)
        {
            _disabled = true;
            _writer?.Dispose();
            _writer = null;
        }
    }

    internal bool IsEnabled(LogLevel logLevel) =>
        logLevel != LogLevel.None && logLevel >= _minimumLevel && !_disabled;

    internal void Write(LogLevel logLevel, string category, string message, Exception? exception)
    {
        var now = DateTimeOffset.Now;

        var line = new StringBuilder(128 + message.Length)
            .Append(now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"))
            .Append(" [").Append(LevelCode(logLevel)).Append("] ")
            .Append("[T").Append(Environment.CurrentManagedThreadId).Append("] ")
            .Append(category).Append(": ")
            .Append(message);

        if (exception is not null)
        {
            line.AppendLine().Append(exception);
        }

        line.AppendLine();

        var text = Redact(line.ToString());

        lock (_gate)
        {
            if (_disabled)
            {
                return;
            }

            try
            {
                EnsureWriter(now.Date);
                _writer!.Write(text);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
            {
                Disable(ex);
            }
        }
    }

    /// <summary>
    /// Masks anything that looks like a credential: the OAuth bearer header and token-endpoint
    /// fields, and the signature parts of a pre-signed S3 URL - both as query parameters and as the
    /// elements S3 echoes back in an error body (an upload failure is logged with that body).
    /// </summary>
    internal static string Redact(string text) =>
        SecretPattern().Replace(text, match => match.Groups["prefix"].Value + "***" + match.Groups["suffix"].Value);

    [GeneratedRegex(
        @"(?<prefix>\bBearer\s+)[A-Za-z0-9\-._~+/]+=*" +
        @"|(?<prefix>\b(?:access_token|refresh_token|id_token|client_secret|code_verifier)[""']?\s*[:=]\s*[""']?)[^""'&\s,}]+" +
        @"|(?<prefix>[?&]code=)[^&\s""']+" +
        @"|(?<prefix>\bX-Amz-(?:Signature|Credential|Security-Token)=)[^&\s""'<]+" +
        @"|(?<prefix><(?<tag>SignatureProvided|StringToSign|CanonicalRequest|AWSAccessKeyId|StringToSignBytes|CanonicalRequestBytes)>)[\s\S]*?(?<suffix></\k<tag>>)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SecretPattern();

    private void EnsureWriter(DateTime date)
    {
        if (_writer is not null && date == _currentDate)
        {
            return;
        }

        _writer?.Dispose();

        // FileShare.ReadWrite so the file can be opened in a text editor (or by a second instance of
        // the application) while this session is still writing to it.
        var stream = new FileStream(
            GetFilePath(date), FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);

        // AutoFlush so the lines leading up to a crash are on disk, which is the case this exists for.
        _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };
        _currentDate = date;
    }

    private string GetFilePath(DateTime date) =>
        Path.Combine(_directory, $"{FilePrefix}{date:yyyyMMdd}{FileExtension}");

    private void PurgeOldFiles(int retentionDays)
    {
        if (retentionDays <= 0)
        {
            return;
        }

        var cutoff = DateTime.Now.Date.AddDays(-retentionDays);

        foreach (var file in Directory.EnumerateFiles(_directory, $"{FilePrefix}*{FileExtension}"))
        {
            try
            {
                if (File.GetLastWriteTime(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A file still open elsewhere is simply left for the next startup.
            }
        }
    }

    private void Disable(Exception ex)
    {
        _disabled = true;
        Debug.WriteLine($"[WARN] File logging disabled ({_directory}): {ex.Message}");
    }

    private static string LevelCode(LogLevel logLevel) => logLevel switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "???"
    };

    private sealed class FileLogger : ILogger
    {
        private readonly FileLoggerProvider _provider;
        private readonly string _category;

        public FileLogger(FileLoggerProvider provider, string category)
        {
            _provider = provider;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => _provider.IsEnabled(logLevel);

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            ArgumentNullException.ThrowIfNull(formatter);

            _provider.Write(logLevel, _category, formatter(state, exception), exception);
        }
    }
}
