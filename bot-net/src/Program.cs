using Bot;
using Bot.Services;
using Bot.Utils;
using Microsoft.AspNetCore.Mvc;

if (args.Length > 0 && args[0] == "auth")
    return await ConsoleAuth.RunAsync();

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<BotHolder>();
builder.Services.AddHostedService<Worker>();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

// Keep Kestrel on a fixed internal port
builder.WebHost.UseUrls("http://0.0.0.0:8080");

var app = builder.Build();

app.UseCors();

// Health check
app.MapGet("/", () => Results.Ok(new { status = "ok" }));

app.MapGet("/version", () => Results.Ok(new { version = AppVersion.Current }));

// Trigger preview of a single movie collection (info card + files)
app.MapPost("/preview/collection/{collectionId:int}", async (int collectionId, IServiceProvider sp) =>
{
    var preview = ActivatorUtilities.CreateInstance<PreviewService>(sp);
    var (ok, error) = await preview.SendCollectionPreviewAsync(collectionId);
    return ok ? Results.Ok(new { status = "ok" }) : Results.Problem(error);
});

// Trigger preview of all files in a season
app.MapPost("/preview/series/{seriesId:int}/season/{seasonNumber:int}", async (int seriesId, int seasonNumber, IServiceProvider sp) =>
{
    var preview = ActivatorUtilities.CreateInstance<PreviewService>(sp);
    var (ok, error) = await preview.SendSeasonPreviewAsync(seriesId, seasonNumber);
    return ok ? Results.Ok(new { status = "ok" }) : Results.Problem(error);
});

// List the video files sitting in the mounted library directories
app.MapGet("/local/files", (
    string? q,
    [FromQuery(Name = "tmdb_id")] int? tmdbId,
    [FromQuery(Name = "tvdb_id")] int? tvdbId,
    IServiceProvider sp) =>
{
    var localMedia = ActivatorUtilities.CreateInstance<LocalMediaService>(sp);
    return Results.Ok(localMedia.ListFiles(q, tmdbId, tvdbId));
});

// Read a local file with ffprobe and store it as a collection's technical metadata
app.MapPost("/probe/collection/{collectionId:int}", async (int collectionId, ProbeRequest request, IServiceProvider sp) =>
{
    var localMedia = ActivatorUtilities.CreateInstance<LocalMediaService>(sp);
    var (ok, error) = await localMedia.ProbeIntoCollectionAsync(collectionId, request.Path);
    return ok ? Results.Ok(new { status = "ok" }) : Results.Problem(error);
});

// Delete a collection's downloaded file, keeping the collection itself
app.MapDelete("/local/collection/{collectionId:int}", async (int collectionId, IServiceProvider sp) =>
{
    var localMedia = ActivatorUtilities.CreateInstance<LocalMediaService>(sp);
    var (ok, error) = await localMedia.DeleteLocalCopyAsync(collectionId);
    return ok ? Results.Ok(new { status = "ok" }) : Results.Problem(error);
});

await app.RunAsync();
return 0;

record ProbeRequest(string Path);
