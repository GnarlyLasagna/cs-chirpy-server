using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;


namespace Handlers
{
    public class UsersHandlers
    {

        public async Task HandlerUsersUpdate(HttpContext context)
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
                user.Password = DatabaseHelpers.HashPassword(updateUserRequest.Password);
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

        public async Task HandlerUsersCreate(HttpContext context)
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
                newUser.Password = DatabaseHelpers.HashPassword(newUser.Password);
                var db = await GetDatabaseAsync();
                newUser.ID = db.Users.Count + 1;
                db.Users.Add(newUser);
                await SaveDatabaseAsync(db);

                context.Response.StatusCode = StatusCodes.Status201Created;
                await context.Response.WriteAsJsonAsync(new
                {
                    id = newUser.ID,
                    email = newUser.Email,
                    is_chirpy_red = newUser.IsChirpyRed // Include the new field in the response
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception: {ex}");
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await context.Response.WriteAsJsonAsync(new { error = "Something went wrong" });
            }
        }



        public string GenerateDefaultSecret()
        {
            // Generate a default secret key for JWT if not provided
            var key = new byte[32]; // 256 bits
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(key);
            }
            return Convert.ToBase64String(key);
        }



        public async Task FsHandler(HttpContext context)
        {
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.SendFileAsync(Path.Combine("wwwroot", "index.html"));
        }

        public async Task WriteOkResponse(HttpContext context)
        {
            context.Response.Headers.Append("Content-Type", "text/plain; charset=utf-8");
            context.Response.StatusCode = StatusCodes.Status200OK;
            await context.Response.WriteAsync("OK");
        }

        public async Task AssetsHandler(HttpContext context)
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

        public async Task SaveDatabaseAsync(Database db)
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

        public async Task<Database> GetDatabaseAsync()
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


        public async Task HandlerPolkaWebhooks(HttpContext context)
        {
            try
            {
                // Check for the API key in the Authorization header
                if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader) ||
                    !authHeader.ToString().StartsWith("ApiKey ") ||
                    authHeader.ToString().Substring(7) != Environment.GetEnvironmentVariable("POLKA_API_KEY"))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }

                // Read the request body
                var bodyString = await new StreamReader(context.Request.Body).ReadToEndAsync();
                var webhookRequest = JsonSerializer.Deserialize<WebhookRequest>(bodyString);

                if (webhookRequest == null || webhookRequest.Event != "user.upgraded")
                {
                    context.Response.StatusCode = StatusCodes.Status204NoContent;
                    return; // Ignore other events
                }

                // Process the "user.upgraded" event
                var userId = webhookRequest.Data.UserId;
                var db = await GetDatabaseAsync();
                var user = db.Users.FirstOrDefault(u => u.ID == userId);

                if (user == null)
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                user.IsChirpyRed = true; // Mark user as Chirpy Red
                await SaveDatabaseAsync(db);
                Console.WriteLine($"user {user}");

                context.Response.StatusCode = StatusCodes.Status204NoContent;
            }
            catch (Exception ex)
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await context.Response.WriteAsJsonAsync(new { error = "Something went wrong", details = ex.Message });
            }
        }

        public class WebhookRequest
        {

            [JsonPropertyName("event")]
            public string Event { get; set; }
            [JsonPropertyName("data")]
            public WebhookData Data { get; set; }
        }

        public class WebhookData
        {
            [JsonPropertyName("user_id")]
            public int UserId { get; set; }
        }
    }
}
