-- Entrega de un estudiante para un examen base: el PDF resuelto del alumno.
-- archivo_ref sigue la misma convención que examenes_base (hoy DriveFileId).
-- nota_total es la suma de los puntajes obtenidos; NULL mientras no se califica.
-- estado: 0=Pendiente, 1=Calificada, 2=Error
CREATE TABLE dbo.evaluaciones_estudiante (
    id                      INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_evaluaciones_estudiante PRIMARY KEY,
    examen_base_id          INT            NOT NULL CONSTRAINT FK_evaluaciones_estudiante_examen REFERENCES dbo.examenes_base(id) ON DELETE CASCADE,
    nombre_estudiante       NVARCHAR(200)  NULL,
    archivo_ref             NVARCHAR(400)  NULL,
    archivo_nombre_original NVARCHAR(255)  NULL,
    total_paginas           INT            NULL CONSTRAINT CK_evaluaciones_estudiante_paginas CHECK (total_paginas BETWEEN 1 AND 10),
    nota_total              DECIMAL(6,2)   NULL,
    estado                  TINYINT        NOT NULL CONSTRAINT DF_evaluaciones_estudiante_estado DEFAULT 0,
    mensaje_error           NVARCHAR(MAX)  NULL,
    fecha_creacion          DATETIME2(0)   NOT NULL CONSTRAINT DF_evaluaciones_estudiante_fecha DEFAULT SYSUTCDATETIME(),
    fecha_calificacion      DATETIME2(0)   NULL
);
GO
CREATE INDEX IX_evaluaciones_estudiante_examen ON dbo.evaluaciones_estudiante(examen_base_id);
GO
