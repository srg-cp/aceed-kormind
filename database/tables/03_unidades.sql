-- Unidades dentro de un curso (Unidad I, Unidad II, ...).
CREATE TABLE dbo.unidades (
    id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_unidades PRIMARY KEY,
    curso_id        INT            NOT NULL CONSTRAINT FK_unidades_curso REFERENCES dbo.cursos(id) ON DELETE CASCADE,
    numero          INT            NOT NULL,
    nombre          NVARCHAR(200)  NOT NULL,
    fecha_creacion  DATETIME2(0)   NOT NULL CONSTRAINT DF_unidades_fecha DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_unidades_curso_numero UNIQUE (curso_id, numero)
);
GO
