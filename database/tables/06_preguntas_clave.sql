-- Clave de corrección del examen base: una fila por pregunta.
-- respuesta_esperada guarda la respuesta correcta o los criterios de corrección
-- que se le darán a Gemini al calificar las entregas de los alumnos.
CREATE TABLE dbo.preguntas_clave (
    id                 INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_preguntas_clave PRIMARY KEY,
    examen_base_id     INT            NOT NULL CONSTRAINT FK_preguntas_clave_examen REFERENCES dbo.examenes_base(id) ON DELETE CASCADE,
    numero             INT            NOT NULL,
    enunciado          NVARCHAR(MAX)  NOT NULL,
    respuesta_esperada NVARCHAR(MAX)  NULL,
    puntaje            DECIMAL(5,2)   NOT NULL CONSTRAINT CK_preguntas_clave_puntaje CHECK (puntaje > 0),
    fecha_creacion     DATETIME2(0)   NOT NULL CONSTRAINT DF_preguntas_clave_fecha DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_preguntas_clave_examen_numero UNIQUE (examen_base_id, numero)
);
GO
