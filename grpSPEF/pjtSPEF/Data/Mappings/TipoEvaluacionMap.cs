using System.Data.Entity.ModelConfiguration;
using pjtSPEF.Models.Entities;

namespace pjtSPEF.Data.Mappings
{
    public class TipoEvaluacionMap : EntityTypeConfiguration<TipoEvaluacion>
    {
        public TipoEvaluacionMap()
        {
            ToTable("tipos_evaluacion");
            HasKey(t => t.Id);
            Property(t => t.Id).HasColumnName("id");
            Property(t => t.UnidadId).HasColumnName("unidad_id");
            Property(t => t.Nombre).HasColumnName("nombre").IsRequired().HasMaxLength(200);
            Property(t => t.FechaCreacion).HasColumnName("fecha_creacion");

            HasRequired(t => t.Unidad)
                .WithMany(u => u.TiposEvaluacion)
                .HasForeignKey(t => t.UnidadId)
                .WillCascadeOnDelete(true);
        }
    }
}
