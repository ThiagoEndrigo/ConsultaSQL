# Consulta SQL Firebird

Aplicativo Windows para consultar e analisar dados em bancos Firebird, executar scripts SQL e facilitar a manutenção de registros.

## Atualizações automáticas

Ao iniciar, o aplicativo consulta a última versão publicada em [Releases](https://github.com/ThiagoEndrigo/ConsultaSQL/releases). Se houver uma versão mais recente, baixa o executável oficial, valida a versão do arquivo, substitui o programa e reinicia. Sem conexão com a internet, o aplicativo continua abrindo normalmente.

## Publicação de uma versão

1. Atualize a versão nos atributos do assembly e nos textos de versão de `ConsultaSQL.cs`.
2. Faça commit das alterações.
3. Crie e envie uma tag no formato `vMAJOR.MINOR.PATCH`, como `v0.0.40`.
4. O GitHub Actions compila o programa e publica `ConsultaSQL.exe` na Release. Esse nome é usado pelo atualizador.

O fluxo de publicação confirma que a versão do código corresponde à tag antes de criar a Release.

## Compilação local

No Windows com .NET Framework instalado, execute:

```powershell
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $csc /nologo /target:winexe /out:ConsultaSQL.exe /win32icon:ConsultaSQL.ico /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Data.dll /r:System.Core.dll /r:System.Web.Extensions.dll ConsultaSQL.cs ConsultaSQLUpdater.cs
```

O programa usa `isql.exe` para executar consultas e `gbak.exe` para backup e restauração. Os arquivos do banco e os resultados exportados não fazem parte deste repositório.