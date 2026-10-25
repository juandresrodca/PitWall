using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using PitWall.Core.Json;
using PitWall.Server.Archive;
using PitWall.Server.Auth;
using PitWall.Server.Feed;
using PitWall.Server.Services;

namespace PitWall.Server;

/// <summary>The embedded web server behind the dashboard. Hosted in-process by PitWall.exe.</summary>
public sealed class PitWallServer : IAsyncDisposable
{
    /// <summary>Header every state-changing request must carry. Cross-site pages cannot add it without CORS.</summary>
    public const string IntentHeader = "X-PitWall";

    private readonly WebApplication _app;

    private PitWallServer(WebApplication app, string url)
    {
        _app = app;
        Url = url;
    }

    /// <summary>Loopback URL of the dashboard, e.g. <c>http://127.0.0.1:5320/</c>.</summary>
    public string Url { get; }

    public IServiceProvider Services => _app.Services;

    public TokenStore Tokens => _app.Services.GetRequiredService<TokenStore>();

    public UpdateService Updates => _app.Services.GetRequiredService<UpdateService>();

    public static async Task<PitWallServer> StartAsync(PitWallOptions options, Action<ILoggingBuilder>? logging = null, CancellationToken cancellation = default)
    {
        Directory.CreateDirectory(options.DataDirectory);
        var port = PickPort(options.Port);

        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory,
            EnvironmentName = Environments.Production,
        });
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(o => o.SingleLine = true);
        builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
        builder.Logging.AddFilter("Microsoft.AspNetCore.Http.Connections.Client", LogLevel.Warning);
        builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
        logging?.Invoke(builder.Logging);

        builder.WebHost.ConfigureKestrel(k =>
        {
            if (options.AllowLan)
            {
                k.ListenAnyIP(port);
            }
            else
            {
                k.Listen(IPAddress.Loopback, port);
            }
        });

        builder.Services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            o.SerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
        });

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(_ => CreateHttpClient());
        builder.Services.AddSingleton(_ => new TokenStore(options.DataDirectory));
        builder.Services.AddSingleton<FeedHub>();
        builder.Services.AddSingleton<ArchiveClient>();
        builder.Services.AddSingleton<LiveTimingSource>();
        builder.Services.AddSingleton<ReplaySource>();
        builder.Services.AddSingleton<SourceManager>();
        builder.Services.AddSingleton<TrackMapService>();
        builder.Services.AddSingleton<ScheduleService>();
        builder.Services.AddSingleton<NewsService>();
        builder.Services.AddSingleton<ForecastService>();
        builder.Services.AddSingleton<UpdateService>();

        var app = builder.Build();

        UseDashboardFiles(app, options);
        MapApi(app);

        var hub = app.Services.GetRequiredService<FeedHub>();
        var tracks = app.Services.GetRequiredService<TrackMapService>();
        hub.SessionChanged += path => tracks.Prefetch(path, CurrentCircuit(hub, path));

        // Report sign-in changes straight away, whichever source is running. The live source
        // follows up with Connected = true once the feed has accepted the token.
        var tokens = app.Services.GetRequiredService<TokenStore>();
        hub.UpdateStatus(s => s with { Auth = AuthStatus.Describe(tokens.Current, usingToken: false) });
        tokens.Changed += token => hub.UpdateStatus(s => s with { Auth = AuthStatus.Describe(token, usingToken: false) });

        await app.StartAsync(cancellation);

        var updates = app.Services.GetRequiredService<UpdateService>();
        _ = Task.Run(() => updates.CheckAsync(force: false, CancellationToken.None));

        var sources = app.Services.GetRequiredService<SourceManager>();
        if (options.ReplayPath is { } replayPath)
        {
            _ = Task.Run(() => sources.UseReplayAsync(replayPath, null, CancellationToken.None));
        }
        else
        {
            await sources.UseLiveAsync();
        }

        return new PitWallServer(app, $"http://127.0.0.1:{port}/");
    }

    private static void MapApi(WebApplication app)
    {
        // Refuse state-changing requests that did not come from the dashboard itself.
        app.Use(async (context, next) =>
        {
            var method = context.Request.Method;
            if (context.Request.Path.StartsWithSegments("/api")
                && !HttpMethods.IsGet(method) && !HttpMethods.IsHead(method)
                && !context.Request.Headers.ContainsKey(IntentHeader))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            await next();
        });

        var api = app.MapGroup("/api");

        api.MapGet("/stream", (FeedHub hub, HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-cache";
            var reader = hub.Subscribe(context.RequestAborted);
            return TypedResults.ServerSentEvents(reader.ReadAllAsync(context.RequestAborted));
        });

        api.MapGet("/status", (FeedHub hub) => TypedResults.Json(hub.Status, JsonSerializerOptions.Web));

        api.MapGet("/schedule", async (ScheduleService schedule, CancellationToken ct) =>
        {
            var season = await schedule.GetSeasonAsync(ct);
            return season is null ? Results.StatusCode(StatusCodes.Status503ServiceUnavailable) : Results.Ok(season);
        });

        api.MapGet("/standings", async (ScheduleService schedule, CancellationToken ct) =>
        {
            var standings = await schedule.GetStandingsAsync(ct);
            return standings is null ? Results.StatusCode(StatusCodes.Status503ServiceUnavailable) : Results.Ok(standings);
        });

        api.MapGet("/news", async (NewsService news, CancellationToken ct) => Results.Ok(await news.GetAsync(ct)));

        api.MapGet("/forecast", async (ForecastService forecast, CancellationToken ct) =>
        {
            var body = await forecast.GetForCurrentWeekendAsync(ct);
            return body is null ? Results.NoContent() : Results.Text(body.ToJsonString(), "application/json");
        });

        // 200 the outline · 202 still being built (poll again) · 204 nothing to build it from.
        api.MapGet("/track", (string? path, FeedHub hub, TrackMapService tracks) =>
        {
            path ??= hub.Status.SessionPath;
            if (path is null || !SourceManager.SessionPathPattern().IsMatch(path))
            {
                return Results.NoContent();
            }
            var map = tracks.TryGet(path, CurrentCircuit(hub, path), out var building);
            return map is not null ? Results.Ok(map)
                : building ? Results.StatusCode(StatusCodes.Status202Accepted)
                : Results.NoContent();
        });

        api.MapGet("/archive/{year:int}", async (int year, ArchiveClient archive, CancellationToken ct) =>
        {
            var sessions = await archive.GetSeasonAsync(year, ct);
            var meetings = sessions
                .Where(s => s.Path is not null && s.EndUtc < DateTimeOffset.UtcNow)
                .GroupBy(s => s.MeetingKey)
                .Select(g => new
                {
                    key = g.Key,
                    name = g.First().MeetingName,
                    location = g.First().Location,
                    country = g.First().CountryCode,
                    sessions = g.OrderBy(s => s.StartUtc).Select(s => new { s.Key, s.Name, s.Type, s.StartUtc, s.Path }),
                })
                .OrderByDescending(m => m.sessions.First().StartUtc);
            return Results.Ok(meetings);
        });

        api.MapPost("/source/live", async (SourceManager sources) =>
        {
            await sources.UseLiveAsync();
            return Results.Accepted();
        });

        api.MapPost("/source/replay", (ReplayRequest request, SourceManager sources, FeedHub hub, ILogger<PitWallServer> logger) =>
        {
            if (request.Path is null || !SourceManager.SessionPathPattern().IsMatch(request.Path))
            {
                return Results.BadRequest(new { error = "Unknown session path." });
            }

            // Downloading a session can take a while; progress arrives over the event stream.
            _ = Task.Run(async () =>
            {
                try
                {
                    var start = request.StartAt is { } s ? TimeSpan.FromSeconds(s) : (TimeSpan?)null;
                    await sources.UseReplayAsync(request.Path, start, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Replay of {Path} failed", request.Path);
                    hub.UpdateStatus(st => st with { Connection = FeedConnection.Error, Message = "Could not load that session." });
                }
            });
            return Results.Accepted();
        });

        api.MapPost("/replay", (ReplayCommand command, SourceManager sources) =>
        {
            var replay = sources.Replay;
            if (replay.Timeline is null)
            {
                return Results.Conflict(new { error = "No replay is loaded." });
            }
            switch (command.Action)
            {
                case "play": replay.Play(); break;
                case "pause": replay.Pause(); break;
                case "seek" when command.Value is { } seconds: replay.Seek(TimeSpan.FromSeconds(seconds)); break;
                case "speed" when command.Value is { } speed: replay.SetSpeed(speed); break;
                default: return Results.BadRequest(new { error = "Unknown action." });
            }
            return Results.NoContent();
        });

        // Manual sign-in for headless or browser-only setups: the user pastes their own
        // login-session cookie value (or the bare token). It is sealed, never echoed back.
        api.MapPost("/auth", (TokenRequest request, TokenStore tokens) =>
        {
            var token = tokens.Save(request.Token ?? "");
            return token is null
                ? Results.BadRequest(new { error = "That is not a readable F1 TV token or login-session cookie." })
                : Results.Ok(AuthStatus.Describe(token, usingToken: false));
        });

        api.MapGet("/update", async (UpdateService updates, bool? check, CancellationToken ct) =>
            Results.Ok(check == true ? await updates.CheckAsync(force: true, ct) : updates.Status));

        // Downloads and verifies the release; installing it is a separate, deliberate step.
        api.MapPost("/update/download", async (UpdateService updates, CancellationToken ct) =>
            Results.Ok(await updates.DownloadAsync(ct)));

        api.MapDelete("/auth", (TokenStore tokens) =>
        {
            tokens.Clear();
            return Results.NoContent();
        });
    }

    /// <summary>Circuit key from the feed's SessionInfo, if <paramref name="path"/> is the session being shown.</summary>
    private static int CurrentCircuit(FeedHub hub, string? path) => hub.Read(state =>
        path is not null && state.SessionPath == path
            ? state.Topic(Core.LiveTiming.Topics.SessionInfo)?["Meeting"]?["Circuit"]?["Key"].AsInt() ?? 0
            : 0);

    private static void UseDashboardFiles(WebApplication app, PitWallOptions options)
    {
        var root = options.WebRoot ?? Environment.GetEnvironmentVariable("PITWALL_WEBROOT");
        IFileProvider files;
        if (root is not null && Directory.Exists(root))
        {
            files = new PhysicalFileProvider(Path.GetFullPath(root));
        }
        else
        {
            try
            {
                files = new ManifestEmbeddedFileProvider(typeof(PitWallServer).Assembly, "wwwroot");
            }
            catch (Exception)
            {
                // No embedded build (web/ not built yet): serve the placeholder below.
                files = new NullFileProvider();
            }
        }

        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = files,
            OnPrepareResponse = ctx =>
            {
                // Astro fingerprints everything under /_astro/, so it can be cached for good.
                ctx.Context.Response.Headers[HeaderNames.CacheControl] =
                    ctx.Context.Request.Path.StartsWithSegments("/_astro") ? "public, max-age=31536000, immutable" : "no-cache";
            },
        });

        if (!files.GetFileInfo("index.html").Exists)
        {
            app.MapGet("/", () => Results.Content(
                "<h1>PitWall</h1><p>The dashboard has not been built. Run <code>npm run build</code> in <code>web/</code>, then rebuild.</p>",
                "text/html"));
        }
    }

    /// <summary>The HTTP client every PitWall request goes through, identity and all.</summary>
    public static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        };
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(2) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(HttpDefaults.UserAgent);
        return http;
    }

    private static int PickPort(int preferred)
    {
        if (preferred == 0)
        {
            return FreePort();
        }
        for (var port = preferred; port < preferred + 20; port++)
        {
            try
            {
                using var probe = new TcpListener(IPAddress.Loopback, port);
                probe.Start();
                return port;
            }
            catch (SocketException)
            {
            }
        }
        return FreePort();
    }

    private static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }

    public async Task StopAsync()
    {
        await _app.Services.GetRequiredService<SourceManager>().DisposeAsync();
        await _app.StopAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        await _app.DisposeAsync();
    }

    public sealed record ReplayRequest(string? Path, double? StartAt);

    public sealed record ReplayCommand(string Action, double? Value);

    public sealed record TokenRequest(string? Token);
}
