using System.Data.Entity.ModelConfiguration;
using pjtSPEF.Models.Entities;

namespace pjtSPEF.Data.Mappings
{
    public class CursoMap : EntityTypeConfiguration<Curso>
    {
        public CursoMap()
        {
            ToTable("cursos");
            HasKey(c => c.Id);
            Property(c => c.Id).HasColumnName("id");
            Property(c => c.UsuarioId).HasColumnName("usuario_id");
            Property(c => c.Nombre).HasColumnName("nombre").IsRequired().HasMaxLength(200);
            Property(c => c.Periodo).HasColumnName("periodo").HasMaxLength(50);
            Property(c => c.FechaCreacion).HasColumnName("fecha_creacion");

            HasRequired(c => c.Usuario)
                .WithMany(u => u.Cursos)
                .HasForeignKey(c => c.UsuarioId)
                .WillCascadeOnDelete(false);
        }
    }
}
