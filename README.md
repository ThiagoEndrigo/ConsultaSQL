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
& $csc /nologo /target:winexe /out:ConsultaSQL.exe /win32icon:ConsultaSQL.ico /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Data.dll /r:System.Core.dll /r:System.Web.Extensions.dll /r:System.Web.dll ConsultaSQL.cs ConsultaSQLUpdater.cs RecoverySafety.cs
```

## Recuperação de bancos — implementação em andamento

O assistente em Banco > Manutenção > Recuperar banco corrompido exige que o operador confirme uma origem offline de arquivo único. A cópia bloqueia processos Firebird conhecidos ativos e arquivos associados reconhecidos pelo nome; mantém o arquivo aberto com acesso exclusivo durante leitura, cálculo SHA-256 e cópia. Após conferir os hashes da origem e da cópia, pede que o operador inicie o servidor para processar somente a cópia. Não encerra nem inicia serviços automaticamente.

Esses controles ainda NÃO comprovam sozinhos a consistência da origem: engines incorporados em outros processos, guardians/serviços que reiniciam o servidor e arquivos associados com nomes arbitrários ainda exigem tratamento. A Fase 1 do plano permanece incompleta. Não utilizar esta versão como ferramenta de recuperação validada para produção.

Cada execução recebe diretório exclusivo com work, backup, staging, resultado, logs e relatorios. O manifesto JSON registra eventos, versão, hashes disponíveis e estado; sua atualização usa arquivo temporário e substituição atômica. A falha de gravação impede a continuação. Ao terminar o bloco de recuperação, o programa tenta conferir novamente o hash do original e grava diagnóstico e relatório HTML, inclusive em falhas. Um hash final indisponível é registrado; divergências também são registradas. O diretório resultado permanece vazio: não há promoção automática de arquivos em staging.

O backup conservador precede a oferta de mend em segunda cópia, com confirmação. As validações anterior e posterior são apresentadas sem prometer uma quantidade exata de perdas. O caminho automático que removia/recriava constraints após falha do backup foi desativado. O restore usa índices inativos, tenta reativação individual e executa gfix somente leitura depois. O resultado é apresentado com pendências e completude não comprovada.

Ainda faltam: validação de cabeçalho/ODS, versões das ferramentas, inventário comprovado de arquivos associados, controle de serviços, estimativa de espaço, runner central com cancelamento/timeouts, proteção reforçada de credenciais, manifesto completo por comando, retomada, contagens, comparação de metadados e integridade lógica, recuperação por tabela e o corpus CT001–CT022. O mecanismo existente de credenciais usa -fetch_password com arquivo temporário e precisa ser reforçado.

Compilação local: aprovada com C# 5/.NET Framework, incluindo RecoverySafety.cs. Testes executados nesta entrega: SHA-256 conhecido; cópia bloqueada com fb_inet_server ativo e destino não criado; manifesto JSON e remoção do temporário após atualização; escape HTML do relatório; preservação dos bytes de uma origem artificial com caminho acentuado. Cinco verificações aprovadas. A primeira execução no sandbox foi bloqueada por permissão em File.Replace; repetida com permissão de execução, passou. Esses testes não demonstram uma cópia offline bem-sucedida nem recuperação de corrupção. Nenhuma nova tentativa no GOURMET real foi executada.