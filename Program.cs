using System.Text.Json;
using System.Text.Json.Serialization;
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
app.MapGet("/admin/metrics", MetricsHandler);
app.MapGet("/api/reset", ResetHandler);
app.MapGet("/api/healthz", WriteOkResponse);

app.MapPost("/api/chirps", HandlerChirpsCreate);
app.MapGet("/api/chirps", HandlerChirpsRetrieve);
app.MapGet("/api/chirps/{chirpID:int}", HandlerChirpRetrieveById);

app.MapPost("/api/users", HandlerUsersCreate);

app.Run();

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

async Task HandlerChirpsCreate(HttpContext context)
{
    try
    {
        // Read the request body
        var bodyString = await new StreamReader(context.Request.Body).ReadToEndAsync();
        Console.WriteLine("Raw Body: " + bodyString);

        // Deserialize the body string into ChirpRequest
        var chirpRequest = System.Text.Json.JsonSerializer.Deserialize<ChirpRequest>(bodyString);

        if (chirpRequest == null || string.IsNullOrEmpty(chirpRequest.Body))
        {
            Console.WriteLine("Chirp request is null or body is empty");
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid chirp data" });
            return;
        }

        // Validate the chirp
        if (chirpRequest?.Body != null)
        {
            var cleanedBody = ValidateChirp(chirpRequest.Body, out string error);

        if (error != null)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error });
            return;
        }

        // Create the chirp and save to the database
        var chirp = await CreateChirpAsync(cleanedBody);

        context.Response.StatusCode = StatusCodes.Status201Created;
        await context.Response.WriteAsJsonAsync(new { id = chirp.ID, body = chirp.Body });
    }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Exception: {ex}");
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new { error = "Something went wrong" });
    }
}

string ValidateChirp(string body, out string error)
{
    const int MaxChirpLength = 140;
    error = null;

    if (body.Length > MaxChirpLength)
    {
        error = "Chirp is too long";
        return null;
    }

    var profaneWords = new HashSet<string> { "kerfuffle", "sharbert", "fornax" };
    var cleanedBody = CleanProfanity(body, profaneWords);
    return cleanedBody;
}

string CleanProfanity(string input, HashSet<string> badWords)
{
    var words = input.Split(' ');
    for (int i = 0; i < words.Length; i++)
    {
        if (badWords.Contains(words[i].ToLower()))
        {
            words[i] = "****";
        }
    }
    return string.Join(" ", words);
}

async Task HandlerChirpsRetrieve(HttpContext context)
{
    try
    {
        // Retrieve chirps from database.json
        var dbChirps = await GetChirpsAsync();

        if (dbChirps == null)
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new { error = "Couldn't retrieve chirps" });
            return;
        }

        // Sort the chirps by ID
        var sortedChirps = dbChirps.OrderBy(c => c.ID).ToList();

        // Respond with the sorted chirps as JSON
        context.Response.StatusCode = StatusCodes.Status200OK;
        await context.Response.WriteAsJsonAsync(sortedChirps);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Exception: {ex}");
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new { error = "Something went wrong" });
    }
}

async Task HandlerChirpRetrieveById(HttpContext context)
{
    try
    {
        // Extract chirpID from route
        if (!context.Request.RouteValues.TryGetValue("chirpID", out var chirpIdObj) || 
            !int.TryParse(chirpIdObj.ToString(), out int chirpId))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid chirp ID" });
            return;
        }

        // Retrieve chirps from database.json
        var chirps = await GetChirpsAsync();
        var chirp = chirps.FirstOrDefault(c => c.ID == chirpId);

        if (chirp == null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsJsonAsync(new { error = "Chirp not found" });
            return;
        }

        context.Response.StatusCode = StatusCodes.Status200OK;
        await context.Response.WriteAsJsonAsync(chirp);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Exception: {ex}");
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new { error = "Something went wrong" });
    }
}


async Task HandlerUsersCreate(HttpContext context)
{
    try
    {
        // Read the request body
        var bodyString = await new StreamReader(context.Request.Body).ReadToEndAsync();
        Console.WriteLine("Raw Body: " + bodyString);

        // Deserialize the body string into UserRequest
        var userRequest = System.Text.Json.JsonSerializer.Deserialize<UserRequest>(bodyString);

        if (userRequest == null || string.IsNullOrEmpty(userRequest.Email))
        {
            Console.WriteLine("User request is null or email is empty");
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid user data" });
            return;
        }

        // Create the user and save to the database
        var user = await CreateUserAsync(userRequest.Email);

        if (user == null)
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new { error = "Couldn't create user" });
            return;
        }

        context.Response.StatusCode = StatusCodes.Status201Created;
        await context.Response.WriteAsJsonAsync(new { id = user.ID, email = user.Email });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Exception: {ex}");
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new { error = "Something went wrong" });
    }
}

async Task<User?> CreateUserAsync(string email)
{
    var dbData = await GetDatabaseAsync();
    var users = dbData.Users;

    var newUserId = users.Any() ? users.Max(u => u.ID) + 1 : 1;
    var newUser = new User { ID = newUserId, Email = email };

    users.Add(newUser);
    dbData.Users = users;

    try
    {
        await SaveDatabaseAsync(dbData);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error saving user: {ex.Message}");
        return null;
    }

    return newUser;
}

async Task<Database> GetDatabaseAsync()
{
    var dbData = new Database();

    try
    {
        var jsonData = await File.ReadAllTextAsync("database.json");
        dbData = System.Text.Json.JsonSerializer.Deserialize<Database>(jsonData) ?? new Database();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error reading database: {ex.Message}");
    }

    return dbData;
}

async Task SaveDatabaseAsync(Database dbData)
{
    try
    {
        var jsonData = System.Text.Json.JsonSerializer.Serialize(dbData);
        await File.WriteAllTextAsync("database.json", jsonData);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error saving database: {ex.Message}");
    }
}

async Task<List<Chirp>> GetChirpsAsync()
{
    var chirps = new List<Chirp>();

    try
    {
        var jsonData = await File.ReadAllTextAsync("database.json");
        chirps = System.Text.Json.JsonSerializer.Deserialize<List<Chirp>>(jsonData) ?? new List<Chirp>();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error reading chirps: {ex.Message}");
        return new List<Chirp>(); // Return an empty list instead of null
    }

    return chirps;
}

async Task<Chirp?> CreateChirpAsync(string body)
{
    var chirps = await GetChirpsAsync();
    var newChirpId = chirps.Any() ? chirps.Max(c => c.ID) + 1 : 1;
    var newChirp = new Chirp { ID = newChirpId, Body = body };

    chirps.Add(newChirp);

    try
    {
        var jsonData = System.Text.Json.JsonSerializer.Serialize(chirps);
        await File.WriteAllTextAsync("database.json", jsonData);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error saving chirp: {ex.Message}");
        return null; // It's okay to return null if the return type is nullable
    }

    return newChirp;
}

class Chirp
{
    public int? ID { get; set; }
    public string? Body { get; set; }
}

public class ChirpRequest
{
    [JsonPropertyName("body")]
    public string? Body { get; set; }
}
class Database
{
    public List<Chirp> Chirps { get; set; } = new List<Chirp>();
    public List<User> Users { get; set; } = new List<User>();
}

class UserRequest
{
    [JsonPropertyName("email")]
    public string? Email { get; set; }
}


class User
{
    public int? ID { get; set; }
    public string? Email { get; set; }
}


