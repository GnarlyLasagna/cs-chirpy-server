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

public string GenerateToken(string userId)
{
    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secretKey));
    var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

    var now = DateTime.UtcNow;
    var tokenDescriptor = new SecurityTokenDescriptor
    {
        Subject = new ClaimsIdentity(new[] { new Claim(JwtRegisteredClaimNames.Sub, userId) }),
        Expires = now.AddYears(100), // Set to a distant future date
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

