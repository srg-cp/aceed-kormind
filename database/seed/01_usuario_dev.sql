-- Usuario del modo desarrollo (auth simulada mientras no haya Google OAuth).
IF NOT EXISTS (SELECT 1 FROM dbo.usuarios WHERE email = N'dev@spef.local')
    INSERT INTO dbo.usuarios (email, nombre)
    VALUES (N'dev@spef.local', N'Docente (modo desarrollo)');
GO
