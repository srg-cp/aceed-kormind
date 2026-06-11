using System.Data.Entity.ModelConfiguration;
using pjtSPEF.Models.Entities;

namespace pjtSPEF.Data.Mappings
{
    public class UsuarioMap : EntityTypeConfiguration<Usuario>
    {
        public UsuarioMap()
        {
            ToTable("usuarios");
            HasKey(u => u.Id);
            Property(u => u.Id).HasColumnName("id");
            Property(u => u.GoogleId).HasColumnName("google_id").HasMaxLength(64);
            Property(u => u.Email).HasColumnName("email").IsRequired().HasMaxLength(256);
            Property(u => u.Nombre).HasColumnName("nombre").IsRequired().HasMaxLength(200);
            Property(u => u.RefreshToken).HasColumnName("refresh_token");
            Property(u => u.FechaCreacion).HasColumnName("fecha_creacion");
            Property(u => u.Activo).HasColumnName("activo");
        }
    }
}
