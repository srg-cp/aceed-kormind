using System.Data.Entity.ModelConfiguration;
using pjtSPEF.Models.Entities;

namespace pjtSPEF.Data.Mappings
{
    public class PreguntaClaveMap : EntityTypeConfiguration<PreguntaClave>
    {
        public PreguntaClaveMap()
        {
            ToTable("preguntas_clave");
            HasKey(p => p.Id);
            Property(p => p.Id).HasColumnName("id");
            Property(p => p.ExamenBaseId).HasColumnName("examen_base_id");
            Property(p => p.Numero).HasColumnName("numero");
            Property(p => p.Enunciado).HasColumnName("enunciado").IsRequired();
            Property(p => p.RespuestaEsperada).HasColumnName("respuesta_esperada");
            Property(p => p.Puntaje).HasColumnName("puntaje").HasPrecision(5, 2);
            Property(p => p.FechaCreacion).HasColumnName("fecha_creacion");

            HasRequired(p => p.ExamenBase)
                .WithMany(e => e.PreguntasClave)
                .HasForeignKey(p => p.ExamenBaseId)
                .WillCascadeOnDelete(true);
        }
    }
}
