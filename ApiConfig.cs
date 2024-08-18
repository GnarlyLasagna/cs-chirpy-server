using System.Text.Json.Serialization;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
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


public class ApiConfig
{
    public int FileServerHits { get; set; }
}

public class LoginRequest
{
    [JsonPropertyName("email")]
    public string Email { get; set; }

    [JsonPropertyName("password")]
    public string Password { get; set; }

    [JsonPropertyName("expires_in_seconds")]
    public int? ExpiresInSeconds { get; set; } = 3600;
}

public class User
{
    public int ID { get; set; }

    [JsonPropertyName("email")]
    public string Email { get; set; }

    [JsonPropertyName("password")]
    public string Password { get; set; }
    public string? Token { get; set; }

    public bool IsChirpyRed { get; set; } = false;
}

public class Chirp
{
    public int ID { get; set; }
    public string Body { get; set; }
    public int AuthorId { get; set; }
}

public class ChirpRequest
{
    [JsonPropertyName("body")]
    public string Body { get; set; }
}

public class Database
{
    public List<Chirp> Chirps { get; set; } = new List<Chirp>();
    public List<User> Users { get; set; } = new List<User>();
  //  public string? Token { get; set; }
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

public static class DatabaseHelpers
{
    private const string FilePath = "database.json";

    public static async Task SaveDatabaseAsync(Database db)
    {
        try
        {
            var jsonData = JsonSerializer.Serialize(db, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(FilePath, jsonData);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving database: {ex.Message}");
        }
    }

    public static async Task<Database> GetDatabaseAsync()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new Database(); // Return a new instance if the file doesn't exist
            }

            var jsonData = await File.ReadAllTextAsync(FilePath);
            return JsonSerializer.Deserialize<Database>(jsonData) ?? new Database();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reading database: {ex.Message}");
            return new Database();
        }
    }
    public static string HashPassword(string password, string? storedHash = null)
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
}

public class TokenService
{
    private readonly string _secretKey;

    public TokenService(string secretKey)
    {
        _secretKey = secretKey;
    }

    public string GenerateToken(string userId, int? expiresInSeconds = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secretKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var now = DateTime.UtcNow;

        // Set expiration based on parameter or default to approximately 1 year
        var expirationTime = expiresInSeconds.HasValue 
            ? TimeSpan.FromSeconds(expiresInSeconds.Value) 
            : TimeSpan.FromDays(365); // Default to 1 year (approximately)

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[] { new Claim(JwtRegisteredClaimNames.Sub, userId) }),
            Expires = now.Add(expirationTime),
            IssuedAt = now,
            NotBefore = now, // Set to current time
            SigningCredentials = creds
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(tokenDescriptor);
        var tokenString = tokenHandler.WriteToken(token);

        // Log the token details for debugging
        Console.WriteLine($"Generated Token: {tokenString}");
        Console.WriteLine($"NotBefore: {tokenDescriptor.NotBefore}");
        Console.WriteLine($"Expires: {tokenDescriptor.Expires}");

        return tokenString;
    }
}

