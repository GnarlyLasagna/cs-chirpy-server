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



namespace Handlers
{
public class LoginHandlers
{

public async Task HandlerLogin(HttpContext context)
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
        var db = await DatabaseHelpers.GetDatabaseAsync();
        var userToUpdate = db.Users.FirstOrDefault(u => u.ID == user.ID);
        if (userToUpdate != null)
        {
            userToUpdate.Token = tokenString;
            await DatabaseHelpers.SaveDatabaseAsync(db);
        }
        else
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new { error = "User not found in the database" });
            return;
        }

        context.Response.StatusCode = StatusCodes.Status200OK;
        await context.Response.WriteAsJsonAsync(new 
        { 
            id = user.ID, 
            email = user.Email, 
            token = tokenString,
            is_chirpy_red = user.IsChirpyRed // Include the new field in the response
        });
    }
    catch (Exception ex)
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new { error = "Something went wrong", details = ex.Message });
    }
}


public async Task<User?> AuthenticateUserAsync(string email, string password)
{
    var dbData = await DatabaseHelpers.GetDatabaseAsync();
    var user = dbData.Users.FirstOrDefault(u => u.Email == email);

    if (user == null)
    {
        return null; // User not found
    }

    // Check the password
    var isPasswordValid = DatabaseHelpers.HashPassword(password, user.Password) != null;
    return isPasswordValid ? user : null;
}
}
}
