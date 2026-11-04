using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

// No Firebird connections are permitted in this component.
internal sealed class RecoverySafety {
    public readonly string DirectoryPath;
    readonly string original;
    readonly Dictionary<string,object> manifest = new Dictionary<string,object>();
    readonly List<object> events = new List<object>();
    public RecoverySafety(string source,string destination) {
        original=Path.GetFullPath(source);
        DirectoryPath=Path.Combine(destination,"Recuperacao_"+Path.GetFileNameWithoutExtension(source)+"_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+"_"+Guid.NewGuid().ToString("N"));
        foreach(string part in new[]{"work","backup","staging","resultado","logs","relatorios"})System.IO.Directory.CreateDirectory(Path.Combine(DirectoryPath,part));
        manifest["id"]=Guid.NewGuid().ToString("N");manifest["original"]=original;manifest["startedUtc"]=DateTime.UtcNow.ToString("o");manifest["version"]=typeof(RecoverySafety).Assembly.GetName().Version.ToString();manifest["events"]=events;
        Record("Preparando","Execução criada; nenhum resultado aprovado.");
    }
    public void Record(string state,string detail) {
        manifest["state"]=state;events.Add(new{utc=DateTime.UtcNow.ToString("o"),state=state,detail=detail});Save();
    }
    void Save() {
        string target=Path.Combine(DirectoryPath,"manifest.json"),temp=target+".tmp";
        using(var stream=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None))using(var writer=new StreamWriter(stream,new UTF8Encoding(false))){writer.Write(new JavaScriptSerializer().Serialize(manifest));writer.Flush();stream.Flush(true);}
        if(File.Exists(target))File.Replace(temp,target,null);else File.Move(temp,target);
    }
    internal static void CheckOffline() {
        // Conservative guard: never stop a server automatically, including unrelated servers.
        foreach(Process process in Process.GetProcesses())using(process){string name=process.ProcessName;if(name.Equals("fbserver",StringComparison.OrdinalIgnoreCase)||name.Equals("fb_inet_server",StringComparison.OrdinalIgnoreCase)||name.Equals("fbguard",StringComparison.OrdinalIgnoreCase)||name.Equals("firebird",StringComparison.OrdinalIgnoreCase))throw new IOException("Encerre o servidor e o guardian Firebird antes da cópia offline. O assistente não altera o serviço automaticamente.");}
    }
    internal static string Hash(Stream stream) {stream.Position=0;using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();}
    public void CopyOffline(string target) {
        Record("Copiando","Pré-condição: origem offline confirmada pelo operador.");CheckOffline();
        string parent=Path.GetDirectoryName(original),stem=Path.GetFileNameWithoutExtension(original);
        foreach(string file in System.IO.Directory.GetFiles(parent)){
            string name=Path.GetFileName(file);
            if(name.EndsWith(".delta",StringComparison.OrdinalIgnoreCase)||name.StartsWith(Path.GetFileName(original)+".",StringComparison.OrdinalIgnoreCase)||name.StartsWith(stem+".shd",StringComparison.OrdinalIgnoreCase))throw new IOException("Arquivo associado detectado: "+name+". Esta versão não suporta cópia de bancos com delta, shadow ou arquivos secundários.");
        }
        try {
            using(var source=new FileStream(original,FileMode.Open,FileAccess.Read,FileShare.None)){
                if(source.Length<1024)throw new IOException("Arquivo curto demais para ser aceito como banco Firebird.");
                string before=Hash(source);manifest["originalHashBefore"]=before;Save();
                source.Position=0;using(var copy=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None)){source.CopyTo(copy);copy.Flush(true);}
                string after=Hash(source);using(var copy=File.OpenRead(target)){if(Hash(copy)!=before||after!=before)throw new IOException("Hash divergente durante a cópia; recuperação bloqueada.");}
                CheckOffline();manifest["originalHashAfterCopy"]=after;manifest["workCopy"]=target;
                File.WriteAllText(Path.Combine(DirectoryPath,"original_hash.txt"),before+"  "+original+Environment.NewLine,Encoding.UTF8);
                Record("CopiaVerificada","SHA-256 da origem e da cópia coincidem. Verificação não comprova integridade do banco.");
            }
        }catch(Exception ex){Record("Falhou",ex.Message);throw;}
    }
    public void Finish(string report) {
        string state=Convert.ToString(manifest["state"]);
        try{using(var stream=new FileStream(original,FileMode.Open,FileAccess.Read,FileShare.Read)){string hash=Hash(stream);manifest["originalHashAtEnd"]=hash;if(manifest.ContainsKey("originalHashBefore")&&hash!=Convert.ToString(manifest["originalHashBefore"]))state="OrigemAlterada";}}
        catch(Exception ex){manifest["originalHashAtEndUnavailable"]=ex.Message;}
        File.WriteAllText(Path.Combine(DirectoryPath,"logs","diagnostico.log"),report,Encoding.UTF8);
        File.WriteAllText(Path.Combine(DirectoryPath,"relatorios","relatorio_final.html"),"<!doctype html><meta charset=\"utf-8\"><title>Recuperação</title><h1>"+System.Web.HttpUtility.HtmlEncode(state)+"</h1><p>Completude dos dados não comprovada. Arquivos em staging não estão aprovados para produção.</p><pre>"+System.Web.HttpUtility.HtmlEncode(report)+"</pre>",Encoding.UTF8);
        manifest["finishedUtc"]=DateTime.UtcNow.ToString("o");Record(state,"Relatório gravado. Nenhum banco promovido automaticamente.");
    }
}
