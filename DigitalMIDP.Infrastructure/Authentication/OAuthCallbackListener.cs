using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;

namespace DigitalMIDP.Infrastructure.Authentication;

/// <summary>
/// Handles OAuth 2.0 callback by listening on a local HTTP endpoint.
/// Used for capturing the authorization code in the 3-legged OAuth flow.
/// </summary>
internal sealed class OAuthCallbackListener : IDisposable
{
    private readonly HttpListener _listener;
    private readonly ILogger<OAuthCallbackListener> _logger;
    private readonly string _redirectUri;
    private bool _disposed;

    public OAuthCallbackListener(string redirectUri, ILogger<OAuthCallbackListener> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(redirectUri);
        ArgumentNullException.ThrowIfNull(logger);

        _redirectUri = redirectUri;
        _logger = logger;
        _listener = new HttpListener();
    }

    /// <summary>
    /// Starts listening for the OAuth callback and waits for the authorization code.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>The authorization code from the callback.</returns>
    /// <exception cref="InvalidOperationException">Thrown when callback fails or no code is received.</exception>
    public async Task<string> ListenForCallbackAsync(CancellationToken cancellationToken)
    {
        try
        {
            _listener.Prefixes.Add(_redirectUri.EndsWith('/') ? _redirectUri : $"{_redirectUri}/");
            _listener.Start();
            _logger.LogInformation("OAuth callback listener started on {RedirectUri}", _redirectUri);

            using var registration = cancellationToken.Register(() => _listener.Stop());

            var context = await _listener.GetContextAsync();
            var request = context.Request;
            var response = context.Response;

            // Extract authorization code or error from query string
            var queryParams = request.QueryString;
            var code = queryParams["code"];
            var error = queryParams["error"];
            var errorDescription = queryParams["error_description"];

            // Send response to browser
            await SendBrowserResponseAsync(response, code != null, errorDescription);

            if (!string.IsNullOrWhiteSpace(error))
            {
                _logger.LogError("OAuth callback received error: {Error} - {Description}", error, errorDescription);
                throw new InvalidOperationException($"OAuth authentication failed: {error} - {errorDescription}");
            }

            if (string.IsNullOrWhiteSpace(code))
            {
                _logger.LogError("OAuth callback did not contain authorization code");
                throw new InvalidOperationException("No authorization code received from OAuth callback");
            }

            _logger.LogInformation("Authorization code received successfully");
            return code;
        }
        catch (HttpListenerException ex) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("OAuth callback listener cancelled");
            throw new OperationCanceledException("OAuth callback was cancelled", ex, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to listen for OAuth callback");
            throw;
        }
        finally
        {
            Stop();
        }
    }

    /// <summary>
    /// Sends a response page to the browser after OAuth callback.
    /// </summary>
    private async Task SendBrowserResponseAsync(HttpListenerResponse response, bool success, string? errorDescription)
    {
        string htmlContent;
        
        if (success)
        {
            htmlContent = @"<!DOCTYPE html>
<html>
<head>
    <title>Authentication Successful</title>
    <style>
        body { font-family: Arial, sans-serif; text-align: center; padding: 50px; background: #f0f0f0; }
        .container { background: white; padding: 30px; border-radius: 10px; box-shadow: 0 2px 10px rgba(0,0,0,0.1); max-width: 500px; margin: 0 auto; }
        h1 { color: #4CAF50; }
        p { color: #666; }
    </style>
</head>
<body>
    <div class='container'>
        <h1>Authentication Successful</h1>
        <p>You have successfully authenticated with Autodesk Platform Services.</p>
        <p>You can close this window and return to the application.</p>
    </div>
</body>
</html>";
        }
        else
        {
            htmlContent = $@"<!DOCTYPE html>
<html>
<head>
    <title>Authentication Failed</title>
    <style>
        body {{ font-family: Arial, sans-serif; text-align: center; padding: 50px; background: #f0f0f0; }}
        .container {{ background: white; padding: 30px; border-radius: 10px; box-shadow: 0 2px 10px rgba(0,0,0,0.1); max-width: 500px; margin: 0 auto; }}
        h1 {{ color: #f44336; }}
        p {{ color: #666; }}
    </style>
</head>
<body>
    <div class='container'>
        <h1>? Authentication Failed</h1>
        <p>Authentication with Autodesk Platform Services failed.</p>
        <p>{errorDescription ?? "Please try again."}</p>
        <p>You can close this window and return to the application.</p>
    </div>
</body>
</html>";
        }

        var buffer = Encoding.UTF8.GetBytes(htmlContent);
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = buffer.Length;
        response.StatusCode = success ? 200 : 400;

        await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        response.OutputStream.Close();
    }

    /// <summary>
    /// Stops the HTTP listener.
    /// </summary>
    public void Stop()
    {
        if (_listener.IsListening)
        {
            _listener.Stop();
            _logger.LogInformation("OAuth callback listener stopped");
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Stop();
        _listener.Close();
        _disposed = true;
    }
}
