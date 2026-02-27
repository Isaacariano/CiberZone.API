using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json.Nodes;
using System.Text.Json;
using CiberZone.API.Data;
using CiberZone.API.DTOs;
using CiberZone.API.Models;

namespace CiberZone.API.Controllers;

[ApiController]
[Route("api/orders")]
public class OrdersController : ControllerBase
{
    private const long MaxFileSizeBytes = 50L * 1024 * 1024;
    private readonly string _uploadsRoot;
    private readonly AppDbContext _db;

    public OrdersController(AppDbContext db, IWebHostEnvironment env)
    {
        _db = db;
        var webRoot = string.IsNullOrWhiteSpace(env.WebRootPath)
            ? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot")
            : env.WebRootPath;
        _uploadsRoot = Path.Combine(webRoot, "uploads", "pedidos");
    }

    // GET /api/orders  (solo admin)
    [HttpGet]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> GetAll([FromQuery] string? estado, [FromQuery] string? search)
    {
        var query = _db.Pedidos.AsQueryable();

        if (!string.IsNullOrWhiteSpace(estado) && estado != "all")
            query = query.Where(p => p.Estado == estado);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.ToLower();
            query = query.Where(p =>
                p.Nombre.ToLower().Contains(s) ||
                p.Telefono.Contains(s) ||
                p.Servicio.ToLower().Contains(s));
        }

        var pedidos = await query
            .OrderByDescending(p => p.CreadoEn)
            .Select(p => MapDto(p))
            .ToListAsync();

        return Ok(pedidos);
    }

    // GET /api/orders/mis-pedidos  (usuario logueado)
    [HttpGet("mis-pedidos")]
    [Authorize]
    public async Task<IActionResult> MisPedidos()
    {
        var userId = GetCurrentUserId();
        if (!userId.HasValue) return Unauthorized();

        var pedidos = await _db.Pedidos
            .Where(p => p.UsuarioId == userId.Value)
            .OrderByDescending(p => p.CreadoEn)
            .Select(p => MapDto(p))
            .ToListAsync();
        return Ok(pedidos);
    }

    // POST /api/orders  (publico o logueado) - JSON
    [HttpPost]
    [Consumes("application/json")]
    public async Task<IActionResult> Create([FromBody] CreatePedidoRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Nombre) || string.IsNullOrWhiteSpace(req.Telefono))
            return BadRequest(new { message = "Nombre y telefono son requeridos." });

        var dto = await CreatePedidoInternal(
            req.Nombre,
            req.Telefono,
            req.Servicio,
            req.Detalles,
            req.FechaPref,
            req.Origen,
            req.ArchivosJson
        );

        return Ok(dto);
    }

    // POST /api/orders  (publico o logueado) - multipart con archivos
    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestFormLimits(MultipartBodyLengthLimit = 262144000)] // 250 MB por solicitud
    public async Task<IActionResult> CreateWithFiles([FromForm] CreatePedidoFormRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Nombre) || string.IsNullOrWhiteSpace(req.Telefono))
            return BadRequest(new { message = "Nombre y telefono son requeridos." });

        string? archivosJson = null;

        if (req.Archivos is { Count: > 0 })
        {
            Directory.CreateDirectory(_uploadsRoot);
            var saved = new List<object>();

            foreach (var file in req.Archivos)
            {
                if (file.Length <= 0) continue;

                if (file.Length > MaxFileSizeBytes)
                    return BadRequest(new { message = $"El archivo '{file.FileName}' excede 50 MB." });

                var ext = Path.GetExtension(file.FileName);
                var uniqueName = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}{ext}";
                var filePath = Path.Combine(_uploadsRoot, uniqueName);

                await using (var stream = System.IO.File.Create(filePath))
                {
                    await file.CopyToAsync(stream);
                }

                saved.Add(new
                {
                    name = file.FileName,
                    size = file.Length,
                    contentType = file.ContentType,
                    url = $"/uploads/pedidos/{uniqueName}"
                });
            }

            if (saved.Count > 0)
                archivosJson = JsonSerializer.Serialize(saved);
        }

        var dto = await CreatePedidoInternal(
            req.Nombre,
            req.Telefono,
            req.Servicio,
            req.Detalles,
            req.FechaPref,
            req.Origen,
            archivosJson
        );

        return Ok(dto);
    }

    // PATCH /api/orders/{id}/status  (solo admin)
    [HttpPatch("{id:int}/status")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdateEstadoRequest req)
    {
        var pedido = await _db.Pedidos.FindAsync(id);
        if (pedido == null) return NotFound();

        var validos = new[] { "Pendiente", "Completado", "Cancelado" };
        if (!validos.Contains(req.Estado))
            return BadRequest(new { message = "Estado invalido." });

        pedido.Estado = req.Estado;
        await _db.SaveChangesAsync();
        return Ok(MapDto(pedido));
    }

    // DELETE /api/orders/{id}  (solo admin)
    [HttpDelete("{id:int}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Delete(int id)
    {
        var pedido = await _db.Pedidos.FindAsync(id);
        if (pedido == null) return NotFound();
        _db.Pedidos.Remove(pedido);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // PATCH /api/orders/{id}/admin  (solo admin)
    [HttpPatch("{id:int}/admin")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> UpdateAdminData(int id, [FromBody] UpdateAdminDataRequest req)
    {
        var pedido = await _db.Pedidos.FindAsync(id);
        if (pedido == null) return NotFound();

        var meta = EnsureMetaObject(pedido.ArchivosJson);

        meta["adminPrecio"] = string.IsNullOrWhiteSpace(req.Precio) ? null : req.Precio.Trim();
        meta["adminComentario"] = string.IsNullOrWhiteSpace(req.Comentario) ? null : req.Comentario.Trim();
        pedido.ArchivosJson = meta.ToJsonString();

        await _db.SaveChangesAsync();
        return Ok(MapDto(pedido));
    }

    // POST /api/orders/{id}/admin-files  (solo admin) - multipart con archivos para el estudiante
    [HttpPost("{id:int}/admin-files")]
    [Authorize(Roles = "admin")]
    [Consumes("multipart/form-data")]
    [RequestFormLimits(MultipartBodyLengthLimit = 262144000)] // 250 MB por solicitud
    public async Task<IActionResult> UploadAdminFiles(int id, [FromForm] List<IFormFile>? archivos)
    {
        var pedido = await _db.Pedidos.FindAsync(id);
        if (pedido == null) return NotFound();
        if (archivos is null || archivos.Count == 0)
            return BadRequest(new { message = "Debes adjuntar al menos un archivo." });

        var adminUploadsRoot = Path.Combine(_uploadsRoot, "admin");
        Directory.CreateDirectory(adminUploadsRoot);

        var meta = EnsureMetaObject(pedido.ArchivosJson);
        var adminFiles = meta["adminFiles"] as JsonArray ?? new JsonArray();

        foreach (var file in archivos)
        {
            if (file.Length <= 0) continue;

            if (file.Length > MaxFileSizeBytes)
                return BadRequest(new { message = $"El archivo '{file.FileName}' excede 50 MB." });

            var ext = Path.GetExtension(file.FileName);
            var uniqueName = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}{ext}";
            var filePath = Path.Combine(adminUploadsRoot, uniqueName);

            await using (var stream = System.IO.File.Create(filePath))
            {
                await file.CopyToAsync(stream);
            }

            adminFiles.Add(new JsonObject
            {
                ["name"] = file.FileName,
                ["size"] = file.Length,
                ["contentType"] = file.ContentType,
                ["url"] = $"/uploads/pedidos/admin/{uniqueName}",
                ["uploadedAt"] = DateTime.UtcNow.ToString("O")
            });
        }

        meta["adminFiles"] = adminFiles;
        pedido.ArchivosJson = meta.ToJsonString();
        await _db.SaveChangesAsync();
        return Ok(MapDto(pedido));
    }

    // PATCH /api/orders/{id}/user-response  (usuario duenio o admin)
    [HttpPatch("{id:int}/user-response")]
    [Authorize]
    public async Task<IActionResult> UpdateUserResponse(int id, [FromBody] UpdateUserResponseRequest req)
    {
        var pedido = await _db.Pedidos.FindAsync(id);
        if (pedido == null) return NotFound();

        var role = User.FindFirstValue(ClaimTypes.Role) ?? "";
        var isAdmin = role.Equals("admin", StringComparison.OrdinalIgnoreCase);
        int? userId = null;
        if (int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsedId))
            userId = parsedId;

        if (!isAdmin && (!userId.HasValue || pedido.UsuarioId != userId.Value))
            return Forbid();

        var decision = (req.Decision ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(decision) && decision != "Aceptado" && decision != "No aceptado")
            return BadRequest(new { message = "Decision invalida." });

        var meta = EnsureMetaObject(pedido.ArchivosJson);

        meta["userDecision"] = string.IsNullOrWhiteSpace(decision) ? null : decision;
        meta["userComentario"] = string.IsNullOrWhiteSpace(req.Comentario) ? null : req.Comentario.Trim();
        meta["userRespondedAt"] = DateTime.UtcNow.ToString("O");
        pedido.ArchivosJson = meta.ToJsonString();

        await _db.SaveChangesAsync();
        return Ok(MapDto(pedido));
    }

    private async Task<PedidoDto> CreatePedidoInternal(
        string nombre,
        string telefono,
        string servicio,
        string detalles,
        string? fechaPref,
        string? origen,
        string? archivosJson)
    {
        int? userId = null;
        if (User.Identity?.IsAuthenticated == true)
            userId = GetCurrentUserId();

        var pedido = new Pedido
        {
            Nombre = nombre.Trim(),
            Telefono = telefono.Trim(),
            Servicio = string.IsNullOrWhiteSpace(servicio) ? "Otro" : servicio.Trim(),
            Detalles = detalles?.Trim() ?? "",
            FechaPref = fechaPref,
            Origen = origen ?? "Web",
            ArchivosJson = archivosJson,
            UsuarioId = userId
        };

        _db.Pedidos.Add(pedido);
        await _db.SaveChangesAsync();
        return MapDto(pedido);
    }

    private static PedidoDto MapDto(Pedido p) => new(
        p.Id, p.Nombre, p.Telefono, p.Servicio, p.Detalles,
        p.FechaPref, p.Estado, p.Origen, p.CreadoEn, p.ArchivosJson, p.UsuarioId
    );

    private int? GetCurrentUserId()
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return null;

        return userId;
    }

    private static JsonObject EnsureMetaObject(string? rawJson)
    {
        JsonNode? parsed = null;
        if (!string.IsNullOrWhiteSpace(rawJson))
        {
            try { parsed = JsonNode.Parse(rawJson); } catch { parsed = null; }
        }

        if (parsed is JsonObject obj) return obj;
        if (parsed is JsonArray arr) return new JsonObject { ["files"] = arr };
        return new JsonObject { ["files"] = new JsonArray() };
    }
}
