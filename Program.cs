using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;
using CiberZone.API.Data;
using CiberZone.API.Models;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// DATABASE PostgreSQL (compatible with local, Supabase, Neon, Railway, Render, etc.)
var connStr = DbConnectionFactory.ResolveConnectionString(builder.Configuration);
try
{
    var csb = new NpgsqlConnectionStringBuilder(connStr);
    Console.WriteLine($"[Startup] DB target Host={csb.Host}; Port={csb.Port}; Database={csb.Database}; Username={csb.Username}");
}
catch
{
    Console.WriteLine("[Startup] DB connection string detected but could not parse details.");
}

builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseNpgsql(connStr, npgsql =>
    {
        npgsql.CommandTimeout(120);
        npgsql.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(10),
            errorCodesToAdd: null);
    }));

// JWT
var jwtKey = builder.Configuration["Jwt:Key"] ?? "CiberZoneSecretKey2025!MuySegura";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt =>
    {
        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "CiberZone",
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "CiberZoneApp",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddControllers();
builder.Services.Configure<FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = 262144000; // 250 MB per request
});

// SWAGGER
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "CiberZone API",
        Version = "v1",
        Description = "API para CiberZone - Cafe Internet Guatemala"
    });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        In = ParameterLocation.Header,
        Description = "Ingresa el token JWT: Bearer {token}",
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
            Array.Empty<string>()
        }
    });
});

// CORS
builder.Services.AddCors(opt =>
    opt.AddDefaultPolicy(p =>
        p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

// AUTO MIGRATIONS
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    const int maxAttempts = 5;
    var migrated = false;
    for (var attempt = 1; attempt <= maxAttempts; attempt++)
    {
        try
        {
            db.Database.Migrate();
            migrated = true;
            break;
        }
        catch when (attempt < maxAttempts)
        {
            var delayMs = 3000 * attempt;
            Console.WriteLine($"[Startup] DB migrate failed (attempt {attempt}/{maxAttempts}). Retrying in {delayMs} ms...");
            Thread.Sleep(delayMs);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Startup] DB migrate failed definitively: {ex.Message}");
        }
    }

    if (!migrated)
    {
        Console.WriteLine("[Startup] Running without DB migration. Check your PostgreSQL credentials/connection.");
    }
    else
    {
        try
        {
            var adminUsername = builder.Configuration["AdminBootstrap:Username"] ?? "ciberzone";
            var adminPassword = builder.Configuration["AdminBootstrap:Password"] ?? "Admin2025#";

            var adminExists = db.Usuarios.Any(u => u.Username == adminUsername);
            if (!adminExists)
            {
                db.Usuarios.Add(new Usuario
                {
                    Username = adminUsername,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(adminPassword),
                    Rol = "admin",
                    Activo = true
                });
                db.SaveChanges();
                Console.WriteLine("[Startup] Default admin user created.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Startup] Admin bootstrap skipped: {ex.Message}");
        }
    }
}

// PIPELINE
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseDefaultFiles();   // serve index.html from wwwroot
app.UseStaticFiles();    // serve frontend from wwwroot

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok", utc = DateTime.UtcNow }));
app.MapControllers();

// SPA fallback: any non-API route returns index.html
app.MapFallbackToFile("index.html");

app.Run();
