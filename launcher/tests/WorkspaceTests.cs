using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

internal static class WorkspaceTests
{
    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        if(Environment.GetEnvironmentVariable("AWCONTROL_EMBEDDED")=="1")
        {
            string path=args.FirstOrDefault();
            using(var editor=new Form { Text=(path==null?"Documento":Path.GetFileName(path))+" - Word AW",Size=new Size(900,600),FormBorderStyle=FormBorderStyle.None })
            {
                editor.Controls.Add(new Label { Text=path??"Nova página",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter,Font=new Font("Segoe UI",24) });
                editor.FormClosing+=(s,e)=>{if(path!=null && File.Exists(path+".keep")){editor.Text="Cancelado - Word AW";e.Cancel=true;}};
                Application.Run(editor);
            }
            return 0;
        }
        bool real=args.Contains("--real");
        string folder=Path.Combine(Path.GetTempPath(),"AWcontrol-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        string word=Path.Combine(folder,"relatório de teste.txt"),excel=Path.Combine(folder,"tabela de teste.csv");
        File.WriteAllText(word,"Documento de teste do AWcontrol.");File.WriteAllText(excel,"Produto,Quantidade\nTeste,3\n");
        if(!real)File.WriteAllText(word+".keep","");
        string failure=null;int phase=0;var timeout=Stopwatch.StartNew();var since=Stopwatch.StartNew();EditorPage first=null,second=null;
        var all=new System.Collections.Generic.List<Process>();
        try
        {
            using(var host=new SuiteHost(real?(Func<int,string>)Distribution.InstalledPath:app=>Application.ExecutablePath,true))
            using(var broker=new WorkspaceBroker(host))
            using(var timer=new Timer { Interval=100 })
            {
                host.Size=new Size(1200,780);
                timer.Tick+=(s,e)=>
                {
                    try
                    {
                        if(timeout.Elapsed.TotalSeconds>120)throw new Exception("Workspace test timeout at phase "+phase);
                        if(phase==0)
                        {
                            Capture(host,real?"real-home":"home");host.OpenDocument(0,null);host.OpenDocument(1,word);all.AddRange(host.Pages.Select(p=>p.Process));first=host.Pages[0];second=host.Pages[1];phase=1;return;
                        }
                        if(phase==1 && host.Pages.Count==2 && host.Pages.All(p=>p.Window!=IntPtr.Zero))
                        {
                            Require(host.Pages.All(p=>Native.GetParent(p.Window)==host.Stage.Handle),"editor must be a real child of the workspace");
                            host.SelectPage(first);Require(Native.IsWindowVisible(first.Window)&&!Native.IsWindowVisible(second.Window),"tab switching must show only the selected editor");
                            host.SelectPage(null);Require(!Native.IsWindowVisible(first.Window)&&!Native.IsWindowVisible(second.Window),"Home must hide both editors");
                            host.SelectPage(second);host.Size=new Size(1040,680);host.Reorder(second,0);Require(host.Pages[0]==second,"drag order must retain page identity");phase=2;since.Restart();return;
                        }
                        if(phase==2 && since.ElapsedMilliseconds>500)
                        {
                            Native.RECT bounds;Native.GetClientRect(second.Window,out bounds);Require(bounds.Right==host.Stage.ClientSize.Width && bounds.Bottom==host.Stage.ClientSize.Height,"embedded editor must follow workspace resizing");
                            Capture(host,real?"real-mixed-tabs":"mixed-tabs");host.ClosePage(second);phase=3;since.Restart();return;
                        }
                        if(phase==3 && since.ElapsedMilliseconds>600)
                        {
                            if(!real){Require(!second.Process.HasExited,"Cancel must keep the editor and its document alive");File.Delete(word+".keep");host.ClosePage(second);}
                            phase=4;return;
                        }
                        if(phase==4 && host.Pages.Count==1)
                        {
                            // Same path through the live broker as a second AWcontrol invocation.
                            var send=Task.Run(()=>WorkspaceBroker.Forward(new[]{excel,word}));
                            send.ContinueWith(t=>{if(!t.Result)failure="broker did not acknowledge forwarded files";});phase=5;return;
                        }
                        if(phase==5 && host.Pages.Count==3 && host.Pages.All(p=>p.Window!=IntPtr.Zero))
                        {
                            all.AddRange(host.Pages.Select(p=>p.Process).Where(p=>!all.Contains(p)));
                            Require(host.Pages.Count(p=>p.App==0)==2 && host.Pages.Count(p=>p.App==1)==1,"forwarded files must create mixed Excel/Word pages");
                            Require(host.Pages.Any(p=>p.Path==word)&&host.Pages.Any(p=>p.Path==excel),"Unicode paths must survive forwarding");
                            if(real)
                            {
                                // A Windows file association invokes the standalone executable; it must hand the file to AWcontrol and exit.
                                var exe=Distribution.InstalledPath(1);
                                using(var forwarded=Process.Start(new ProcessStartInfo(exe,SuiteHost.Quote(word)){UseShellExecute=false}))
                                    Require(forwarded.WaitForExit(5000)&&forwarded.ExitCode==0,"standalone Word must hand an existing file to AWcontrol and exit");
                                exe=Distribution.InstalledPath(0);
                                using(var forwarded=Process.Start(new ProcessStartInfo(exe,SuiteHost.Quote(excel)){UseShellExecute=false}))
                                    Require(forwarded.WaitForExit(5000)&&forwarded.ExitCode==0,"standalone Excel must hand an existing file to AWcontrol and exit");
                            }
                            Capture(host,real?"real-files":"files");phase=6;host.Close();return;
                        }
                        if(phase==6 && host.IsDisposed)timer.Stop();
                    }
                    catch(Exception error){failure=error.ToString();timer.Stop();host.Dispose();}
                };
                timer.Start();Application.Run(host);
                Require(failure==null,failure??"");Require(phase==6,"test ended before all checks");
            }
            Console.WriteLine(real?"Real Excel/Word workspace integration passed.":"Workspace tabs, routing, resize, Home and Cancel lifecycle passed.");return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
        finally
        {
            foreach(var p in all)try{if(!p.HasExited)p.Kill();}catch(InvalidOperationException){}
            try{Directory.Delete(folder,true);}catch(IOException){}catch(UnauthorizedAccessException){}
        }
    }
    private static void Require(bool valid,string error){if(!valid)throw new Exception(error);}
    private static void Capture(Form form,string name)
    {
        string folder=Environment.GetEnvironmentVariable("AWCONTROL_TEST_SCREENSHOTS");if(string.IsNullOrEmpty(folder))return;Directory.CreateDirectory(folder);
        using(var image=new Bitmap(form.ClientSize.Width,form.ClientSize.Height)){form.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(Path.Combine(folder,name+".png"));}
    }
}
