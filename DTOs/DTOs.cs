using Microsoft.AspNetCore.Http;

namespace CiberZone.API.DTOs;

// AUTH
public record LoginRequest(string Username, string Password);
public record TokenResponse(string Token, string Username, string Rol);

// USERS
public record CreateUserRequest(string Username, string Password);
public record UsuarioDto(int Id, string Username, string Rol, DateTime CreadoEn, bool Activo);

// ORDERS
public record CreatePedidoRequest(
    string Nombre,
    string Telefono,
    string Servicio,
    string Detalles,
    string? FechaPref,
    string? Origen,
    string? ArchivosJson
);

public class CreatePedidoFormRequest
{
    public string Nombre { get; set; } = "";
    public string Telefono { get; set; } = "";
    public string Servicio { get; set; } = "";
    public string Detalles { get; set; } = "";
    public string? FechaPref { get; set; }
    public string? Origen { get; set; }
    public List<IFormFile>? Archivos { get; set; }
}

public record UpdateEstadoRequest(string Estado);
public record UpdateAdminDataRequest(string? Precio, string? Comentario);
public record UpdateUserResponseRequest(string? Decision, string? Comentario);

public record PedidoDto(
    int Id,
    string Nombre,
    string Telefono,
    string Servicio,
    string Detalles,
    string? FechaPref,
    string Estado,
    string Origen,
    DateTime CreadoEn,
    string? ArchivosJson,
    int? UsuarioId
);
