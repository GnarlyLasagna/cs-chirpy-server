using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Handlers;

var ChirpsHandlers = new ChirpsHandlers();
var UsersHandlers = new UsersHandlers();
var LoginHandlers = new LoginHandlers();
var SingleUseHandlers = new SingleUseHandlers();
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
app.MapGet("/app", SingleUseHandlers.FsHandler);
app.MapGet("/app/assets", SingleUseHandlers.AssetsHandler);
app.MapGet("/admin/metrics", context => SingleUseHandlers.MetricsHandler(context, config));

app.MapGet("/api/reset", context => SingleUseHandlers.ResetHandler(context, config));
app.MapGet("/api/healthz", SingleUseHandlers.WriteOkResponse);

app.MapPost("/api/polka/webhooks", SingleUseHandlers.HandlerPolkaWebhooks);

app.MapPost("/api/chirps", ChirpsHandlers.HandlerChirpsCreate);
app.MapGet("/api/chirps", ChirpsHandlers.HandlerChirpsRetrieve);
app.MapGet("/api/chirps/{chirpID:int}", ChirpsHandlers.HandlerChirpRetrieveById);

app.MapPost("/api/users", UsersHandlers.HandlerUsersCreate);
app.MapPost("/api/login", LoginHandlers.HandlerLogin);
app.MapPut("/api/users", UsersHandlers.HandlerUsersUpdate);
app.MapDelete("/api/chirps/{chirpID:int}", ChirpsHandlers.HandlerChirpsDelete);

app.Run();
