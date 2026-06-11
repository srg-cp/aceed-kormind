using System.Data.Entity.ModelConfiguration;
using pjtSPEF.Models.Entities;

namespace pjtSPEF.Data.Mappings
{
    public class UnidadMap : EntityTypeConfiguration<Unidad>
    {
        public UnidadMap()
        {
            ToTable("unidades");
            HasKey(u => u.Id);
            Property(u => u.Id).HasColumnName("id");
            Property(u => u.CursoId).HasColumnName("curso_id");
            Property(u => u.Numero).HasColumnName("numero");
            Property(u => u.Nombre).HasColumnName("nombre").IsRequired().HasMaxLength(200);
            Property(u => u.FechaCreacion).HasColumnName("fecha_creacion");

            HasRequired(u => u.Curso)
                .WithMany(c => c.Unidades)
                .HasForeignKey(u => u.CursoId)
                .WillCascadeOnDelete(true);
        }
    }
}
