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
public class ChirpsHandlers
{

public async Task HandlerChirpsRetrieve(HttpContext context)
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


public async Task HandlerChirpRetrieveById(HttpContext context)
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


public async Task HandlerChirpsDelete(HttpContext context)
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

        // Extract chirpID from route
        if (!context.Request.RouteValues.TryGetValue("chirpID", out var chirpIdObj) || 
            !int.TryParse(chirpIdObj.ToString(), out var chirpID))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid chirp ID" });
            return;
        }

        // Retrieve and delete the chirp
        var db = await DatabaseHelpers.GetDatabaseAsync();
        var chirp = db.Chirps.FirstOrDefault(c => c.ID == chirpID);

        if (chirp == null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsJsonAsync(new { error = "Chirp not found" });
            return;
        }

        if (chirp.AuthorId != userId)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { error = "You are not authorized to delete this chirp" });
            return;
        }

        db.Chirps.Remove(chirp);
        await DatabaseHelpers.SaveDatabaseAsync(db);
        context.Response.StatusCode = StatusCodes.Status204NoContent;
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

public async Task HandlerChirpsCreate(HttpContext context)
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

public string ValidateChirp(string body, out string error)
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

public string CleanProfanity(string input, HashSet<string> badWords)
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

public async Task<List<Chirp>> GetChirpsAsync()
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

public async Task<Chirp> CreateChirpAsync(string body, int authorId)
{
    var db = await DatabaseHelpers.GetDatabaseAsync();
    var chirp = new Chirp
    {
        ID = db.Chirps.Count + 1,
        Body = body,
        AuthorId = authorId // Associate chirp with the user's ID
    };
    db.Chirps.Add(chirp);
    await DatabaseHelpers.SaveDatabaseAsync(db);

    return chirp;
}

}
}
