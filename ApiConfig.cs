using System.Text.Json.Serialization;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

public class ApiConfig
{
    public int FileServerHits { get; set; }
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

