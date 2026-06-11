-- Requerido por el índice filtrado (sqlcmd sin -I lo desactiva)
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- Docentes del sistema. google_id y refresh_token quedan NULL hasta integrar Google OAuth.
CREATE TABLE dbo.usuarios (
    id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_usuarios PRIMARY KEY,
    google_id       NVARCHAR(64)   NULL,
    email           NVARCHAR(256)  NOT NULL CONSTRAINT UQ_usuarios_email UNIQUE,
    nombre          NVARCHAR(200)  NOT NULL,
    refresh_token   VARBINARY(MAX) NULL,  -- cifrado (DPAPI) cuando llegue OAuth
    fecha_creacion  DATETIME2(0)   NOT NULL CONSTRAINT DF_usuarios_fecha DEFAULT SYSUTCDATETIME(),
    activo          BIT            NOT NULL CONSTRAINT DF_usuarios_activo DEFAULT 1
);
GO
CREATE UNIQUE INDEX UX_usuarios_google_id ON dbo.usuarios(google_id) WHERE google_id IS NOT NULL;
GO
