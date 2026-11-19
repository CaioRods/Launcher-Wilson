using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

internal static class EditorControl
{
    internal static int ReservePort(){var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();return port;}
    internal static Task<Dictionary<string,object>> Send(EditorPage page,string action,string name=null)
    {
        if(!new[]{"state","save","saveAs","rename","detach","embed"}.Contains(action))throw new ArgumentException("Unsupported action");
        return Task.Run(()=>
        {
            string key=page.ControlToken;
            if(page.App==1){string folder=Environment.GetEnvironmentVariable("WORDCRAFT_PROFILE_DIR")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"WordCraft");string path=Path.Combine(folder,"control-key.local-"+page.ControlPort);if(!File.Exists(path)||new FileInfo(path).Length>512)throw new IOException("O editor ainda está iniciando.");key=File.ReadAllText(path).Trim();}
            if(string.IsNullOrWhiteSpace(key)||key.Length>256)throw new IOException("Não foi possível autenticar o editor.");
            using(var client=new TcpClient())
            {
                if(!client.ConnectAsync(IPAddress.Loopback,page.ControlPort).Wait(1000))throw new IOException("O editor não respondeu.");client.ReceiveTimeout=(action=="save"||action=="saveAs")?120000:5000;client.SendTimeout=1000;
                using(var stream=client.GetStream())
                {
                    var json=new JavaScriptSerializer{MaxJsonLength=65536,RecursionLimit=24};
                    if(page.App==0){Write(stream,json.Serialize(new{id=1,method="auth",@params=new{token=key}}));Read(stream,json);}
                    Write(stream,json.Serialize(new{id=2,key=key,method="engine.execute",@params=new{command="ui.workspace.action",@params=new{action=action,name=name}}}));
                    var reply=Read(stream,json);object result;return reply.TryGetValue("result",out result)?result as Dictionary<string,object>??new Dictionary<string,object>():reply;
                }
            }
        });
    }
    private static void Write(Stream stream,string value){byte[] bytes=Encoding.UTF8.GetBytes(value+"\n");if(bytes.Length>16384)throw new IOException("Solicitação muito longa.");stream.Write(bytes,0,bytes.Length);}
    private static Dictionary<string,object> Read(Stream stream,JavaScriptSerializer json)
    {
        var bytes=new System.Collections.Generic.List<byte>();for(int i=0;i<65536;i++){int value=stream.ReadByte();if(value<0)throw new IOException("O editor fechou a conexão.");if(value==10){var result=json.DeserializeObject(Encoding.UTF8.GetString(bytes.ToArray())) as Dictionary<string,object>;object ok,error;if(result==null)throw new IOException("Resposta inválida do editor.");if(result.TryGetValue("ok",out ok)&&ok is bool&&!(bool)ok||result.TryGetValue("error",out error))throw new IOException("O editor recusou a operação.");return result;}bytes.Add((byte)value);}throw new IOException("Resposta muito longa.");
    }
}
