using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;

internal sealed class UpdateCandidate {
    public string Tag;
    public string DownloadPath;
    public string ReleaseUrl;
}

internal static class ConsultaSQLUpdater {
    const string ApiUrl="https://api.github.com/repos/ThiagoEndrigo/ConsultaSQL/releases/latest";
    const string AssetBase="https://github.com/ThiagoEndrigo/ConsultaSQL/releases/download/";
    static readonly JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=4*1024*1024};

    public static string LastDiagnostic="";

    public static UpdateCandidate CheckForUpdate() {
        string downloadPath=null;
        LastDiagnostic="";
        try {
            ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
            string json=GetText(ApiUrl,20000);
            Dictionary<string,object> release=Json.Deserialize<Dictionary<string,object>>(json);
            if(release==null||!release.ContainsKey("tag_name")||!release.ContainsKey("assets")){LastDiagnostic="O GitHub não retornou os dados esperados da versão mais recente.";return null;}
            string tag=Convert.ToString(release["tag_name"],CultureInfo.InvariantCulture);
            Version newest;if(String.IsNullOrWhiteSpace(tag)||!Version.TryParse(tag.TrimStart('v','V'),out newest)){LastDiagnostic="A versão informada pelo GitHub não pôde ser interpretada.";return null;}
            Version current=typeof(Program).Assembly.GetName().Version;
            if(newest<=current){LastDiagnostic="Nenhuma atualização disponível. Versão em uso: "+Program.VersionDisplay+".";return null;}
            object[] assets=release["assets"] as object[];
            if(assets==null) { ArrayList list=release["assets"] as ArrayList;if(list!=null)assets=list.ToArray(); }
            if(assets==null){LastDiagnostic="A release "+tag+" não contém uma lista válida de arquivos.";return null;}
            string url="";
            foreach(object item in assets) {
                Dictionary<string,object> asset=item as Dictionary<string,object>;
                if(asset==null||!asset.ContainsKey("name")||!asset.ContainsKey("browser_download_url"))continue;
                if(String.Equals(Convert.ToString(asset["name"],CultureInfo.InvariantCulture),"ConsultaSQL.exe",StringComparison.OrdinalIgnoreCase)){url=Convert.ToString(asset["browser_download_url"],CultureInfo.InvariantCulture);break;}
            }
            Uri downloadUri;if(String.IsNullOrWhiteSpace(url)||!Uri.TryCreate(url,UriKind.Absolute,out downloadUri)||downloadUri.Scheme!=Uri.UriSchemeHttps||!String.Equals(downloadUri.Host,"github.com",StringComparison.OrdinalIgnoreCase)||!downloadUri.AbsolutePath.StartsWith("/ThiagoEndrigo/ConsultaSQL/releases/download/",StringComparison.OrdinalIgnoreCase)){LastDiagnostic="A release "+tag+" não possui um executável ConsultaSQL.exe válido.";return null;}
            string folder=Path.Combine(Path.GetTempPath(),"ConsultaSQL-Atualizacao");Directory.CreateDirectory(folder);
            downloadPath=Path.Combine(folder,"ConsultaSQL-"+newest.ToString()+"-"+Guid.NewGuid().ToString("N")+".exe");
            DownloadFile(downloadUri.AbsoluteUri,downloadPath,120000);
            FileInfo info=new FileInfo(downloadPath);if(info.Length<65536||info.Length>50L*1024L*1024L){TryDelete(downloadPath);downloadPath=null;LastDiagnostic="O executável baixado está fora do tamanho esperado.";return null;}
            using(FileStream file=File.OpenRead(downloadPath)){if(file.ReadByte()!='M'||file.ReadByte()!='Z'){TryDelete(downloadPath);downloadPath=null;LastDiagnostic="O arquivo baixado não parece ser um executável válido do Windows.";return null;}}
            string productVersion=FileVersionInfo.GetVersionInfo(downloadPath).ProductVersion??"";Version binaryVersion;
            if(!Version.TryParse(productVersion.Trim().TrimStart('v','V'),out binaryVersion)||binaryVersion!=newest){TryDelete(downloadPath);downloadPath=null;LastDiagnostic="A versão do executável baixado não corresponde à versão publicada ("+tag+").";return null;}
            LastDiagnostic="Nova versão "+tag+" encontrada.";
            return new UpdateCandidate{Tag=tag,DownloadPath=downloadPath,ReleaseUrl=url};
        } catch(Exception ex) {
            if(!String.IsNullOrWhiteSpace(downloadPath))TryDelete(downloadPath);
            LastDiagnostic="Não foi possível verificar a atualização. Confira a internet ou o acesso ao GitHub. Detalhe: "+ex.GetType().Name+": "+ex.Message;
            return null;
        }
    }
    static string GetText(string url,int timeout) {
        HttpWebRequest request=(HttpWebRequest)WebRequest.Create(url);request.Method="GET";request.UserAgent="ConsultaSQL-Firebird-Updater";request.Accept="application/vnd.github+json";request.Timeout=timeout;request.ReadWriteTimeout=timeout;request.Headers[HttpRequestHeader.CacheControl]="no-cache";
        using(HttpWebResponse response=(HttpWebResponse)request.GetResponse())using(Stream stream=response.GetResponseStream())using(StreamReader reader=new StreamReader(stream,Encoding.UTF8)){return reader.ReadToEnd();}
    }

    static void DownloadFile(string url,string path,int timeout) {
        HttpWebRequest request=(HttpWebRequest)WebRequest.Create(url);request.Method="GET";request.UserAgent="ConsultaSQL-Firebird-Updater";request.Timeout=timeout;request.ReadWriteTimeout=timeout;request.AllowAutoRedirect=true;
        using(HttpWebResponse response=(HttpWebResponse)request.GetResponse()) {
            if(response.ContentLength>50L*1024L*1024L)throw new InvalidDataException("O arquivo da atualização é maior que o limite permitido.");
            using(Stream input=response.GetResponseStream())using(FileStream output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None)){input.CopyTo(output);}
        }
    }

    public static void Start(UpdateCandidate update,int processId,string[] arguments) {
        if(update==null||String.IsNullOrWhiteSpace(update.DownloadPath)||!File.Exists(update.DownloadPath))throw new InvalidOperationException("O arquivo da atualização não está disponível.");
        string scriptPath=Path.Combine(Path.GetTempPath(),"ConsultaSQL-Atualizar-"+Guid.NewGuid().ToString("N")+".ps1");
        string target=ApplicationPath();
        string target64=Base64(target),download64=Base64(update.DownloadPath),args64=Base64(Json.Serialize(arguments??new string[0]));
        string script=@"param([int]$ProcessId,[string]$Target64,[string]$Download64,[string]$Args64)
$ErrorActionPreference = 'Stop'
$target = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($Target64))
$download = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($Download64))
$argsJson = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($Args64))
$appArgs = @((ConvertFrom-Json -InputObject $argsJson))
$quoted = @()
$dq = [char]34
foreach ($arg in $appArgs) { $quoted += ($dq + ([string]$arg).Replace([string]$dq,([string][char]92 + $dq)) + $dq) }
$argLine = [string]::Join(' ', $quoted)
$workDir = Split-Path -Parent $target
$scriptPath = $MyInvocation.MyCommand.Path
$backup = $target + '.previous'
$staged = $target + '.update'
$installed = $false
$lastInstallError = 'Nenhum detalhe adicional foi informado pelo Windows.'
$newProcess = $null
$retryStarted = $false
try {
    $oldProcess = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
    if ($null -ne $oldProcess) {
        $oldProcess.WaitForExit(120000)
        $oldProcess.Refresh()
        if (-not $oldProcess.HasExited) { throw 'O programa anterior ainda está em execução. Feche-o e tente novamente.' }
    }
    Start-Sleep -Milliseconds 1000

    if (-not (Test-Path -LiteralPath $target)) { throw 'O executável instalado não foi encontrado.' }
    if (Test-Path -LiteralPath $backup) { Remove-Item -LiteralPath $backup -Force }
    if (Test-Path -LiteralPath $staged) { Remove-Item -LiteralPath $staged -Force }
    $sourceHash = (Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash
    $backupReady = $false
    for ($i=0; $i -lt 30; $i++) {
        try {
            if (Test-Path -LiteralPath $staged) { Remove-Item -LiteralPath $staged -Force }
            Copy-Item -LiteralPath $download -Destination $staged -Force
            $stagedHash = (Get-FileHash -LiteralPath $staged -Algorithm SHA256).Hash
            if ($sourceHash -ne $stagedHash) { throw 'A cópia temporária da atualização não corresponde ao arquivo baixado.' }

            if (Test-Path -LiteralPath $target) {
                $existingHash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
                if ($existingHash -eq $sourceHash) { $installed = $true; break }
                if (-not $backupReady) {
                    if (Test-Path -LiteralPath $backup) { throw 'Já existe uma cópia anterior que não pôde ser removida.' }
                    Move-Item -LiteralPath $target -Destination $backup -Force
                    $backupReady = $true
                } else {
                    Remove-Item -LiteralPath $target -Force
                }
            } elseif (-not $backupReady -and -not (Test-Path -LiteralPath $backup)) {
                throw 'O executável instalado não foi encontrado e não há cópia para restauração.'
            }

            Move-Item -LiteralPath $staged -Destination $target -Force
            $targetHash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
            if ($sourceHash -ne $targetHash) { throw 'O arquivo instalado não corresponde ao arquivo baixado.' }
            $installed = $true
            break
        } catch {
            $lastInstallError = $_.Exception.Message
            if (Test-Path -LiteralPath $backup) {
                try {
                    if (Test-Path -LiteralPath $target) {
                        $currentTargetHash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
                        if ($currentTargetHash -ne $sourceHash) { Remove-Item -LiteralPath $target -Force }
                    }
                    if (-not (Test-Path -LiteralPath $target)) { Copy-Item -LiteralPath $backup -Destination $target -Force }
                } catch { $lastInstallError += ' A cópia de segurança foi preservada, mas a restauração temporária falhou: ' + $_.Exception.Message }
            }
        }
        Start-Sleep -Milliseconds 500
    }
    if (-not $installed) { throw ('O Windows não permitiu substituir o executável. Detalhe do sistema: {0}' -f $lastInstallError) }

    if ($argLine.Length -gt 0) { $newProcess = Start-Process -FilePath $target -ArgumentList $argLine -WorkingDirectory $workDir -PassThru }
    else { $newProcess = Start-Process -FilePath $target -WorkingDirectory $workDir -PassThru }
    if ($null -eq $newProcess) { throw 'O Windows não iniciou a nova versão.' }
    try { $newProcess.WaitForInputIdle(15000) | Out-Null } catch { }
    Start-Sleep -Seconds 2
    $newProcess.Refresh()
    if ($newProcess.HasExited) { throw 'A nova versão encerrou durante a inicialização.' }

    for ($cleanupAttempt=0; $cleanupAttempt -lt 20; $cleanupAttempt++) {
        try { Remove-Item -LiteralPath $backup -Force -ErrorAction Stop } catch { }
        if (-not (Test-Path -LiteralPath $backup)) { break }
        Start-Sleep -Milliseconds 500
    }
} catch {
    $rawError = $_.Exception.Message
    $hresult = '0x{0:X8}' -f [BitConverter]::ToUInt32([BitConverter]::GetBytes([int]$_.Exception.HResult),0)
    $cause = 'O Windows bloqueou a operação por uma causa não classificada.'
    $action = 'Confira a mensagem técnica e tente novamente.'
    if ($hresult -eq '0x80070005' -or $rawError -match '(?i)access.*denied|acesso negado|unauthorizedaccess') { $cause = 'A conta atual não tem permissão para gravar na pasta do programa.'; $action = 'Feche o programa e tente executá-lo como administrador, ou instale-o em uma pasta do usuário.' }
    elseif ($hresult -eq '0x80070020' -or $hresult -eq '0x80070021' -or $rawError -match '(?i)being used by another process|used by another process|em uso por outro processo|sharing violation|still running|ainda est[aá] em execu[cç][aã]o') { $cause = 'O executável está aberto ou bloqueado por outro processo.'; $action = 'Feche outras instâncias do ConsultaSQL. Se necessário, aguarde o antivírus concluir a verificação e tente novamente.' }
    elseif ($hresult -eq '0x80070002' -or $hresult -eq '0x80070003') { $cause = 'O arquivo ou a pasta da instalação não foi encontrado.'; $action = 'Confira se o executável e a pasta de instalação ainda existem.' }
    elseif ($hresult -eq '0x80070070') { $cause = 'Não há espaço livre suficiente para concluir a atualização.'; $action = 'Libere espaço no disco e tente novamente.' }
    $message = $cause + ""`r`n"" + $action + ""`r`nCódigo do Windows: "" + $hresult + ""`r`nDetalhe técnico: "" + $rawError
    if ($newProcess -ne $null) {
        try { $newProcess.Refresh(); if (-not $newProcess.HasExited) { Stop-Process -Id $newProcess.Id -Force }; $newProcess.WaitForExit(15000) } catch { }
    }
    if (Test-Path -LiteralPath $backup) {
        try {
            $failedTarget = $target + '.failed'
            if (Test-Path -LiteralPath $failedTarget) { Remove-Item -LiteralPath $failedTarget -Force }
            if (Test-Path -LiteralPath $target) { Move-Item -LiteralPath $target -Destination $failedTarget -Force }
            Move-Item -LiteralPath $backup -Destination $target -Force
            if (Test-Path -LiteralPath $failedTarget) { Remove-Item -LiteralPath $failedTarget -Force }
        } catch {
            try { Copy-Item -LiteralPath $backup -Destination $target -Force } catch { $message += ' A restauração automática falhou: ' + $_.Exception.Message }
        }
    }
    try {
        Add-Type -AssemblyName System.Windows.Forms
        $prompt = 'A atualização não foi concluída. A versão anterior foi restaurada, se necessário.' + [Environment]::NewLine + [Environment]::NewLine + $message + [Environment]::NewLine + [Environment]::NewLine + 'Deseja tentar atualizar novamente agora?'
        $choice = [System.Windows.Forms.MessageBox]::Show($prompt,'Diagnóstico da atualização',[System.Windows.Forms.MessageBoxButtons]::RetryCancel,[System.Windows.Forms.MessageBoxIcon]::Warning,[System.Windows.Forms.MessageBoxDefaultButton]::Button2)
        if ($choice -eq [System.Windows.Forms.DialogResult]::Retry) {
            try {
                $retryScript = $scriptPath + '.retry.ps1'
                Copy-Item -LiteralPath $scriptPath -Destination $retryScript -Force
                $powershellPath = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::Windows)) 'System32\WindowsPowerShell\v1.0\powershell.exe'
                if (-not (Test-Path -LiteralPath $powershellPath)) { $powershellPath = 'powershell.exe' }
                $retryArguments = '-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File ' + [char]34 + $retryScript + [char]34 + ' -1 ' + [char]34 + $Target64 + [char]34 + ' ' + [char]34 + $Download64 + [char]34 + ' ' + [char]34 + $Args64 + [char]34
                Start-Process -FilePath $powershellPath -ArgumentList $retryArguments -WorkingDirectory $workDir -WindowStyle Hidden
                $retryStarted = $true
            } catch {
                [System.Windows.Forms.MessageBox]::Show(('Não foi possível iniciar a nova tentativa. Detalhe: {0}' -f $_.Exception.Message),'Falha ao repetir atualização',[System.Windows.Forms.MessageBoxButtons]::OK,[System.Windows.Forms.MessageBoxIcon]::Error) | Out-Null
            }
        }
        if (-not $retryStarted) { [System.Windows.Forms.MessageBox]::Show(('A atualização não foi concluída. A versão anterior foi restaurada, se necessário. Detalhes: {0}' -f $message),'Atualização do Consulta SQL',[System.Windows.Forms.MessageBoxButtons]::OK,[System.Windows.Forms.MessageBoxIcon]::Warning) | Out-Null }
    } catch { }
    if (-not $retryStarted -and (Test-Path -LiteralPath $target)) {
        try {
            if ($argLine.Length -gt 0) { Start-Process -FilePath $target -ArgumentList $argLine -WorkingDirectory $workDir }
            else { Start-Process -FilePath $target -WorkingDirectory $workDir }
        } catch { }
    }
} finally {
    try { if (-not $retryStarted -and (Test-Path -LiteralPath $staged)) { Remove-Item -LiteralPath $staged -Force -ErrorAction SilentlyContinue } } catch { }
    try { if (-not $retryStarted -and (Test-Path -LiteralPath $download)) { Remove-Item -LiteralPath $download -Force -ErrorAction SilentlyContinue } } catch { }
    try { if (Test-Path -LiteralPath $scriptPath) { Remove-Item -LiteralPath $scriptPath -Force -ErrorAction SilentlyContinue } } catch { }
}";
        File.WriteAllText(scriptPath,script,new UTF8Encoding(true));
        string powershell=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),@"System32\WindowsPowerShell\v1.0\powershell.exe");if(!File.Exists(powershell))powershell="powershell.exe";
        ProcessStartInfo start=new ProcessStartInfo();start.FileName=powershell;start.Arguments="-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File \""+scriptPath+"\" "+processId.ToString(CultureInfo.InvariantCulture)+" \""+target64+"\" \""+download64+"\" \""+args64+"\"";start.CreateNoWindow=true;start.UseShellExecute=false;
        if(Process.Start(start)==null)throw new InvalidOperationException("O atualizador do Windows não foi iniciado.");
    }

    static string ApplicationPath() { return Process.GetCurrentProcess().MainModule.FileName; }
    static string Base64(string value) { return Convert.ToBase64String(Encoding.UTF8.GetBytes(value??"")); }
    static void TryDelete(string path) { try { if(File.Exists(path))File.Delete(path); } catch {} }
}

