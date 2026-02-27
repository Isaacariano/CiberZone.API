using Microsoft.EntityFrameworkCore;
using CiberZone.API.Models;

namespace CiberZone.API.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Pedido> Pedidos => Set<Pedido>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        // Usuario
        mb.Entity<Usuario>(e =>
        {
            e.HasKey(u => u.Id);
            e.HasIndex(u => u.Username).IsUnique();
            e.Property(u => u.Username).HasMaxLength(100).IsRequired();
            e.Property(u => u.PasswordHash).IsRequired();
            e.Property(u => u.Rol).HasMaxLength(20).HasDefaultValue("user");
        });

        // Pedido
        mb.Entity<Pedido>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Nombre).HasMaxLength(200).IsRequired();
            e.Property(p => p.Telefono).HasMaxLength(30).IsRequired();
            e.Property(p => p.Servicio).HasMaxLength(200).IsRequired();
            e.Property(p => p.Estado).HasMaxLength(30).HasDefaultValue("Pendiente");
            e.Property(p => p.Origen).HasMaxLength(50).HasDefaultValue("Web");
            e.HasOne(p => p.Usuario)
             .WithMany(u => u.Pedidos)
             .HasForeignKey(p => p.UsuarioId)
             .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
