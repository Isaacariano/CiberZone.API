namespace CiberZone.API.Models;

public class Usuario
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Rol { get; set; } = "user"; // "admin" | "user"
    public DateTime CreadoEn { get; set; } = DateTime.UtcNow;
    public bool Activo { get; set; } = true;

    // Navigation
    public List<Pedido> Pedidos { get; set; } = new();
}

public class Pedido
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public string Telefono { get; set; } = "";
    public string Servicio { get; set; } = "";
    public string Detalles { get; set; } = "";
    public string? FechaPref { get; set; }
    public string Estado { get; set; } = "Pendiente"; // "Pendiente" | "Completado" | "Cancelado"
    public string Origen { get; set; } = "Web";
    public DateTime CreadoEn { get; set; } = DateTime.UtcNow;
    public string? ArchivosJson { get; set; } // JSON array de archivos base64

    // Optional FK to user
    public int? UsuarioId { get; set; }
    public Usuario? Usuario { get; set; }
}
