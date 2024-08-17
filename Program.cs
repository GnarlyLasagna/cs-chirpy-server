using Microsoft.AspNetCore.Authentication.JwtBearer;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

var jwtSecret = Environment.GetEnvironmentVariable("JWT_SECRET") ?? string.Empty;
var tokenHandler = new JwtSecurityTokenHandler();
var key = Encoding.ASCII.GetBytes(jwtSecret);

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(key),
            ValidateIssuer = false,
            ValidateAudience = false,
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddSingleton<ApiConfig>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
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

// Route Handlers
app.MapGet("/app", FsHandler);
app.MapGet("/app/assets", AssetsHandler);
app.MapGet("/admin/metrics", MetricsHandler);
app.MapGet("/api/reset", ResetHandler);
app.MapGet("/api/healthz", WriteOkResponse);

app.MapPost("/api/chirps", HandlerChirpsCreate);
app.MapGet("/api/chirps", HandlerChirpsRetrieve);
app.MapGet("/api/chirps/{chirpID:int}", HandlerChirpRetrieveById);

app.MapPost("/api/users", HandlerUsersCreate);
app.MapPost("/api/login", HandlerLogin);
app.MapPut("/api/users", HandlerUsersUpdate);

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

async Task HandlerLogin(HttpContext context)
{
    try
    {
        var bodyString = await new StreamReader(context.Request.Body).ReadToEndAsync();
        var loginRequest = JsonSerializer.Deserialize<LoginRequest>(bodyString);

        if (loginRequest == null || string.IsNullOrEmpty(loginRequest.Email) || string.IsNullOrEmpty(loginRequest.Password))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid login data" });
            return;
        }

        var user = await AuthenticateUserAsync(loginRequest.Email, loginRequest.Password);
        if (user == null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid email or password" });
            return;
        }

        var jwtSecret = Environment.GetEnvironmentVariable("JWT_SECRET");
        if (string.IsNullOrEmpty(jwtSecret))
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new { error = "JWT secret is not configured" });
            return;
        }

        var expirationTime = loginRequest.ExpiresInSeconds.HasValue 
            ? TimeSpan.FromSeconds(loginRequest.ExpiresInSeconds.Value) 
            : TimeSpan.FromHours(24); // Default to 24 hours if no expiration is provided

        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.ASCII.GetBytes(jwtSecret);
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new Claim[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.ID.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            }),
            Expires = DateTime.UtcNow.Add(expirationTime),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        var tokenString = tokenHandler.WriteToken(token);

        // Save the token in the user's object
        user.Token = tokenString;
        var db = await GetDatabaseAsync();
        var userToUpdate = db.Users.FirstOrDefault(u => u.ID == user.ID);
        if (userToUpdate != null)
        {
            userToUpdate.Token = tokenString;
            await SaveDatabaseAsync(db);
        }
        else
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new { error = "User not found in the database" });
            return;
        }

        context.Response.StatusCode = StatusCodes.Status200OK;
        await context.Response.WriteAsJsonAsync(new { id = user.ID, email = user.Email, token = tokenString });
    }
    catch (Exception ex)
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new { error = "Something went wrong", details = ex.Message });
    }
}

async Task<User?> AuthenticateUserAsync(string email, string password)
{
    var dbData = await GetDatabaseAsync();
    var user = dbData.Users.FirstOrDefault(u => u.Email == email);

    if (user == null)
    {
        return null; // User not found
    }

    // Check the password
    var isPasswordValid = HashPassword(password, user.Password) != null;
    return isPasswordValid ? user : null;
}

async Task HandlerChirpsCreate(HttpContext context)
{
    try
    {
        // Ensure the user is authenticated
        var token = context.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        if (string.IsNullOrEmpty(token))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Unauthorized" });
            return;
        }

        var jwtSecret = Environment.GetEnvironmentVariable("JWT_SECRET");
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.ASCII.GetBytes(jwtSecret);
        
        tokenHandler.ValidateToken(token, new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(key),
            ValidateIssuer = false,
            ValidateAudience = false,
            ClockSkew = TimeSpan.Zero
        }, out SecurityToken validatedToken);

        var jwtToken = (JwtSecurityToken)validatedToken;
        var userId = int.Parse(jwtToken.Claims.First(x => x.Type == JwtRegisteredClaimNames.Sub).Value);

        // Read the request body
        var bodyString = await new StreamReader(context.Request.Body).ReadToEndAsync();
        var chirpRequest = JsonSerializer.Deserialize<ChirpRequest>(bodyString);

        if (chirpRequest == null || string.IsNullOrEmpty(chirpRequest.Body))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid chirp data" });
            return;
        }

        // Validate the chirp
        var cleanedBody = ValidateChirp(chirpRequest.Body, out string error);
        if (error != null)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error });
            return;
        }

        // Create the chirp and save to the database
        var chirp = await CreateChirpAsync(cleanedBody, userId);

        context.Response.StatusCode = StatusCodes.Status201Created;
        await context.Response.WriteAsJsonAsync(new { id = chirp.ID, body = chirp.Body, author_id = chirp.AuthorId });
    }
    catch (SecurityTokenException)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = "Invalid token" });
    }
    catch (Exception ex)
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new { error = "Something went wrong", details = ex.Message });
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

        // Respond with the sorted chirps as JSON, including the author_id
        context.Response.StatusCode = StatusCodes.Status200OK;
        await context.Response.WriteAsJsonAsync(sortedChirps.Select(c => new 
        { 
            id = c.ID, 
            body = c.Body, 
            author_id = c.AuthorId 
        }));
    }
    catch (Exception ex)
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new { error = "Something went wrong", details = ex.Message });
    }
}


async Task HandlerChirpRetrieveById(HttpContext context)
{
    try
    {
        // Extract chirpID from route
        if (!context.Request.RouteValues.TryGetValue("chirpID", out var chirpIdObj) || 
            !int.TryParse(chirpIdObj.ToString(), out var chirpID))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid chirp ID" });
            return;
        }

        // Retrieve the chirp by ID
        var dbChirps = await GetChirpsAsync();
        var chirp = dbChirps.FirstOrDefault(c => c.ID == chirpID);

        if (chirp == null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsJsonAsync(new { error = "Chirp not found" });
            return;
        }

        // Respond with the chirp as JSON
        context.Response.StatusCode = StatusCodes.Status200OK;
        await context.Response.WriteAsJsonAsync(chirp);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Exception: {ex}");
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        // context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new { error = "Something went wrong" });
    }
}

async Task HandlerUsersUpdate(HttpContext context)
{
    try
    {
        var token = context.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");

        var jwtSecret = Environment.GetEnvironmentVariable("JWT_SECRET");
        var tokenHandler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var key = Encoding.ASCII.GetBytes(jwtSecret);

        tokenHandler.ValidateToken(token, new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(key),
            ValidateIssuer = false,
            ValidateAudience = false,
            ClockSkew = TimeSpan.Zero
        }, out SecurityToken validatedToken);

        var jwtToken = (JwtSecurityToken)validatedToken;
        var userId = int.Parse(jwtToken.Claims.First(x => x.Type == JwtRegisteredClaimNames.Sub).Value);

        var db = await GetDatabaseAsync();
        var user = db.Users.FirstOrDefault(u => u.ID == userId);

        if (user == null || user.Token != token) // Verify the token matches the user's current token
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid token" });
            return;
        }

        var bodyString = await new StreamReader(context.Request.Body).ReadToEndAsync();
        var updateUserRequest = JsonSerializer.Deserialize<User>(bodyString);

        if (updateUserRequest == null || string.IsNullOrEmpty(updateUserRequest.Email) || string.IsNullOrEmpty(updateUserRequest.Password))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid user data" });
            return;
        }

        user.Email = updateUserRequest.Email;
        user.Password = HashPassword(updateUserRequest.Password);
        await SaveDatabaseAsync(db);

        context.Response.StatusCode = StatusCodes.Status200OK;
        await context.Response.WriteAsJsonAsync(new { id = user.ID, email = user.Email });
    }
    catch (SecurityTokenException)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = "Invalid token" });
    }
    catch (Exception ex)
    {
       // context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = "Something went wrong", details = ex.Message });
    }
}

async Task HandlerUsersCreate(HttpContext context)
{
    try
    {
        var bodyString = await new StreamReader(context.Request.Body).ReadToEndAsync();
        Console.WriteLine("Raw Body: " + bodyString);

        var newUser = System.Text.Json.JsonSerializer.Deserialize<User>(bodyString);
        Console.WriteLine("Deserialized User: " + (newUser != null ? $"Email: {newUser.Email}, Password: {newUser.Password}" : "null"));

        if (newUser == null || string.IsNullOrEmpty(newUser.Email) || string.IsNullOrEmpty(newUser.Password))
        {
            Console.WriteLine("User data is invalid!!!");
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid user data" });
            return;
        }

        // Create user and save to the database
        newUser.Password = HashPassword(newUser.Password);
        var db = await GetDatabaseAsync();
        newUser.ID = db.Users.Count + 1;
        db.Users.Add(newUser);
        await SaveDatabaseAsync(db);

        context.Response.StatusCode = StatusCodes.Status201Created;
        await context.Response.WriteAsJsonAsync(new { id = newUser.ID, email = newUser.Email });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Exception: {ex}");
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new { error = "Something went wrong" });
    }
}

async Task<List<Chirp>> GetChirpsAsync()
{
    try
    {
        var jsonData = await File.ReadAllTextAsync("database.json");
        return JsonSerializer.Deserialize<Database>(jsonData)?.Chirps ?? new List<Chirp>();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error reading chirps: {ex.Message}");
        return new List<Chirp>();
    }
}

async Task<Chirp> CreateChirpAsync(string body, int authorId)
{
    var db = await GetDatabaseAsync();
    var chirp = new Chirp
    {
        ID = db.Chirps.Count + 1,
        Body = body,
        AuthorId = authorId // Associate chirp with the user's ID
    };
    db.Chirps.Add(chirp);
    await SaveDatabaseAsync(db);
    return chirp;
}



string GenerateDefaultSecret()
{
    // Generate a default secret key for JWT if not provided
    var key = new byte[32]; // 256 bits
    using (var rng = RandomNumberGenerator.Create())
    {
        rng.GetBytes(key);
    }
    return Convert.ToBase64String(key);
}

async Task SaveDatabaseAsync(Database db)
{
    try
    {
        var jsonData = JsonSerializer.Serialize(db, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync("database.json", jsonData);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error saving database: {ex.Message}");
    }
}

async Task<Database> GetDatabaseAsync()
{
    try
    {
        var jsonData = await File.ReadAllTextAsync("database.json");
        return JsonSerializer.Deserialize<Database>(jsonData) ?? new Database();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error reading database: {ex.Message}");
        return new Database();
    }
}

string HashPassword(string password, string? storedHash = null)
{
    if (storedHash == null)
    {
        var salt = new byte[16];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(salt);
        }

        var hash = KeyDerivation.Pbkdf2(
            password: password,
            salt: salt,
            prf: KeyDerivationPrf.HMACSHA256,
            iterationCount: 10000,
            numBytesRequested: 32
        );

        var hashBytes = new byte[48];
        Array.Copy(salt, 0, hashBytes, 0, 16);
        Array.Copy(hash, 0, hashBytes, 16, 32);

        return Convert.ToBase64String(hashBytes);
    }
    else
    {
        var hashBytes = Convert.FromBase64String(storedHash);
        var salt = hashBytes.Take(16).ToArray();
        var storedHashBytes = hashBytes.Skip(16).ToArray();

        var hash = KeyDerivation.Pbkdf2(
            password: password,
            salt: salt,
            prf: KeyDerivationPrf.HMACSHA256,
            iterationCount: 10000,
            numBytesRequested: 32
        );

        return hash.SequenceEqual(storedHashBytes) ? storedHash : null;
    }
}


