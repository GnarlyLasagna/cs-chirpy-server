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
using Handlers;

var ChirpsHandlers = new ChirpsHandlers();
var UsersHandlers = new UsersHandlers();
var LoginHandlers = new LoginHandlers();
var WebhookHandlers = new WebhookHandlers();
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
app.MapGet("/app", WebhookHandlers.FsHandler);
app.MapGet("/app/assets", WebhookHandlers.AssetsHandler);
app.MapGet("/admin/metrics", Metrics.MetricsHandler);
app.MapGet("/api/reset", ResetHandler);
app.MapGet("/api/healthz", WebhookHandlers.WriteOkResponse);

app.MapPost("/api/chirps", ChirpsHandlers.HandlerChirpsCreate);
app.MapGet("/api/chirps", ChirpsHandlers.HandlerChirpsRetrieve);
app.MapGet("/api/chirps/{chirpID:int}", ChirpsHandlers.HandlerChirpRetrieveById);

app.MapPost("/api/users", UsersHandlers.HandlerUsersCreate);
app.MapPost("/api/login", LoginHandlers.HandlerLogin);
app.MapPut("/api/users", UsersHandlers.HandlerUsersUpdate);
app.MapDelete("/api/chirps/{chirpID:int}", ChirpsHandlers.HandlerChirpsDelete);

app.MapPost("/api/polka/webhooks", WebhookHandlers.HandlerPolkaWebhooks);

app.Run();

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
