using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Client;
using Xunit;

namespace McpCarbonServer.Tests;

/// <summary>
/// Starts the built server in HTTP mode as a child process and connects an MCP client to it
/// over Streamable HTTP.
/// </summary>
/// <remarks>
/// Same reasoning as the stdio fixture: drive the shipped executable rather than the tool
/// methods, so the transport, the routing and the serialisation are all in the path under
/// test. A port is chosen by binding to zero and releasing, which races in theory and is
/// stable in practice; hard-coding one would collide with whatever else the machine is
/// running.
/// </remarks>
public sealed class HttpServerFixture : IAsyncLifetime
{
    private Process? _process;
    private HttpClient? _http;
    private McpClient? _client;

    /// <summary>The connected client. Valid once <see cref="InitializeAsync"/> has run.</summary>
    public McpClient Client =>
        _client ?? throw new InvalidOperationException("The fixture has not been initialised.");

    /// <summary>The base address the server is listening on.</summary>
    public Uri BaseAddress { get; private set; } = null!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        int port = FindFreePort();
        BaseAddress = new Uri($"http://127.0.0.1:{port}");

        ProcessStartInfo startInfo = new("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        startInfo.ArgumentList.Add(McpServerFixture.ResolveServerAssemblyPath());
        startInfo.ArgumentList.Add("--http");
        startInfo.Environment["ASPNETCORE_URLS"] = BaseAddress.ToString().TrimEnd('/');

        _process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The server process did not start.");

        _http = new HttpClient { BaseAddress = BaseAddress };

        await WaitForHealthAsync();

        HttpClientTransport transport = new(new HttpClientTransportOptions
        {
            Name = "mcp-carbon-server (http)",
            Endpoint = new Uri(BaseAddress, "/mcp"),
        });

        _client = await McpClient.CreateAsync(
            transport,
            cancellationToken: TestContext.Current.CancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
        }

        _http?.Dispose();

        if (_process is { HasExited: false })
        {
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync();
        }

        _process?.Dispose();
    }

    /// <summary>Issues a plain HTTP request, for the endpoints that are not MCP.</summary>
    public Task<HttpResponseMessage> GetAsync(string path) =>
        (_http ?? throw new InvalidOperationException("The fixture has not been initialised."))
            .GetAsync(path, TestContext.Current.CancellationToken);

    private async Task WaitForHealthAsync()
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            try
            {
                using HttpResponseMessage response = await _http!.GetAsync(
                    "/health",
                    TestContext.Current.CancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // Kestrel is not listening yet.
            }

            if (_process is { HasExited: true })
            {
                string error = await _process.StandardError.ReadToEndAsync();
                throw new InvalidOperationException($"The server exited before it was ready. stderr:\n{error}");
            }

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        throw new InvalidOperationException($"The server did not answer /health at {BaseAddress} within 10 seconds.");
    }

    private static int FindFreePort()
    {
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();

        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }
}
