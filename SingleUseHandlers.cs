using System.Text.Json;
using System.Text.Json.Serialization;


namespace Handlers
{
    public class SingleUseHandlers
    {

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

        public async Task ResetHandler(HttpContext context, ApiConfig config)
        {
            config.FileServerHits = 0;
            context.Response.StatusCode = StatusCodes.Status200OK;
            await context.Response.WriteAsync("File server hits counter reset.");
        }

        public async Task MetricsHandler(HttpContext context, ApiConfig config)
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
                var db = await DatabaseHelpers.GetDatabaseAsync();
                var user = db.Users.FirstOrDefault(u => u.ID == userId);

                if (user == null)
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                user.IsChirpyRed = true; // Mark user as Chirpy Red
                await DatabaseHelpers.SaveDatabaseAsync(db);
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
