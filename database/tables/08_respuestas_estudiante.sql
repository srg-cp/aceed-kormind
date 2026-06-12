-- Detalle por pregunta de la calificación de una entrega.
-- enunciado y puntaje_maximo son copias (snapshot) de la clave al momento de calificar,
-- para que el detalle siga siendo legible aunque luego se recalibre la clave del examen.
CREATE TABLE dbo.respuestas_estudiante (
    id                        INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_respuestas_estudiante PRIMARY KEY,
    evaluacion_estudiante_id  INT            NOT NULL CONSTRAINT FK_respuestas_estudiante_evaluacion REFERENCES dbo.evaluaciones_estudiante(id) ON DELETE CASCADE,
    numero                    INT            NOT NULL,
    enunciado                 NVARCHAR(MAX)  NOT NULL,
    respuesta_texto           NVARCHAR(MAX)  NULL,
    puntaje_maximo            DECIMAL(5,2)   NOT NULL,
    puntaje_obtenido          DECIMAL(5,2)   NOT NULL,
    comentario                NVARCHAR(MAX)  NULL,
    fecha_creacion            DATETIME2(0)   NOT NULL CONSTRAINT DF_respuestas_estudiante_fecha DEFAULT SYSUTCDATETIME()
);
GO
CREATE INDEX IX_respuestas_estudiante_evaluacion ON dbo.respuestas_estudiante(evaluacion_estudiante_id);
GO
