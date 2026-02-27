using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using CiberZone.API.Data;
using CiberZone.API.DTOs;
using CiberZone.API.Models;

namespace CiberZone.API.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _cfg;

    public AuthController(AppDbContext db, IConfiguration cfg)
    {
        _db = db;
        _cfg = cfg;
    }

    // POST /api/auth/login
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        var user = await _db.Usuarios
            .FirstOrDefaultAsync(u => u.Username == req.Username && u.Activo);

        if (user == null || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
            return Unauthorized(new { message = "Usuario o contraseña incorrectos." });

        var token = GenerarToken(user);
        return Ok(new TokenResponse(token, user.Username, user.Rol));
    }

    // POST /api/auth/register
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] CreateUserRequest req)
    {
        var username = (req.Username ?? "").Trim();
        var password = req.Password ?? "";

        if (username.Length < 3)
            return BadRequest(new { message = "El usuario debe tener al menos 3 caracteres." });

        if (password.Length < 4)
            return BadRequest(new { message = "La contraseña debe tener al menos 4 caracteres." });

        var usernameLower = username.ToLowerInvariant();
        var exists = await _db.Usuarios.AnyAsync(u => u.Username.ToLower() == usernameLower);
        if (exists)
            return Conflict(new { message = "Ese nombre de usuario ya existe." });

        var user = new Usuario
        {
            Username = username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            Rol = "user",
            Activo = true
        };

        _db.Usuarios.Add(user);
        await _db.SaveChangesAsync();

        var token = GenerarToken(user);
        return Ok(new TokenResponse(token, user.Username, user.Rol));
    }

    private string GenerarToken(Usuario user)
    {
        var jwtKey = _cfg["Jwt:Key"] ?? "CiberZoneSecretKey2025!MuySegura";
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Role, user.Rol)
        };

        var token = new JwtSecurityToken(
            issuer: _cfg["Jwt:Issuer"] ?? "CiberZone",
            audience: _cfg["Jwt:Audience"] ?? "CiberZoneApp",
            claims: claims,
            expires: DateTime.UtcNow.AddDays(7),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
