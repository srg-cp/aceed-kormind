/* ============================================================
   SPEF — Script consolidado de base de datos
   Crea la base de datos SPEF completa. Es idempotente: se puede
   ejecutar más de una vez sin romper nada (solo crea lo que falta).

   Ejecutar con SSMS o:
     sqlcmd -S "(localdb)\MSSQLLocalDB" -i database\script.sql -b -I

   Última actualización: 2026-06-11 (Incremento 1)
   ============================================================ */

-- Requerido por el índice filtrado de usuarios (sqlcmd sin -I lo desactiva)
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF DB_ID(N'SPEF') IS NULL
    CREATE DATABASE SPEF;
GO

USE SPEF;
GO

/* ---------- usuarios ---------- */
-- Docentes del sistema. google_id y refresh_token quedan NULL hasta integrar Google OAuth.
IF OBJECT_ID(N'dbo.usuarios', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.usuarios (
        id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_usuarios PRIMARY KEY,
        google_id       NVARCHAR(64)   NULL,
        email           NVARCHAR(256)  NOT NULL CONSTRAINT UQ_usuarios_email UNIQUE,
        nombre          NVARCHAR(200)  NOT NULL,
        refresh_token   VARBINARY(MAX) NULL,  -- cifrado (DPAPI) cuando llegue OAuth
        fecha_creacion  DATETIME2(0)   NOT NULL CONSTRAINT DF_usuarios_fecha DEFAULT SYSUTCDATETIME(),
        activo          BIT            NOT NULL CONSTRAINT DF_usuarios_activo DEFAULT 1
    );
    CREATE UNIQUE INDEX UX_usuarios_google_id ON dbo.usuarios(google_id) WHERE google_id IS NOT NULL;
END
GO

/* ---------- cursos ---------- */
-- Cursos del docente. Sin cascade desde usuarios: un docente con cursos no se elimina.
IF OBJECT_ID(N'dbo.cursos', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.cursos (
        id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_cursos PRIMARY KEY,
        usuario_id      INT            NOT NULL CONSTRAINT FK_cursos_usuario REFERENCES dbo.usuarios(id),
        nombre          NVARCHAR(200)  NOT NULL,
        periodo         NVARCHAR(50)   NULL,  -- p. ej. "2026-I"
        fecha_creacion  DATETIME2(0)   NOT NULL CONSTRAINT DF_cursos_fecha DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_cursos_usuario_id ON dbo.cursos(usuario_id);
END
GO

/* ---------- unidades ---------- */
-- Unidades dentro de un curso (Unidad I, Unidad II, ...).
IF OBJECT_ID(N'dbo.unidades', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.unidades (
        id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_unidades PRIMARY KEY,
        curso_id        INT            NOT NULL CONSTRAINT FK_unidades_curso REFERENCES dbo.cursos(id) ON DELETE CASCADE,
        numero          INT            NOT NULL,
        nombre          NVARCHAR(200)  NOT NULL,
        fecha_creacion  DATETIME2(0)   NOT NULL CONSTRAINT DF_unidades_fecha DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_unidades_curso_numero UNIQUE (curso_id, numero)
    );
END
GO

/* ---------- tipos_evaluacion ---------- */
-- Tipo de evaluación dentro de una unidad (Examen teórico, Examen parcial, Práctica calificada, ...).
IF OBJECT_ID(N'dbo.tipos_evaluacion', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tipos_evaluacion (
        id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tipos_evaluacion PRIMARY KEY,
        unidad_id       INT            NOT NULL CONSTRAINT FK_tipos_evaluacion_unidad REFERENCES dbo.unidades(id) ON DELETE CASCADE,
        nombre          NVARCHAR(200)  NOT NULL,
        fecha_creacion  DATETIME2(0)   NOT NULL CONSTRAINT DF_tipos_evaluacion_fecha DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_tipos_evaluacion_unidad_id ON dbo.tipos_evaluacion(unidad_id);
END
GO

/* ---------- examenes_base ---------- */
-- Examen base (la clave con respuestas). archivo_ref es una referencia genérica:
-- hoy es una ruta relativa en App_Data/storage; cuando llegue Google Drive será el DriveFileId.
-- estado: 0=Borrador, 1=Calibrado, 2=Activo
IF OBJECT_ID(N'dbo.examenes_base', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.examenes_base (
        id                      INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_examenes_base PRIMARY KEY,
        tipo_evaluacion_id      INT            NOT NULL CONSTRAINT FK_examenes_base_tipo REFERENCES dbo.tipos_evaluacion(id) ON DELETE CASCADE,
        titulo                  NVARCHAR(200)  NOT NULL,
        archivo_ref             NVARCHAR(400)  NULL,
        archivo_nombre_original NVARCHAR(255)  NULL,
        total_paginas           INT            NULL CONSTRAINT CK_examenes_base_paginas CHECK (total_paginas BETWEEN 1 AND 10),
        nota_maxima             DECIMAL(5,2)   NOT NULL CONSTRAINT DF_examenes_base_nota DEFAULT 20
                                               CONSTRAINT CK_examenes_base_nota CHECK (nota_maxima > 0),
        estado                  TINYINT        NOT NULL CONSTRAINT DF_examenes_base_estado DEFAULT 0,
        fecha_creacion          DATETIME2(0)   NOT NULL CONSTRAINT DF_examenes_base_fecha DEFAULT SYSUTCDATETIME(),
        fecha_modificacion      DATETIME2(0)   NULL
    );
    CREATE INDEX IX_examenes_base_tipo ON dbo.examenes_base(tipo_evaluacion_id);
END
GO

/* ---------- seed: usuario del modo desarrollo ---------- */
IF NOT EXISTS (SELECT 1 FROM dbo.usuarios WHERE email = N'dev@spef.local')
    INSERT INTO dbo.usuarios (email, nombre)
    VALUES (N'dev@spef.local', N'Docente (modo desarrollo)');
GO
