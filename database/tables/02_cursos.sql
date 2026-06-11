-- Cursos del docente. Sin cascade desde usuarios: un docente con cursos no se elimina.
CREATE TABLE dbo.cursos (
    id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_cursos PRIMARY KEY,
    usuario_id      INT            NOT NULL CONSTRAINT FK_cursos_usuario REFERENCES dbo.usuarios(id),
    nombre          NVARCHAR(200)  NOT NULL,
    periodo         NVARCHAR(50)   NULL,  -- p. ej. "2026-I"
    fecha_creacion  DATETIME2(0)   NOT NULL CONSTRAINT DF_cursos_fecha DEFAULT SYSUTCDATETIME()
);
GO
CREATE INDEX IX_cursos_usuario_id ON dbo.cursos(usuario_id);
GO
