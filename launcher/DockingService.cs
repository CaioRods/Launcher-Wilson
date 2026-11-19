using System;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// A live HWND is transferred: document data, undo, caret and scroll never leave the editor.
// WinEvents describe actual OS window moves; an ordinary click over the central cannot dock it.
internal sealed class DockingService : IDisposable
{
    private readonly SuiteHost host;private readonly WinEvent callback;private readonly IntPtr hook;
    private EditorPage moving;private readonly Label cue;
    private readonly Timer discovery=new Timer{Interval=1500};
    private readonly Timer feedback=new Timer{Interval=100};
    private readonly ToolTip hint=new ToolTip{ShowAlways=true};
    private bool targetHot;private Point lastHint;private Native.RECT moveStart;
    internal DockingService(SuiteHost owner)
    {
        host=owner;cue=new Label{Text="Solte para devolver ao AWcontrol",AutoSize=false,TextAlign=ContentAlignment.MiddleCenter,BackColor=Color.FromArgb(147,20,37),ForeColor=Color.White,Font=new Font("Segoe UI",11,FontStyle.Bold),Visible=false,Height=38};
        host.Controls.Add(cue);feedback.Tick+=(s,e)=>UpdateTarget();discovery.Tick+=(s,e)=>Discover();discovery.Start();callback=OnMove;hook=SetWinEventHook(0x000A,0x000B,IntPtr.Zero,callback,0,0,0);
    }
    private void Discover()
    {
        string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"AlimentosWilson","sessions");
        try
        {
            if(!Directory.Exists(folder))return;
            foreach(var path in Directory.EnumerateFiles(folder,"*.json").Take(128))
            {
                try
                {
                if(new FileInfo(path).Length>4096)continue;
                string raw=File.ReadAllText(path);
                var data=new JavaScriptSerializer{MaxJsonLength=4096,RecursionLimit=4}.DeserializeObject(raw) as Dictionary<string,object>;
                object pidValue,appValue,portValue,exeValue,version;
                if(data==null||!data.TryGetValue("version",out version)||!Equals(version,1)||!data.TryGetValue("pid",out pidValue)||!(pidValue is int)||!data.TryGetValue("app",out appValue)||!(appValue is int)||!data.TryGetValue("port",out portValue)||!(portValue is int)||!data.TryGetValue("executable",out exeValue)||!(exeValue is string))continue;
                int pid=(int)pidValue,app=(int)appValue,port=(int)portValue;if(Path.GetFileNameWithoutExtension(path)!=pid.ToString()||pid<=0||app<0||app>1||port<1||port>65535||host.Pages.Concat(host.DetachedPages).Concat(host.StagingPages).Any(p=>p.Process.Id==pid))continue;
                Process process=null;
                try
                {
                    try{process=Process.GetProcessById(pid);}
                    catch(ArgumentException){if(File.ReadAllText(path)==raw)File.Delete(path);continue;} // Prune dead sessions so old records cannot hide a newly started app.
                    if(process.HasExited||!string.Equals(process.MainModule.FileName,(string)exeValue,StringComparison.OrdinalIgnoreCase)){process.Dispose();continue;}
                    string installed=Distribution.InstalledPath(app);if(installed==null||!string.Equals(installed,(string)exeValue,StringComparison.OrdinalIgnoreCase)){process.Dispose();continue;}
                    IntPtr window=Native.FindWindow(pid);if(window==IntPtr.Zero||Native.GetParent(window)!=IntPtr.Zero){process.Dispose();continue;}
                    string token=null;if(app==0){string tokenPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".gridcraft","control-"+port+".token");if(!File.Exists(tokenPath)||new FileInfo(tokenPath).Length>512){process.Dispose();continue;}token=File.ReadAllText(tokenPath).Trim();}
                    host.DetachedPages.Add(new EditorPage{App=app,Process=process,Window=window,ControlPort=port,ControlToken=token,Title=app==0?"Excel AW":"Word AW"});process=null;
                }
                catch(Exception error){if(!(error is ArgumentException||error is InvalidOperationException||error is System.ComponentModel.Win32Exception||error is IOException||error is UnauthorizedAccessException))throw;}
                finally{if(process!=null)process.Dispose();}
                }
                catch(Exception error){if(!(error is IOException||error is UnauthorizedAccessException||error is ArgumentException||error is InvalidOperationException))throw;}
            }
        }
        catch(Exception error){if(!(error is IOException||error is UnauthorizedAccessException||error is ArgumentException||error is InvalidOperationException))throw;}
        foreach(var page in host.DetachedPages.ToArray())if(page.Process.HasExited){host.DetachedPages.Remove(page);page.Process.Dispose();}
    }
    private void OnMove(IntPtr handle,uint ev,IntPtr window,int obj,int child,uint thread,uint time)
    {
        if(obj!=0||host.IsDisposed||!host.IsHandleCreated)return;
        try{host.BeginInvoke((Action)(()=>
        {
            if(ev==0x000A){var candidate=host.DetachedPages.FirstOrDefault(p=>p.Window==window&&!p.Process.HasExited);if(candidate!=null){moving=candidate;Native.GetWindowRect(window,out moveStart);moving.DockOrigin=Rectangle.FromLTRB(moveStart.Left,moveStart.Top,moveStart.Right,moveStart.Bottom);cue.SetBounds(Math.Max(10,(host.ClientSize.Width-320)/2),44,320,38);targetHot=false;feedback.Start();UpdateTarget();}}
            else if(moving!=null&&moving.Window==window)
            {
                var page=moving;moving=null;feedback.Stop();hint.Hide(host);cue.Visible=false;
                // Only an OS window drag can dock; ordinary editing clicks are never interpreted as drops.
                if(IsMove(window)&&DropBounds.Contains(Cursor.Position)&&host.WindowState!=FormWindowState.Minimized)Return(page);
            }
        }));}catch(InvalidOperationException){}
    }
    private bool IsMove(IntPtr window)
    {
        Native.RECT current;if(!Native.GetWindowRect(window,out current))return false;
        // Windows emits the same move/size events for a border resize. Resizing must
        // never adopt a document merely because its edge happens to overlap the central.
        return current.Right-current.Left==moveStart.Right-moveStart.Left&&current.Bottom-current.Top==moveStart.Bottom-moveStart.Top&&(Math.Abs(current.Left-moveStart.Left)>3||Math.Abs(current.Top-moveStart.Top)>3);
    }
    private void UpdateTarget()
    {
        if(moving==null)return;bool hot=IsMove(moving.Window)&&DropBounds.Contains(Cursor.Position)&&host.WindowState!=FormWindowState.Minimized;cue.Visible=hot;
        if(hot){cue.BringToFront();Point p=host.PointToClient(Cursor.Position);if(!targetHot||Math.Abs(p.X-lastHint.X)+Math.Abs(p.Y-lastHint.Y)>40){hint.Show("Solte para devolver ao AWcontrol",host,p.X+18,p.Y+24,20000);lastHint=p;}}
        else if(targetHot)hint.Hide(host);targetHot=hot;
    }
    internal Rectangle DropBounds{get{return host.RectangleToScreen(host.ClientRectangle);}}
    private async void Return(EditorPage page){await host.DockPage(page);}
    public void Dispose(){feedback.Dispose();hint.Dispose();discovery.Dispose();if(hook!=IntPtr.Zero)UnhookWinEvent(hook);cue.Dispose();}
    private delegate void WinEvent(IntPtr hook,uint ev,IntPtr window,int obj,int child,uint thread,uint time);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min,uint max,IntPtr module,WinEvent callback,uint process,uint thread,uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
}
