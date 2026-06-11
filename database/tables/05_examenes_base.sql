-- Examen base (la clave con respuestas). archivo_ref es una referencia genérica:
-- hoy es una ruta relativa en App_Data/storage; cuando llegue Google Drive será el DriveFileId.
-- estado: 0=Borrador, 1=Calibrado, 2=Activo
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
GO
CREATE INDEX IX_examenes_base_tipo ON dbo.examenes_base(tipo_evaluacion_id);
GO
