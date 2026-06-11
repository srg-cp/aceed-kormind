using System.Data.Entity.ModelConfiguration;
using pjtSPEF.Models.Entities;

namespace pjtSPEF.Data.Mappings
{
    public class ExamenBaseMap : EntityTypeConfiguration<ExamenBase>
    {
        public ExamenBaseMap()
        {
            ToTable("examenes_base");
            HasKey(e => e.Id);
            Property(e => e.Id).HasColumnName("id");
            Property(e => e.TipoEvaluacionId).HasColumnName("tipo_evaluacion_id");
            Property(e => e.Titulo).HasColumnName("titulo").IsRequired().HasMaxLength(200);
            Property(e => e.ArchivoRef).HasColumnName("archivo_ref").HasMaxLength(400);
            Property(e => e.ArchivoNombreOriginal).HasColumnName("archivo_nombre_original").HasMaxLength(255);
            Property(e => e.TotalPaginas).HasColumnName("total_paginas");
            Property(e => e.NotaMaxima).HasColumnName("nota_maxima").HasPrecision(5, 2);
            Property(e => e.Estado).HasColumnName("estado");
            Property(e => e.FechaCreacion).HasColumnName("fecha_creacion");
            Property(e => e.FechaModificacion).HasColumnName("fecha_modificacion");

            HasRequired(e => e.TipoEvaluacion)
                .WithMany(t => t.ExamenesBase)
                .HasForeignKey(e => e.TipoEvaluacionId)
                .WillCascadeOnDelete(true);
        }
    }
}
