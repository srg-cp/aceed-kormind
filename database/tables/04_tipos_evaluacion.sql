-- Tipo de evaluación dentro de una unidad (Examen teórico, Examen parcial, Práctica calificada, ...).
CREATE TABLE dbo.tipos_evaluacion (
    id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tipos_evaluacion PRIMARY KEY,
    unidad_id       INT            NOT NULL CONSTRAINT FK_tipos_evaluacion_unidad REFERENCES dbo.unidades(id) ON DELETE CASCADE,
    nombre          NVARCHAR(200)  NOT NULL,
    fecha_creacion  DATETIME2(0)   NOT NULL CONSTRAINT DF_tipos_evaluacion_fecha DEFAULT SYSUTCDATETIME()
);
GO
CREATE INDEX IX_tipos_evaluacion_unidad_id ON dbo.tipos_evaluacion(unidad_id);
GO
