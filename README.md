# Consulta SQL Firebird

Aplicativo Windows para consultar e analisar dados em bancos Firebird, executar scripts SQL e facilitar a manutenção de registros.

## Atualizações automáticas

Ao iniciar, o aplicativo consulta a última versão publicada em [Releases](https://github.com/ThiagoEndrigo/ConsultaSQL/releases). Se houver uma versão mais recente, baixa o executável oficial, valida a versão do arquivo, substitui o programa e reinicia. Sem conexão com a internet, o aplicativo continua abrindo normalmente.

## Publicação de uma versão

**Autorização obrigatória:** nenhuma alteração, tag ou executável será enviado ao GitHub sem autorização explícita do usuário para aquela publicação. Autorizações anteriores não se aplicam a versões futuras. O código pode ser preparado, compilado e commitado localmente enquanto se aguarda essa autorização.

1. Atualize a versão nos atributos do assembly e nos textos de versão de `ConsultaSQL.cs`.
2. Compile localmente e confira a versão gerada.
3. **Somente depois da autorização explícita do usuário**, envie o código e a tag `vMAJOR.MINOR.PATCH` ou `vMAJOR.MINOR.PATCH.REVISION` ao GitHub.
4. O GitHub Actions compila o programa e publica `ConsultaSQL.exe` na Release. Esse nome é usado pelo atualizador.

O fluxo de publicação confirma que a versão do código corresponde à tag antes de criar a Release.

## Compilação local

No Windows com .NET Framework instalado, execute:

```powershell
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $csc /nologo /target:winexe /out:ConsultaSQL.exe /win32icon:ConsultaSQL.ico /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Data.dll /r:System.Core.dll /r:System.Web.Extensions.dll ConsultaSQL.cs ConsultaSQLUpdater.cs
```

O programa usa `isql.exe` para executar consultas, `gbak.exe` para backup e restauração, e `gfix.exe` para validação e manutenção de bancos. Em **Banco > Manutenção**, a validação é somente leitura. O **Assistente de recuperação** cria uma cópia de trabalho e conduz validação, reparo com `gfix -mend -ignore`, backup com `gbak -ignore` e restauração em um banco novo. Se o backup falhar por corrupção em um índice, **Resgate avançado** identifica a tabela, tenta remover o índice/restrição danificada apenas na cópia e repete o backup. No Firebird 3 ou mais recente, oferece ainda a opção de omitir os dados da tabela afetada se necessário. Feche todos os sistemas conectados antes de iniciar, para a cópia representar um estado consistente. O arquivo original não é alterado; confira os detalhes da execução e valide os dados do banco restaurado antes de voltar a usá-lo. Os arquivos do banco e os resultados exportados não fazem parte deste repositório.
