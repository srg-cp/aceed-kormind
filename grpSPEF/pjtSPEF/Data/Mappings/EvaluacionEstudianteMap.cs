using System.Data.Entity.ModelConfiguration;
using pjtSPEF.Models.Entities;

namespace pjtSPEF.Data.Mappings
{
    public class EvaluacionEstudianteMap : EntityTypeConfiguration<EvaluacionEstudiante>
    {
        public EvaluacionEstudianteMap()
        {
            ToTable("evaluaciones_estudiante");
            HasKey(e => e.Id);
            Property(e => e.Id).HasColumnName("id");
            Property(e => e.ExamenBaseId).HasColumnName("examen_base_id");
            Property(e => e.NombreEstudiante).HasColumnName("nombre_estudiante").HasMaxLength(200);
            Property(e => e.ArchivoRef).HasColumnName("archivo_ref").HasMaxLength(400);
            Property(e => e.ArchivoNombreOriginal).HasColumnName("archivo_nombre_original").HasMaxLength(255);
            Property(e => e.TotalPaginas).HasColumnName("total_paginas");
            Property(e => e.NotaTotal).HasColumnName("nota_total").HasPrecision(6, 2);
            Property(e => e.Estado).HasColumnName("estado");
            Property(e => e.MensajeError).HasColumnName("mensaje_error");
            Property(e => e.FechaCreacion).HasColumnName("fecha_creacion");
            Property(e => e.FechaCalificacion).HasColumnName("fecha_calificacion");

            HasRequired(e => e.ExamenBase)
                .WithMany(b => b.Evaluaciones)
                .HasForeignKey(e => e.ExamenBaseId)
                .WillCascadeOnDelete(true);
        }
    }
}
