using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CiberZone.API.Data;
using CiberZone.API.DTOs;
using CiberZone.API.Models;

namespace CiberZone.API.Controllers;

[ApiController]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly AppDbContext _db;

    public UsersController(AppDbContext db) => _db = db;

    // GET /api/users  (solo admin)
    [HttpGet]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> GetAll()
    {
        var users = await _db.Usuarios
            .OrderByDescending(u => u.CreadoEn)
            .Select(u => new UsuarioDto(u.Id, u.Username, u.Rol, u.CreadoEn, u.Activo))
            .ToListAsync();
        return Ok(users);
    }

    // POST /api/users  (solo admin)
    [HttpPost]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
            return BadRequest(new { message = "Usuario y contraseña son requeridos." });

        if (await _db.Usuarios.AnyAsync(u => u.Username == req.Username))
            return Conflict(new { message = "Ese nombre de usuario ya existe." });

        var user = new Usuario
        {
            Username = req.Username.Trim(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
            Rol = "user"
        };

        _db.Usuarios.Add(user);
        await _db.SaveChangesAsync();
        return Ok(new UsuarioDto(user.Id, user.Username, user.Rol, user.CreadoEn, user.Activo));
    }

    // DELETE /api/users/{id}  (solo admin)
    [HttpDelete("{id:int}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Delete(int id)
    {
        var user = await _db.Usuarios.FindAsync(id);
        if (user == null) return NotFound();
        if (user.Rol == "admin") return BadRequest(new { message = "No puedes eliminar un admin." });

        _db.Usuarios.Remove(user);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
