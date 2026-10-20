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
foreach ($arg in $appArgs) { $quoted += ('""' + ([string]$arg).Replace('""','\""') + '""') }
$argLine = [string]::Join(' ', $quoted)
$workDir = Split-Path -Parent $target
$scriptPath = $MyInvocation.MyCommand.Path
try {
    for ($i=0; $i -lt 120; $i++) {
        $running = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
        if ($null -eq $running) { break }
        Start-Sleep -Milliseconds 250
    }
    $backup = $target + '.previous'
    try { Copy-Item -LiteralPath $target -Destination $backup -Force } catch {}
    $installed = $false
    for ($i=0; $i -lt 20; $i++) {
        try {
            Copy-Item -LiteralPath $download -Destination $target -Force
            $sourceHash = (Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash
            $targetHash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
            if ($sourceHash -eq $targetHash) { $installed = $true; break }
        } catch {}
        Start-Sleep -Milliseconds 500
    }
    if (-not $installed) {
        try { if (Test-Path -LiteralPath $backup) { Copy-Item -LiteralPath $backup -Destination $target -Force } } catch {}
        try { Add-Type -AssemblyName System.Windows.Forms; [System.Windows.Forms.MessageBox]::Show('O Windows não permitiu substituir o executável. O programa antigo será aberto novamente. Instale-o em uma pasta com permissão de gravação.','Atualização do Consulta SQL',[System.Windows.Forms.MessageBoxButtons]::OK,[System.Windows.Forms.MessageBoxIcon]::Warning) | Out-Null } catch {}
    }
    if ($argLine.Length -gt 0) { Start-Process -FilePath $target -ArgumentList $argLine -WorkingDirectory $workDir }
    else { Start-Process -FilePath $target -WorkingDirectory $workDir }
} catch {
    try { Add-Type -AssemblyName System.Windows.Forms; [System.Windows.Forms.MessageBox]::Show('Não foi possível concluir a atualização. Abra o programa novamente.','Atualização do Consulta SQL',[System.Windows.Forms.MessageBoxButtons]::OK,[System.Windows.Forms.MessageBoxIcon]::Warning) | Out-Null } catch {}
} finally {
    try { Remove-Item -LiteralPath $download -Force -ErrorAction SilentlyContinue } catch {}
    try { Remove-Item -LiteralPath $scriptPath -Force -ErrorAction SilentlyContinue } catch {}
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