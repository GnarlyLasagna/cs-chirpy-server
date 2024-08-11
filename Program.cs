var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ApiConfig>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

var config = app.Services.GetRequiredService<ApiConfig>();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseDefaultFiles();
app.UseStaticFiles();

// Move the middleware after static files but before route mappings
app.UseMiddleware<RequestCounterMiddleware>(config);

app.UseFileServer(new FileServerOptions
{
     DefaultFilesOptions = { DefaultFileNames = new List<string> { "index.html" } }
});

app.MapGet("/app", FsHandler);

app.MapGet("/app/assets", AssetsHandler);

app.MapGet("/api/reset", ResetHandler);

app.MapGet("/api/metrics", MetricsHandler);

app.MapGet("/api/healthz", WriteOkResponse);

app.Run();



// Handler Functions //
async Task FsHandler(HttpContext context)
{
    context.Response.ContentType = "text/html; charset=utf-8";
    await context.Response.SendFileAsync(Path.Combine("wwwroot", "index.html"));
}

async Task WriteOkResponse(HttpContext context)
{
    context.Response.Headers.Append("Content-Type", "text/plain; charset=utf-8");
    context.Response.StatusCode = StatusCodes.Status200OK;
    await context.Response.WriteAsync("OK");
}

async Task MetricsHandler(HttpContext context)
{
    context.Response.ContentType = "text/plain; charset=utf-8";
    await context.Response.WriteAsync($"Hits: {config.FileServerHits}");
}

async Task ResetHandler(HttpContext context)
{
    config.FileServerHits = 0;
    context.Response.StatusCode = StatusCodes.Status200OK;
    await context.Response.WriteAsync("File server hits counter reset.");
}

async Task AssetsHandler(HttpContext context)
{
        var assetsDir = Path.Combine("wwwroot", "assets");

    if (!Directory.Exists(assetsDir))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        await context.Response.WriteAsync("Directory not found");
        return;
    }

    context.Response.ContentType = "text/html; charset=utf-8";

    var files = Directory.GetFiles(assetsDir)
                         .Select(Path.GetFileName);

    var html = "<pre>\n";
    foreach (var file in files)
    {
        html += $"<a href=\"{file}\">{file}</a>\n";
    }
    html += "</pre>";

    await context.Response.WriteAsync(html);
}


