using System.Data.Entity.ModelConfiguration;
using pjtSPEF.Models.Entities;

namespace pjtSPEF.Data.Mappings
{
    public class RespuestaEstudianteMap : EntityTypeConfiguration<RespuestaEstudiante>
    {
        public RespuestaEstudianteMap()
        {
            ToTable("respuestas_estudiante");
            HasKey(r => r.Id);
            Property(r => r.Id).HasColumnName("id");
            Property(r => r.EvaluacionEstudianteId).HasColumnName("evaluacion_estudiante_id");
            Property(r => r.Numero).HasColumnName("numero");
            Property(r => r.Enunciado).HasColumnName("enunciado").IsRequired();
            Property(r => r.RespuestaTexto).HasColumnName("respuesta_texto");
            Property(r => r.PuntajeMaximo).HasColumnName("puntaje_maximo").HasPrecision(5, 2);
            Property(r => r.PuntajeObtenido).HasColumnName("puntaje_obtenido").HasPrecision(5, 2);
            Property(r => r.Comentario).HasColumnName("comentario");
            Property(r => r.FechaCreacion).HasColumnName("fecha_creacion");

            HasRequired(r => r.EvaluacionEstudiante)
                .WithMany(e => e.Respuestas)
                .HasForeignKey(r => r.EvaluacionEstudianteId)
                .WillCascadeOnDelete(true);
        }
    }
}
