using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;
using NetTopologySuite.Geometries;
using WorldMapLeaflet.Data;
using WorldMapLeaflet.Models;

var builder = WebApplication.CreateBuilder(args);

// DbContext + PostgreSQL + PostGIS
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        npgsqlOptions => npgsqlOptions.UseNetTopologySuite()
    ));

// Identity
builder.Services.AddIdentity<User, IdentityRole>(options =>
{
    options.Password.RequireDigit = false;
    options.Password.RequireLowercase = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredLength = 4;
    options.User.RequireUniqueEmail = true;
})
.AddEntityFrameworkStores<AppDbContext>()
.AddDefaultTokenProviders();

// JWT Authentication
var jwtKey = builder.Configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key not found");
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? throw new InvalidOperationException("Jwt:Issuer not found");
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? throw new InvalidOperationException("Jwt:Audience not found");

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtIssuer,
        ValidAudience = jwtAudience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        ClockSkew = TimeSpan.Zero
    };
});

builder.Services.AddAuthorization();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:5008", "https://localhost:7182")
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});

var app = builder.Build();

// Database creation - Ensure both Identity and custom tables are created
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    try
    {
        // EnsureCreated creates tables but doesn't handle migrations well
        // It should work for Identity tables though
        var created = db.Database.EnsureCreated();
        Console.WriteLine($"Database EnsureCreated returned: {created}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error during EnsureCreated: {ex.Message}");
    }
}

app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

// HttpClient for Nominatim
var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("WorldMapLeaflet/1.0");
http.DefaultRequestHeaders.From = builder.Configuration["Nominatim:Email"] ?? "senin.gercek.mailin@ornek.com"; // <-- GERÇEK mailini yaz

// JWT Helper
string GenerateJwtToken(User user)
{
    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
    var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    var expirationDays = int.Parse(builder.Configuration["Jwt:ExpirationDays"] ?? "30");

    var claims = new[]
    {
        new Claim(ClaimTypes.NameIdentifier, user.Id),
        new Claim(ClaimTypes.Name, user.UserName ?? string.Empty),
        new Claim(ClaimTypes.Email, user.Email ?? string.Empty)
    };

    var token = new JwtSecurityToken(
        issuer: jwtIssuer,
        audience: jwtAudience,
        claims: claims,
        expires: DateTime.UtcNow.AddDays(expirationDays),
        signingCredentials: creds
    );

    return new JwtSecurityTokenHandler().WriteToken(token);
}

// Geocode API (public)
app.MapGet("/api/geocode", async (string q) =>
{
    if (string.IsNullOrWhiteSpace(q)) return Results.Json(Array.Empty<object>());
    var url =
        "https://nominatim.openstreetmap.org/search" +
        $"?format=json&addressdetails=1&limit=8&countrycodes=tr&accept-language=tr&q={Uri.EscapeDataString(q)}";

    try
    {
        var resp = await http.GetAsync(url);
        if (!resp.IsSuccessStatusCode)
            return Results.Problem($"Nominatim: {(int)resp.StatusCode}");
        var data = await resp.Content.ReadFromJsonAsync<object>();
        return Results.Json(data);
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
});

// Register
app.MapPost("/api/auth/register", async (RegisterRequest req, UserManager<User> userManager) =>
{
    if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
        return Results.BadRequest("Username and password are required");

    var user = new User
    {
        UserName = req.Username,
        Email = req.Email
    };

    var result = await userManager.CreateAsync(user, req.Password);
    if (!result.Succeeded)
        return Results.BadRequest(result.Errors);

    var token = GenerateJwtToken(user);
    return Results.Ok(new AuthResponse
    {
        Token = token,
        Username = user.UserName ?? string.Empty,
        Email = user.Email ?? string.Empty
    });
});

// Login
app.MapPost("/api/auth/login", async (LoginRequest req, UserManager<User> userManager) =>
{
    var user = await userManager.FindByNameAsync(req.Username);
    if (user == null || !await userManager.CheckPasswordAsync(user, req.Password))
        return Results.Unauthorized();

    var token = GenerateJwtToken(user);
    return Results.Ok(new AuthResponse
    {
        Token = token,
        Username = user.UserName ?? string.Empty,
        Email = user.Email ?? string.Empty
    });
});

// Get current user
app.MapGet("/api/auth/me", async (ClaimsPrincipal user, UserManager<User> userManager) =>
{
    var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
    if (string.IsNullOrEmpty(userId))
        return Results.Unauthorized();

    var dbUser = await userManager.FindByIdAsync(userId);
    if (dbUser == null)
        return Results.Unauthorized();

    return Results.Ok(new { dbUser.Id, dbUser.UserName, dbUser.Email });
}).RequireAuthorization();

// Get all parcels for current user
app.MapGet("/api/parcels", async (ClaimsPrincipal user, AppDbContext db) =>
{
    var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
    if (string.IsNullOrEmpty(userId))
        return Results.Unauthorized();

    var parcels = await db.Parcels
        .Where(p => p.UserId == userId)
        .ToListAsync();

    var responses = parcels.Select(p =>
    {
        var geometry = p.Geometry;
        var coordinates = new List<CoordinateDto>();
        
        if (geometry != null && geometry is Polygon polygon && polygon.ExteriorRing != null)
        {
            foreach (var coord in polygon.ExteriorRing.Coordinates)
            {
                coordinates.Add(new CoordinateDto { Lat = coord.Y, Lon = coord.X });
            }
            // Son koordinatı kaldır (kapalı polygon'da ilk ve son aynı)
            if (coordinates.Count > 0 && coordinates[0].Lat == coordinates[^1].Lat && 
                coordinates[0].Lon == coordinates[^1].Lon)
            {
                coordinates.RemoveAt(coordinates.Count - 1);
            }
        }

        return new ParcelResponse
        {
            Id = p.Id,
            Name = p.Name,
            Description = p.Description,
            Coordinates = coordinates,
            CreatedAt = p.CreatedAt,
            UpdatedAt = p.UpdatedAt
        };
    }).ToList();

    return Results.Ok(responses);
}).RequireAuthorization();

// Create parcel
app.MapPost("/api/parcels", async (CreateParcelRequest req, ClaimsPrincipal user, AppDbContext db) =>
{
    var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
    if (string.IsNullOrEmpty(userId))
        return Results.Unauthorized();

    if (req.Coordinates.Count < 4)
        return Results.BadRequest("En az 4 koordinat noktası gerekli");

    // Koordinatları kapat (ilk ve son nokta aynı olmalı polygon için)
    var coords = req.Coordinates.Select(c => new Coordinate(c.Lon, c.Lat)).ToList();
    if (coords[0] != coords[^1])
        coords.Add(coords[0]);

    var geometryFactory = new GeometryFactory(new PrecisionModel(), 4326);
    var polygon = geometryFactory.CreatePolygon(coords.ToArray());

    var parcel = new Parcel
    {
        UserId = userId,
        Name = req.Name,
        Description = req.Description,
        Geometry = polygon
    };

    db.Parcels.Add(parcel);
    await db.SaveChangesAsync();

    var response = new ParcelResponse
    {
        Id = parcel.Id,
        Name = parcel.Name,
        Description = parcel.Description,
        Coordinates = req.Coordinates,
        CreatedAt = parcel.CreatedAt,
        UpdatedAt = parcel.UpdatedAt
    };

    return Results.Created($"/api/parcels/{parcel.Id}", response);
}).RequireAuthorization();

// Get parcel by id
app.MapGet("/api/parcels/{id}", async (int id, ClaimsPrincipal user, AppDbContext db) =>
{
    var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
    if (string.IsNullOrEmpty(userId))
        return Results.Unauthorized();

    var parcel = await db.Parcels
        .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);

    if (parcel == null)
        return Results.NotFound();

    var coordinates = new List<CoordinateDto>();
    if (parcel.Geometry != null && parcel.Geometry is Polygon polygon && polygon.ExteriorRing != null)
    {
        foreach (var coord in polygon.ExteriorRing.Coordinates)
        {
            coordinates.Add(new CoordinateDto { Lat = coord.Y, Lon = coord.X });
        }
        // Son koordinatı kaldır (kapalı polygon'da ilk ve son aynı)
        if (coordinates.Count > 0 && coordinates[0].Lat == coordinates[^1].Lat && 
            coordinates[0].Lon == coordinates[^1].Lon)
        {
            coordinates.RemoveAt(coordinates.Count - 1);
        }
    }

    return Results.Ok(new ParcelResponse
    {
        Id = parcel.Id,
        Name = parcel.Name,
        Description = parcel.Description,
        Coordinates = coordinates,
        CreatedAt = parcel.CreatedAt,
        UpdatedAt = parcel.UpdatedAt
    });
}).RequireAuthorization();

// Update parcel
app.MapPut("/api/parcels/{id}", async (int id, CreateParcelRequest req, ClaimsPrincipal user, AppDbContext db) =>
{
    var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
    if (string.IsNullOrEmpty(userId))
        return Results.Unauthorized();

    var parcel = await db.Parcels
        .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);

    if (parcel == null)
        return Results.NotFound();

    if (req.Coordinates.Count < 4)
        return Results.BadRequest("En az 4 koordinat noktası gerekli");

    var coords = req.Coordinates.Select(c => new Coordinate(c.Lon, c.Lat)).ToList();
    if (coords[0] != coords[^1])
        coords.Add(coords[0]);

    var geometryFactory = new GeometryFactory(new PrecisionModel(), 4326);
    var polygon = geometryFactory.CreatePolygon(coords.ToArray());

    parcel.Name = req.Name;
    parcel.Description = req.Description;
    parcel.Geometry = polygon;
    parcel.UpdatedAt = DateTime.UtcNow;

    await db.SaveChangesAsync();

    var response = new ParcelResponse
    {
        Id = parcel.Id,
        Name = parcel.Name,
        Description = parcel.Description,
        Coordinates = req.Coordinates,
        CreatedAt = parcel.CreatedAt,
        UpdatedAt = parcel.UpdatedAt
    };

    return Results.Ok(response);
}).RequireAuthorization();

// Delete parcel
app.MapDelete("/api/parcels/{id}", async (int id, ClaimsPrincipal user, AppDbContext db) =>
{
    var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
    if (string.IsNullOrEmpty(userId))
        return Results.Unauthorized();

    var parcel = await db.Parcels
        .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);

    if (parcel == null)
        return Results.NotFound();

    db.Parcels.Remove(parcel);
    await db.SaveChangesAsync();

    return Results.NoContent();
}).RequireAuthorization();

app.Run();
