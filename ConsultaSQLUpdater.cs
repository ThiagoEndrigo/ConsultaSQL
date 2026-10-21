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

    public static UpdateCandidate CheckForUpdate() {
        try {
            ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
            string json=GetText(ApiUrl,8000);
            Dictionary<string,object> release=Json.Deserialize<Dictionary<string,object>>(json);
            if(release==null||!release.ContainsKey("tag_name")||!release.ContainsKey("assets"))return null;
            string tag=Convert.ToString(release["tag_name"],CultureInfo.InvariantCulture);
            Version newest;if(String.IsNullOrWhiteSpace(tag)||!Version.TryParse(tag.TrimStart('v','V'),out newest))return null;
            Version current=typeof(Program).Assembly.GetName().Version;
            if(newest<=current)return null;
            object[] assets=release["assets"] as object[];
            if(assets==null) { ArrayList list=release["assets"] as ArrayList;if(list!=null)assets=list.ToArray(); }
            if(assets==null)return null;
            string url="";
            foreach(object item in assets) {
                Dictionary<string,object> asset=item as Dictionary<string,object>;
                if(asset==null||!asset.ContainsKey("name")||!asset.ContainsKey("browser_download_url"))continue;
                if(String.Equals(Convert.ToString(asset["name"],CultureInfo.InvariantCulture),"ConsultaSQL.exe",StringComparison.OrdinalIgnoreCase)){url=Convert.ToString(asset["browser_download_url"],CultureInfo.InvariantCulture);break;}
            }
            Uri downloadUri;if(String.IsNullOrWhiteSpace(url)||!Uri.TryCreate(url,UriKind.Absolute,out downloadUri)||downloadUri.Scheme!=Uri.UriSchemeHttps||!String.Equals(downloadUri.Host,"github.com",StringComparison.OrdinalIgnoreCase)||!downloadUri.AbsolutePath.StartsWith("/ThiagoEndrigo/ConsultaSQL/releases/download/",StringComparison.OrdinalIgnoreCase))return null;
            string folder=Path.Combine(Path.GetTempPath(),"ConsultaSQL-Atualizacao");Directory.CreateDirectory(folder);
            string downloadPath=Path.Combine(folder,"ConsultaSQL-"+newest.ToString()+"-"+Guid.NewGuid().ToString("N")+".exe");
            DownloadFile(downloadUri.AbsoluteUri,downloadPath,60000);
            FileInfo info=new FileInfo(downloadPath);if(info.Length<65536||info.Length>50L*1024L*1024L){TryDelete(downloadPath);return null;}
            using(FileStream file=File.OpenRead(downloadPath)){if(file.ReadByte()!='M'||file.ReadByte()!='Z'){TryDelete(downloadPath);return null;}}
            string productVersion=FileVersionInfo.GetVersionInfo(downloadPath).ProductVersion??"";Version binaryVersion;
            if(!Version.TryParse(productVersion.Trim().TrimStart('v','V'),out binaryVersion)||binaryVersion!=newest){TryDelete(downloadPath);return null;}
            return new UpdateCandidate{Tag=tag,DownloadPath=downloadPath,ReleaseUrl=url};
        } catch { return null; }
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
    Copy-Item -LiteralPath $download -Destination $staged -Force
    $sourceHash = (Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash
    $stagedHash = (Get-FileHash -LiteralPath $staged -Algorithm SHA256).Hash
    if ($sourceHash -ne $stagedHash) { throw 'A cópia temporária da atualização não corresponde ao arquivo baixado.' }

    for ($i=0; $i -lt 30; $i++) {
        try {
            if (Test-Path -LiteralPath $target) { Move-Item -LiteralPath $target -Destination $backup -Force }
            Move-Item -LiteralPath $staged -Destination $target -Force
            $targetHash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
            if ($sourceHash -eq $targetHash) { $installed = $true; break }
            throw 'O arquivo instalado não corresponde ao arquivo baixado.'
        } catch {
            $lastInstallError = $_.Exception.Message
            if (-not (Test-Path -LiteralPath $target) -and (Test-Path -LiteralPath $backup)) {
                try { Move-Item -LiteralPath $backup -Destination $target -Force } catch { $lastInstallError += ' A restauração temporária também falhou: ' + $_.Exception.Message }
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
    $message = $_.Exception.Message
    if ($message -match '(?i)access.*denied|acesso negado|unauthorizedaccess') { $message = ('Sem permissão para gravar na pasta do programa. Instale-o em uma pasta do usuário ou execute-o com permissão de administrador. Detalhe: {0}' -f $message) }
    elseif ($message -match '(?i)being used by another process|used by another process|em uso por outro processo|sharing violation') { $message = ('Outro processo ainda está usando o executável. Feche as outras instâncias e tente novamente. Detalhe: {0}' -f $message) }
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
        [System.Windows.Forms.MessageBox]::Show(('A atualização não foi concluída. A versão anterior foi restaurada, se necessário. Abra o programa novamente. Detalhe: {0}' -f $message),'Atualização do Consulta SQL',[System.Windows.Forms.MessageBoxButtons]::OK,[System.Windows.Forms.MessageBoxIcon]::Warning) | Out-Null
    } catch { }
    if (Test-Path -LiteralPath $target) {
        try {
            if ($argLine.Length -gt 0) { Start-Process -FilePath $target -ArgumentList $argLine -WorkingDirectory $workDir }
            else { Start-Process -FilePath $target -WorkingDirectory $workDir }
        } catch { }
    }
} finally {
    try { if (Test-Path -LiteralPath $staged) { Remove-Item -LiteralPath $staged -Force -ErrorAction SilentlyContinue } } catch { }
    try { if (Test-Path -LiteralPath $download) { Remove-Item -LiteralPath $download -Force -ErrorAction SilentlyContinue } } catch { }
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

