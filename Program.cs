using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ApiConfig>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

var config = app.Services.GetRequiredService<ApiConfig>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseMiddleware<RequestCounterMiddleware>(config);

app.UseFileServer(new FileServerOptions
{
    DefaultFilesOptions = { DefaultFileNames = new List<string> { "index.html" } }
});

app.MapGet("/app", FsHandler);
app.MapGet("/app/assets", AssetsHandler);
app.MapGet("/api/reset", ResetHandler);
app.MapGet("/admin/metrics", MetricsHandler);
app.MapGet("/api/healthz", WriteOkResponse);

app.MapPost("/api/validate_chirp", async (HttpContext context) =>
{
    try
    {
        // Read the request body as a string
        var bodyString = await new StreamReader(context.Request.Body).ReadToEndAsync();
        Console.WriteLine("Raw Body: " + bodyString);

        // Deserialize the body string into ChirpRequest using System.Text.Json.JsonSerializer
        var chirpRequest = System.Text.Json.JsonSerializer.Deserialize<ChirpRequest>(bodyString);

        Console.WriteLine("Deserialized ChirpRequest: " + (chirpRequest?.Body ?? "null"));

        if (chirpRequest == null || string.IsNullOrEmpty(chirpRequest.Body))
        {
            Console.WriteLine("chirp request is == null or chirp body is empty");
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid chirp data" });
            return;
        }

        if (chirpRequest.Body.Length > 140)
        {
            Console.WriteLine("chirp length is over 140 ");
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = "Chirp is too long" });
            return;
        }

        var cleanedBody = CleanProfanity(chirpRequest.Body);

        context.Response.StatusCode = StatusCodes.Status200OK;
        await context.Response.WriteAsJsonAsync(new { cleaned_body = cleanedBody });
    }
    catch (Exception ex)
    {
        // Log exception message
        Console.WriteLine($"Exception: {ex}");

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new { error = "Something went wrong" });
    }
});

app.Run();

string CleanProfanity(string input)
{
    var profaneWords = new List<string> { "kerfuffle", "sharbert", "fornax" };

    foreach (var word in profaneWords)
    {
        var regex = new Regex($@"\b{word}\b", RegexOptions.IgnoreCase);
        input = regex.Replace(input, "****");
    }

    return input;
}

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
    context.Response.ContentType = "text/html; charset=utf-8";

    var htmlContent = $@"
    <html>
    <body>
        <h1>Welcome, Chirpy Admin</h1>
        <p>Chirpy has been visited {config.FileServerHits} times!</p>
    </body>
    </html>";

    await context.Response.WriteAsync(htmlContent);
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

