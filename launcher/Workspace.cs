using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

// Each page contains the real native editor. Closing always asks that editor to close;
// the workspace never terminates an editor or discards its save/cancel prompt.
internal sealed class EditorPage
{
    internal readonly Guid Id = Guid.NewGuid();
    internal int App;
    internal string Path, Title;
    internal Process Process;
    internal IntPtr Window;
    internal readonly Stopwatch Opening = Stopwatch.StartNew();
    internal readonly Stopwatch Attached = new Stopwatch();
    internal bool CloseSent, AttachFailed, Attaching, Detaching, CommandBusy, StatePending;
    internal int ControlPort;
    internal string ControlToken;
    internal Rectangle DockOrigin=Rectangle.Empty;
    internal bool DockOriginMaximized;
    internal Size LastSize=Size.Empty;
    internal readonly Stopwatch TitleRefresh=Stopwatch.StartNew();
}

internal sealed class SuiteHost : Form
{
    internal readonly List<EditorPage> Pages = new List<EditorPage>();
    internal EditorPage ActivePage;
    internal readonly List<EditorPage> DetachedPages=new List<EditorPage>();
    internal readonly List<EditorPage> StagingPages=new List<EditorPage>();
    private readonly WindowChrome chrome;
    private readonly DockingService docking;
    internal readonly Panel Stage = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(248,248,249) };
    private readonly WorkspaceStrip strip;
    internal readonly WilsonHome Home;
    private readonly Label status = new Label { Dock = DockStyle.Bottom, Height = 26, Padding = new Padding(18,4,0,0) };
    private readonly System.Windows.Forms.Timer poll = new System.Windows.Forms.Timer { Interval = 100 };
    private readonly Func<int,string> locate;
    private WorkspaceBroker broker;
    private bool closeAll, completeClose;
    private readonly Stopwatch profileRefresh=Stopwatch.StartNew();
    internal bool Testing;
    internal bool NativeCommands=true;
    internal string CloseStatus { get { return "closeAll="+closeAll+"; "+string.Join(" | ",Pages.Select(p=>p.Title+", requested="+p.CloseSent+", exited="+p.Process.HasExited)); } }

    private static readonly string StateDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AlimentosWilson");

    internal static readonly Color Red = Color.FromArgb(161,57,54), Gold = Color.FromArgb(168,113,15);
    internal static readonly Image[] Logos = { LoadArt("excel"), LoadArt("word") };
    internal static readonly Image Brand = LoadArt("wilson");
    internal static readonly Image ControlIcon = LoadArt("awcontrol");
    internal bool AnimateUI {get{return Home==null||Home.Animations;}}
    internal void RefreshAnimations(){strip.Animate();}
    internal void Notify(string text){status.Text=text;status.Visible=true;}

    internal SuiteHost(Func<int,string> find = null, bool testing = false)
    {
        Testing = testing;
        locate = find ?? (Func<int,string>)Distribution.InstalledPath;
        Text = "AWcontrol";
        Font = new Font("Segoe UI",9);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96,96);
        FormBorderStyle = FormBorderStyle.None;
        MinimumSize = new Size(720,500);
        Size = new Size(1200,780);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(248,248,249);
        using(var art=typeof(SuiteHost).Assembly.GetManifestResourceStream("awcontrol.ico"))
            Icon=art==null?Icon.ExtractAssociatedIcon(Application.ExecutablePath):new Icon(art);
        Home = new WilsonHome(this);
        strip = new WorkspaceStrip(this) { Dock = DockStyle.Top, Height = 40 };
        var body=new Panel{Dock=DockStyle.Fill,Padding=Padding.Empty,BackColor=Color.FromArgb(235,232,230)};body.Controls.Add(Stage);
        Controls.Add(body); Controls.Add(status); Controls.Add(strip);
        Stage.Controls.Add(Home);
        chrome=new WindowChrome(this);docking=new DockingService(this);
        status.Visible=false;
        status.TextChanged+=(s,e)=>{status.Visible=new[]{"Não","Arquivo não","Feche","Falha","O editor"}.Any(prefix=>status.Text.StartsWith(prefix,StringComparison.Ordinal));};
        AllowDrop = true;
        DragEnter += (s,e) => { if(e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect=DragDropEffects.Copy; };
        DragDrop += (s,e) => { var paths=e.Data.GetData(DataFormats.FileDrop) as string[]; if(paths!=null) foreach(var p in paths) OpenDocument(Kind(p),p); };
        Stage.Resize += (s,e) => ResizeEditors();
        poll.Tick += (s,e) => { TickEditors(); if(profileRefresh.ElapsedMilliseconds>=1000){profileRefresh.Restart();strip.RefreshProfile();Home.UpdateProfile(strip.ProfileName,strip.Avatar);} };
        poll.Start();
        Shown += (s,e) => { MaximizedBounds=Screen.FromHandle(Handle).WorkingArea; };
        Activated += (s,e) => { if(ActivePage!=null && ActivePage.Window!=IntPtr.Zero)Native.FocusEditor(ActivePage.Window); };
        Deactivate += (s,e) => { if(ActivePage!=null && ActivePage.Window!=IntPtr.Zero)Native.EditorActive(ActivePage.Window,false); };
        SizeChanged+=(s,e)=>Native.RoundWindow(this);
        HandleCreated+=(s,e)=>Native.RoundWindow(this);
        FormClosing += ClosingWorkspace;
        FormClosed += (s,e) => { poll.Dispose(); if(broker!=null) broker.Dispose(); foreach(var p in Pages) p.Process.Dispose();foreach(var p in DetachedPages)p.Process.Dispose();docking.Dispose();chrome.Dispose(); };
    }

    internal static void Run(string[] args)
    {
        if(args.Length==1 && args[0]=="--smoke-test") { using(var h=new SuiteHost()) {} return; }
        string user=WindowsIdentity.GetCurrent().User.Value;
        using(var mutex=new Mutex(false,"Local\\AlimentosWilson.Workspace."+user))
        {
            bool owns;
            try { owns=mutex.WaitOne(0); } catch(AbandonedMutexException) { owns=true; }
            if(!owns)
            {
                for(int n=0;n<10;n++) { if(WorkspaceBroker.Forward(args)) return; Thread.Sleep(100); }
                MessageBox.Show("O AWcontrol está iniciando. Tente abrir o arquivo novamente.","AWcontrol");return;
            }
            try
            {
                using(var h=new SuiteHost())
                {
                    h.broker=new WorkspaceBroker(h);
                    foreach(string p in args.Where(p=>!p.StartsWith("--",StringComparison.Ordinal))) h.OpenDocument(Kind(p),System.IO.Path.GetFullPath(p));
                    Application.Run(h);
                }
            }
            catch(Exception e) { if(!(e is IOException || e is SocketException || e is UnauthorizedAccessException))throw; MessageBox.Show("Não foi possível iniciar o AWcontrol: "+e.Message,"AWcontrol"); }
            finally { mutex.ReleaseMutex(); }
        }
    }

    private static Image LoadArt(string name)
    {
        using(var s=typeof(SuiteHost).Assembly.GetManifestResourceStream(name))
        { if(s==null)return new Bitmap(1,1); using(var i=Image.FromStream(s))return new Bitmap(i); }
    }
    internal static int Kind(string path)
    {
        string ext=System.IO.Path.GetExtension(path).ToLowerInvariant();
        return new[]{".xlsx",".xlsm",".xltx",".xltm",".xls",".xlsb",".csv",".tsv",".ods"}.Contains(ext)?0:1;
    }
    internal void Receive(int app, string[] files, string startup=null)
    {
        WindowState=WindowState==FormWindowState.Minimized?FormWindowState.Normal:WindowState;
        Native.SetForegroundWindow(Handle);
        if(files.Length==0) { if(app<0) SelectPage(null); else OpenDocument(app,null,startup); }
        else foreach(string path in files) OpenDocument(Kind(path),path);
    }
    internal void OpenDocument(int app, string path, string startup=null)
    {
        if(app<0 || app>1)return;
        if(path!=null)
        {
            path=System.IO.Path.GetFullPath(path);
            if(!File.Exists(path)) { status.Text="Arquivo não encontrado: "+System.IO.Path.GetFileName(path);return; }
            var existing=Pages.FirstOrDefault(p=>p.Path!=null && string.Equals(p.Path,path,StringComparison.OrdinalIgnoreCase));
            if(existing!=null){ SelectPage(existing);return; }
        }
        if(Pages.Count>=32){ status.Text="Feche uma página antes de abrir outra.";return; }
        string exe=locate(app);
        if(exe==null){ ManageApps();return; }
        if(!Testing && Distribution.InstalledVersion(app,false)<Distribution.BundledUpdate(app).Version){ManageApps();return;}
        var page=CreateEditor(app,path,startup);if(page==null)return;
        Pages.Add(page);SelectPage(page);status.Text="Abrindo "+page.Title+"…";
        if(path!=null)Home.AddRecent(path);
    }
    internal EditorPage CreateEditor(int app,string path=null,string startup=null)
    {
        string exe=locate(app);if(exe==null)return null;
        var page=new EditorPage { App=app,Path=path,Title=path==null?(app==0?"Nova planilha":"Novo documento"):System.IO.Path.GetFileName(path) };
        try
        {
            page.ControlPort=NativeCommands?EditorControl.ReservePort():0;page.ControlToken=Guid.NewGuid().ToString("N")+Guid.NewGuid().ToString("N");
            string arguments=(NativeCommands?"--control "+page.ControlPort+" ":"")+(path==null?"":Quote(path));
            var info=new ProcessStartInfo(exe,arguments) { UseShellExecute=false,WorkingDirectory=System.IO.Path.GetDirectoryName(exe) };
            info.EnvironmentVariables["AWCONTROL_EMBEDDED"]="1";
            info.EnvironmentVariables["AWCONTROL_APP"]=app.ToString();
            if(NativeCommands&&app==0)info.EnvironmentVariables["GRIDCRAFT_CONTROL_TOKEN"]=page.ControlToken;
            if(startup!=null)info.EnvironmentVariables["AWCONTROL_STARTUP"]=startup;
            page.Process=Process.Start(info);
            if(page.Process==null){ status.Text="Não foi possível abrir "+Distribution.Names[app];return null; }
            return page;
        }
        catch(Exception e) { if(!(e is System.ComponentModel.Win32Exception || e is IOException || e is UnauthorizedAccessException))throw; status.Text="Não foi possível abrir "+Distribution.Names[app]+": "+e.Message; }
        return null;
    }
    internal static string Quote(string value)
    {
        // CommandLineToArgvW escaping, including trailing backslashes; never invoke a shell.
        var b=new StringBuilder("\"");int slashes=0;
        foreach(char c in value)
        { if(c=='\\'){slashes++;continue;} if(c=='"'){b.Append('\\',slashes*2+1);b.Append(c);}else{b.Append('\\',slashes);b.Append(c);}slashes=0; }
        b.Append('\\',slashes*2);b.Append('"');return b.ToString();
    }
    internal void SelectPage(EditorPage page)
    {
        if(page!=null&&!Pages.Contains(page))return;
        closeAll=false;
        ActivePage=page;
        foreach(var p in Pages) if(p.Window!=IntPtr.Zero&&!p.Attaching&&!p.Detaching) {if(p!=page)Native.EditorActive(p.Window,false);Native.ShowWindowAsync(p.Window,p==page?5:0);}
        Home.Visible=page==null;
        if(page==null){Home.Navigate("home");Home.BringToFront();}
        else if(page.Window!=IntPtr.Zero&&!page.Attaching&&!page.Detaching){ Native.FocusEditor(page.Window);status.Text=page.Title; }
        ResizeEditors();strip.EnsureVisible(page);strip.Animate();
    }
    internal void ClosePage(EditorPage page)
    {
        if(!Pages.Contains(page)||page.Detaching||page.CommandBusy||page.Process.HasExited)return;
        SelectPage(page);
        page.CloseSent=true;
        if(page.Window!=IntPtr.Zero) {Native.PostMessage(page.Window,0x0010,IntPtr.Zero,IntPtr.Zero);status.Text="Fechando página. Confirme o salvamento no editor, se solicitado."; }
    }
    internal void Reorder(EditorPage page, int index)
    { if(index<0 || index>=Pages.Count||!Pages.Contains(page)||page.Detaching)return;Pages.Remove(page);Pages.Insert(index,page);strip.EnsureVisible(ActivePage);strip.Animate(); }
    private void ResizeEditors()
    {
        foreach(var p in Pages)if(p.Window!=IntPtr.Zero && !p.Attaching && !p.Detaching && !p.Process.HasExited && p.LastSize!=Stage.ClientSize)
        {Native.Resize(p.Window,0,0,Stage.ClientSize.Width,Stage.ClientSize.Height);p.LastSize=Stage.ClientSize;}
    }
    private void TickEditors()
    {
        foreach(var p in Pages.ToArray())
        {
            if(p.Process.HasExited)
            {
                bool active=ActivePage==p;Pages.Remove(p);p.Process.Dispose();
                if(active) { var next=Pages.LastOrDefault();bool closing=closeAll;SelectPage(next);closeAll=closing; }
                strip.Animate();continue;
            }
            if(p.Attaching||p.Detaching)continue;
            if(p.Window!=IntPtr.Zero && !Native.IsWindow(p.Window)){p.Window=IntPtr.Zero;p.Attached.Reset();}
            // The renderer can recreate its first window or restore its original styles on first focus.
            // Re-establish child style/parent after that transition instead of retaining a stale HWND.
            if(p.Window==IntPtr.Zero || Native.GetParent(p.Window)!=Stage.Handle || (Native.GetWindowLongPtr(p.Window,-16).ToInt64() & 0x40000000L)==0)
            {
                IntPtr window=p.Window!=IntPtr.Zero?p.Window:Native.FindWindow(p.Process.Id);
                if(window!=IntPtr.Zero && Native.Responsive(window))
                {
                    AttachEditor(p,window);
                }
                else if(p.Opening.Elapsed.TotalSeconds>90)status.Text="O editor está demorando para iniciar. Aguarde ou feche esta página.";
            }
            else if(p.TitleRefresh.ElapsedMilliseconds>=500)
            {
                p.TitleRefresh.Restart();var title=new StringBuilder(512);Native.GetWindowText(p.Window,title,title.Capacity);
                string name=title.ToString().Replace(" — Word AW","").Replace(" - Word AW","").Replace(" — Excel AW","").Replace(" - Excel AW","");
                if(name.Length>0 && name!=p.Title){p.Title=name;strip.Animate();if(NativeCommands&&!p.CommandBusy&&!p.StatePending)RefreshDocumentPath(p);}
            }
        }
        if(closeAll)
        {
            if(Pages.Count==0){ completeClose=true;Close(); }
            else if(!Pages.Any(p=>p.CloseSent)){var next=Pages[0];ClosePage(next);closeAll=true;}
        }
    }
    private void AttachEditor(EditorPage page,IntPtr window)
    {
        // Keep the candidate across winit's startup transitions. Cross-process parent/style
        // calls can wait for the editor's UI thread; let our own message loop keep running.
        page.Window=window;page.Attaching=true;page.LastSize=Size.Empty;IntPtr stage=Stage.Handle;
        Task.Run(()=>
        {
            if(Testing)Console.WriteLine(DateTime.UtcNow.ToString("O")+" Attaching editor "+page.App+" hwnd="+window);
            long style=Native.GetWindowLongPtr(window,-16).ToInt64();
            Native.SetWindowLongPtr(window,-16,new IntPtr((style & ~0x81CF0000L) | 0x46000000L));
            Native.SetParent(window,stage);int error=Marshal.GetLastWin32Error();
            long childStyle=Native.GetWindowLongPtr(window,-16).ToInt64();
            Native.SetWindowLongPtr(window,-16,new IntPtr((childStyle & ~0x81CF0000L) | 0x46000000L));
            long ex=Native.GetWindowLongPtr(window,-20).ToInt64();Native.SetWindowLongPtr(window,-20,new IntPtr((ex & ~0x00040000L) | 0x00000080L));
            if(IsDisposed || !IsHandleCreated)return;
            try{BeginInvoke((Action)(()=>
            {
                page.Attaching=false;if(IsDisposed || !Pages.Contains(page))return;
                if(Native.GetParent(window)!=Stage.Handle){if(Testing&&!page.AttachFailed)Console.WriteLine("Parent failure app="+page.App+" error="+error+" actual="+Native.GetParent(window));page.AttachFailed=true;return;}
                page.AttachFailed=false;page.Attached.Restart();
                if(Testing)Console.WriteLine("Parent attached for editor "+page.App);
                Native.SetWindowPos(window,IntPtr.Zero,0,0,Stage.ClientSize.Width,Stage.ClientSize.Height,0x4034);
                page.LastSize=Stage.ClientSize;Native.ShowWindowAsync(window,page==ActivePage?5:0);ResizeEditors();
                if(page==ActivePage)Native.FocusEditor(window);
                if(page.CloseSent)Native.PostMessage(window,0x0010,IntPtr.Zero,IntPtr.Zero);
                status.Text=page.Title;
            }));}catch(InvalidOperationException){}
        });
    }
    private void ClosingWorkspace(object sender,FormClosingEventArgs e)
    {
        if(StagingPages.Count>0||Pages.Concat(DetachedPages).Any(p=>p.Detaching||p.CommandBusy)){e.Cancel=true;Notify("Aguarde a operação do documento terminar.");return;}
        if(Home.ApplicationsBusy){e.Cancel=true;Notify("Aguarde a instalação terminar antes de fechar o AWcontrol.");return;}
        if(completeClose || Pages.Count==0)return;
        e.Cancel=true;
        // Close in document order, so each save/cancel prompt is visible. Cancel keeps the suite alive.
        if(Pages.Any(p=>p.CloseSent)) { foreach(var p in Pages)p.CloseSent=false; }
        ClosePage(ActivePage??Pages[0]);closeAll=true;
    }
    internal void ManageApps(){SelectPage(null);Home.Navigate("apps");}
    internal void ToggleMaximized(){strip.ResetCaptionClick();WindowState=WindowState==FormWindowState.Maximized?FormWindowState.Normal:FormWindowState.Maximized;}
    internal async Task<bool> DetachPage(EditorPage page,Point location)
    {
        if(!Pages.Contains(page)||page.Attaching||page.Detaching||page.Window==IntPtr.Zero||page.CommandBusy)return false;
        page.Detaching=true;
        try
        {
            if(NativeCommands)await EditorControl.Send(page,"detach");
            IntPtr window=page.Window;var area=Screen.FromPoint(location).WorkingArea;float dpi=DeviceDpi/96f;int width=Math.Min((int)(1100*dpi),area.Width-40),height=Math.Min((int)(760*dpi),area.Height-40);
            var bounds=new Rectangle(Math.Max(area.Left,Math.Min(location.X-140,area.Right-width)),Math.Max(area.Top,Math.Min(location.Y-20,area.Bottom-height)),width,height);
            bool detached=await Task.Run(()=>Native.Detach(window,bounds));
            if(!detached){if(NativeCommands)await EditorControl.Send(page,"embed");page.Detaching=false;Notify("Não foi possível destacar a página. Ela continua aberta no AWcontrol.");return false;}
            Pages.Remove(page);if(ActivePage==page)SelectPage(Pages.LastOrDefault());strip.Animate();
            Native.RoundHandle(window);Native.ShowWindowAsync(window,5);Native.SetForegroundWindow(window);Native.FocusEditor(window);
            // The editor keeps its process and dirty document. Closing the central won't close it.
            page.Detaching=false;DetachedPages.Add(page);return true;
        }
        catch(Exception error){if(!(error is IOException||error is SocketException||error is AggregateException||error is InvalidOperationException||error is UnauthorizedAccessException||error is ArgumentException))throw;}
        // A lost reply may arrive after the native mode changed. Restore hosted mode before
        // accepting another drag/close, so a partial transition cannot leave an orphan tab.
        try{if(NativeCommands)await EditorControl.Send(page,"embed");}catch(Exception error){if(!(error is IOException||error is SocketException||error is AggregateException||error is InvalidOperationException||error is UnauthorizedAccessException||error is ArgumentException))throw;}
        page.Detaching=false;Notify("Não foi possível destacar a página. Ela continua aberta no AWcontrol.");return false;
    }
    internal async Task<bool> DockPage(EditorPage page)
    {
        if(!DetachedPages.Contains(page)||page.Attaching||page.Detaching||page.CommandBusy||page.Process.HasExited||!Native.IsWindow(page.Window))return false;
        if(Pages.Count>=32){Notify("Feche uma página antes de devolver outra.");return false;}
        page.Detaching=true;bool requested=false;
        try
        {
            if(NativeCommands){var state=await EditorControl.Send(page,"state");object pid,count;if(state.TryGetValue("pid",out pid)&&!Equals(pid,page.Process.Id))throw new IOException("A sessão do editor mudou.");
                if(!state.TryGetValue("count",out count)||!(count is int)||(int)count<1){page.Detaching=false;Notify("Não foi possível identificar as abas. Atualize os dois editores em Aplicativos.");return false;}
                if((int)count>1){bool moved=await SelectedHandoff.Move(this,page);page.Detaching=false;return moved;}
                requested=true;await EditorControl.Send(page,"embed");}
            DetachedPages.Remove(page);page.CloseSent=false;page.Detaching=false;page.LastSize=Size.Empty;
            page.Attached.Reset();page.Attaching=true;Pages.Add(page);SelectPage(page);AttachEditor(page,page.Window);Native.SetForegroundWindow(Handle);return true;
        }
        catch(Exception error){if(!(error is IOException||error is SocketException||error is AggregateException||error is InvalidOperationException||error is UnauthorizedAccessException||error is ArgumentException))throw;}
        if(requested){try{await EditorControl.Send(page,"detach");}catch(Exception error){if(!(error is IOException||error is SocketException||error is AggregateException||error is InvalidOperationException||error is UnauthorizedAccessException||error is ArgumentException))throw;}}
        page.Detaching=false;Notify("Não foi possível devolver a página. O editor continua aberto separadamente.");return false;
    }
    internal void AdoptImported(EditorPage page)
    {
        StagingPages.Remove(page);page.Window=Native.FindWindow(page.Process.Id);page.Attached.Reset();page.LastSize=Size.Empty;
        Pages.Add(page);SelectPage(page);if(page.Window!=IntPtr.Zero)AttachEditor(page,page.Window);Native.SetForegroundWindow(Handle);
        if(page.Path!=null)Home.AddRecent(page.Path);
    }
    private async void RefreshDocumentPath(EditorPage page)
    {
        page.StatePending=true;
        try{var state=await EditorControl.Send(page,"state");object path;if(Pages.Contains(page)&&!page.Detaching&&state.TryGetValue("path",out path)){string current=path as string;if(current!=null&&!string.Equals(current,page.Path,StringComparison.OrdinalIgnoreCase)){page.Path=current;Home.AddRecent(current);}}}
        catch(Exception error){if(!(error is IOException||error is SocketException||error is AggregateException||error is InvalidOperationException||error is UnauthorizedAccessException||error is ArgumentException))throw;}
        finally{page.StatePending=false;}
    }
    internal async void DocumentAction(EditorPage page,string action)
    {
        if(page.CommandBusy||page.Detaching||!Pages.Contains(page))return;page.CommandBusy=true;SelectPage(page);
        try
        {
            string name=null,previous=page.Path;
            if(action=="rename")
            {
                var state=await EditorControl.Send(page,"state");object value;string path=state.TryGetValue("path",out value)?value as string:null;previous=path;
                using(var dialog=new Form{Text="Renomear arquivo",FormBorderStyle=FormBorderStyle.FixedDialog,ClientSize=new Size(360,132),StartPosition=FormStartPosition.CenterParent,MinimizeBox=false,MaximizeBox=false})
                {var field=new TextBox{Text=path==null?page.Title:Path.GetFileNameWithoutExtension(path),Bounds=new Rectangle(18,22,324,25),MaxLength=200};var save=new Button{Text="Renomear",Bounds=new Rectangle(230,77,112,32),DialogResult=DialogResult.OK};dialog.Controls.Add(field);dialog.Controls.Add(save);dialog.AcceptButton=save;if(dialog.ShowDialog(this)!=DialogResult.OK)return;name=field.Text;}
            }
            await EditorControl.Send(page,action,name);var current=await EditorControl.Send(page,"state");object updated;if(current.TryGetValue("path",out updated)){page.Path=updated as string;if(page.Path!=null){if(action=="rename")Home.RenameRecent(previous,page.Path);else Home.AddRecent(page.Path);}}
            Native.FocusEditor(page.Window);
        }
        catch(Exception error){if(!(error is IOException||error is SocketException||error is AggregateException||error is InvalidOperationException||error is UnauthorizedAccessException||error is ArgumentException))throw;Notify("Não foi possível concluir a operação no editor.");}
        finally{page.CommandBusy=false;}
    }
    internal void OpenPicker(int? app=null)
    {
        string filter=app==0?"Planilhas|*.xlsx;*.xlsm;*.xls;*.csv;*.tsv;*.ods;*.json|Todos os arquivos|*.*":app==1?"Documentos|*.docx;*.doc;*.odt;*.rtf;*.txt;*.md|Todos os arquivos|*.*":"Documentos e planilhas|*.xlsx;*.xlsm;*.csv;*.tsv;*.ods;*.docx;*.doc;*.odt;*.rtf;*.txt;*.md|Todos os arquivos|*.*";
        using(var d=new OpenFileDialog { Multiselect=true,Filter=filter })
            if(d.ShowDialog(this)==DialogResult.OK)foreach(var p in d.FileNames)OpenDocument(app??Kind(p),p);
    }
    internal void OpenTemplate(int app,string key)
    {
        object args=app==0?(object)new{sample=key}:new{template=key};
        OpenDocument(app,null,new JavaScriptSerializer().Serialize(new{command="file.new",@params=args}));
    }
    protected override CreateParams CreateParams
    {
        get{var cp=base.CreateParams;cp.Style|=0x00040000|0x00020000|0x00010000;return cp;}
    }
    protected override void WndProc(ref Message m)
    {
        if(m.Msg==0x00A1&&m.WParam.ToInt32()>=10&&m.WParam.ToInt32()<=17&&chrome!=null){chrome.BeginResize(m.WParam.ToInt32());m.Result=IntPtr.Zero;return;}
        if(m.Msg==0x0083){m.Result=IntPtr.Zero;return;} // Keep resize/Snap styles without a second title bar.
        if(m.Msg==0x007F){m.Result=Icon==null?IntPtr.Zero:Icon.Handle;return;}
        if(m.Msg==0x0080&&Icon!=null)m.LParam=Icon.Handle;
        if(m.Msg==0x00A3&&m.WParam.ToInt32()==2){ToggleMaximized();m.Result=IntPtr.Zero;return;}
        const int HIT=0x84;
        if(m.Msg==HIT && WindowState==FormWindowState.Normal)
        {
            base.WndProc(ref m);var p=PointToClient(new Point((short)((long)m.LParam & 0xffff),(short)(((long)m.LParam>>16)&0xffff)));
            bool l=p.X<6,r=p.X>=ClientSize.Width-6,t=p.Y<4,b=p.Y>=ClientSize.Height-6;
            if(l||r||t||b)m.Result=new IntPtr(l?(t?13:b?16:10):r?(t?14:b?17:11):t?12:15);return;
        }
        base.WndProc(ref m);
    }
}

internal sealed class WorkspaceStrip : Control
{
    private const int BrandWidth=194,Start=292,TabWidth=190,Right=222;
    private readonly SuiteHost host;
    private readonly System.Windows.Forms.Timer animation=new System.Windows.Forms.Timer{Interval=16};
    private readonly Dictionary<Guid,float> positions=new Dictionary<Guid,float>(),hover=new Dictionary<Guid,float>();
    private readonly Dictionary<EditorPage,RectangleF> rectangles=new Dictionary<EditorPage,RectangleF>();
    private EditorPage pressed,hotPage;
    private Point press;
    private bool moving,outside;
    private readonly Stopwatch captionClick=new Stopwatch();
    private Point captionPoint;
    internal void ResetCaptionClick(){captionClick.Reset();}
    private EditorPage menuPage;
    internal readonly ContextMenuStrip TabMenu=new ContextMenuStrip();
    private float offset;
    private float Unit {get{return DeviceDpi/96f;}}
    private float LogicalWidth {get{return Width/Unit;}}
    private int hovered=-1;
    private Image avatar;
    private DateTime profileStamp;
    private string profileName="Conta",lastTip="";
    internal readonly ContextMenuStrip NewFileMenu=new ContextMenuStrip();
    private readonly ToolTip tip=new ToolTip();
    internal string ProfileName {get{return profileName;}}
    internal Image Avatar {get{return avatar;}}
    internal WorkspaceStrip(SuiteHost owner)
    {
        host=owner;DoubleBuffered=true;SetStyle(ControlStyles.ResizeRedraw,true);BackColor=Color.FromArgb(36,37,43);
        TabMenu.Items.Add("Salvar",null,(s,e)=>{if(menuPage!=null)host.DocumentAction(menuPage,"save");});
        TabMenu.Items.Add("Salvar como…",null,(s,e)=>{if(menuPage!=null)host.DocumentAction(menuPage,"saveAs");});
        TabMenu.Items.Add("Renomear arquivo…",null,(s,e)=>{if(menuPage!=null)host.DocumentAction(menuPage,"rename");});
        TabMenu.Items.Add(new ToolStripSeparator());TabMenu.Items.Add("Abrir em janela separada",null,async(s,e)=>{if(menuPage!=null)await host.DetachPage(menuPage,Cursor.Position);});
        TabMenu.Items.Add(new ToolStripSeparator());TabMenu.Items.Add("Fechar aba",null,(s,e)=>{if(menuPage!=null)host.ClosePage(menuPage);});
        SetStyle(ControlStyles.StandardDoubleClick|ControlStyles.StandardClick,true);
        NewFileMenu.Items.Add("Nova planilha",null,(s,e)=>host.OpenDocument(0,null));
        NewFileMenu.Items.Add("Novo documento",null,(s,e)=>host.OpenDocument(1,null));
        animation.Tick+=(s,e)=>Invalidate();Disposed+=(s,e)=>{animation.Dispose();tip.Dispose();NewFileMenu.Dispose();TabMenu.Dispose();if(avatar!=null)avatar.Dispose();};AccessibleName="Páginas do AWcontrol";
    }
    internal void RefreshProfile()
    {
        string path=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"AlimentosWilson","profile.json");
        try
        {
            var info=new FileInfo(path);if(!info.Exists||info.LastWriteTimeUtc==profileStamp||info.Length>16*1024*1024)return;profileStamp=info.LastWriteTimeUtc;
            var data=new JavaScriptSerializer{MaxJsonLength=16*1024*1024}.DeserializeObject(File.ReadAllText(path)) as Dictionary<string,object>;
            if(data==null)return;object value;if(data.TryGetValue("name",out value)&&value is string)profileName=(string)value;
            Image next=null;if(data.TryGetValue("photo",out value)){var bytes=value as object[];if(bytes!=null&&bytes.Length>0&&bytes.Length<4*1024*1024){using(var stream=new MemoryStream(bytes.Select(Convert.ToByte).ToArray()))using(var image=Image.FromStream(stream)){if(image.Width<=4096&&image.Height<=4096)next=new Bitmap(image);}}}
            var previous=avatar;avatar=next;if(previous!=null)previous.Dispose();Invalidate();
        }catch(Exception e){if(!(e is IOException||e is UnauthorizedAccessException||e is ArgumentException||e is InvalidOperationException||e is FormatException||e is OverflowException))throw;}
    }
    internal void Animate(){animation.Start();Invalidate();}
    internal void EnsureVisible(EditorPage page)
    {
        if(page==null){offset=0;return;}int index=host.Pages.IndexOf(page);float available=LogicalWidth-Start-Right;
        offset=Math.Max(0,Math.Min(offset,index*TabWidth));if((index+1)*TabWidth-offset>available)offset=Math.Max(0,(index+1)*TabWidth-available);
    }
    internal static GraphicsPath Round(RectangleF r,float radius)
    {var p=new GraphicsPath();float d=radius*2;p.AddArc(r.Left,r.Top,d,d,180,90);p.AddArc(r.Right-d,r.Top,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.Left,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;}
    protected override void OnPaint(PaintEventArgs e)
    {
        var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;rectangles.Clear();bool changing=false;float h=Height/Unit-4;g.ScaleTransform(Unit,Unit);
        using(var b=new LinearGradientBrush(new RectangleF(0,0,LogicalWidth,h),Color.FromArgb(47,48,54),Color.FromArgb(26,27,32),90))g.FillRectangle(b,0,0,LogicalWidth,h);
        g.DrawImage(SuiteHost.Brand,12,(h-24)/2,38,24);WilsonHome.Text(g,"AWcontrol",new RectangleF(60,0,126,h),16,Color.White,true);
        using(var p=new Pen(Color.FromArgb(90,140,139,140)))g.DrawLine(p,BrandWidth-12,12,BrandWidth-12,h-12);
        var home=new RectangleF(BrandWidth+4,4,Start-BrandWidth-8,h-4);
        if(host.ActivePage==null)Pill(g,home,Color.FromArgb(251,249,247));
        WilsonHome.Icon(g,"home",new RectangleF(home.X+12,10,16,16),host.ActivePage==null?Color.FromArgb(112,23,34):Color.White);
        WilsonHome.Text(g,"Início",new RectangleF(home.X+36,3,home.Width-39,h-3),13,host.ActivePage==null?Color.FromArgb(38,29,31):Color.White,true);
        var state=g.Save();g.SetClip(new RectangleF(Start,0,Math.Max(0,LogicalWidth-Start-Right),h));
        for(int i=0;i<host.Pages.Count;i++)
        {
            var page=host.Pages[i];float target=Start+i*TabWidth-offset,current;if(!positions.TryGetValue(page.Id,out current))current=target+14;
            float next=host.AnimateUI?current+(target-current)*.27f:target;if(Math.Abs(target-next)<.4f)next=target;else changing=true;positions[page.Id]=next;
            float old;hover.TryGetValue(page.Id,out old);float to=page==hotPage?1:0;float amount=host.AnimateUI?old+(to-old)*.24f:to;if(Math.Abs(amount-to)<.01f)amount=to;else changing=true;hover[page.Id]=amount;
            var r=new RectangleF(next+4,4,TabWidth-8,h-4);rectangles[page]=r;bool active=host.ActivePage==page;
            if(active)Pill(g,r,Color.FromArgb(251,249,247));else{Pill(g,r,Color.FromArgb(38+(int)(amount*26),39+(int)(amount*26),45+(int)(amount*26)));using(var path=Round(r,8))using(var pen=new Pen(Color.FromArgb(75+(int)(amount*80),120,120,125)))g.DrawPath(pen,path);}
            using(var b=new SolidBrush(page.App==0?Color.FromArgb(182,41,49):Color.FromArgb(213,158,40)))g.FillRectangle(b,r.Left+10,r.Top, r.Width-20,2);
            g.DrawImage(SuiteHost.Logos[page.App],r.Left+10,r.Top+6,20,20);
            WilsonHome.Text(g,page.Title,new RectangleF(r.Left+38,r.Top,r.Width-68,r.Height),12,active?Color.FromArgb(43,32,34):Color.White);
            if(page==hotPage)Pill(g,new RectangleF(r.Right-27,r.Top+5,21,22),Color.FromArgb(active?20:32,active?Color.Black:Color.White));
            using(var pen=new Pen(active?Color.FromArgb(76,60,62):Color.FromArgb(224,219,220),1.2f)){float x=r.Right-16,y=r.Top+r.Height/2;g.DrawLine(pen,x-3,y-3,x+3,y+3);g.DrawLine(pen,x-3,y+3,x+3,y-3);}
        }
        g.Restore(state);
        WilsonHome.Text(g,"+",new RectangleF(LogicalWidth-Right,0,36,h),25,Color.White,false,StringAlignment.Center);
        var avatarRect=new RectangleF(LogicalWidth-178,(h-30)/2,30,30);
        if(avatar!=null){var saved=g.Save();using(var path=new GraphicsPath()){path.AddEllipse(avatarRect);g.SetClip(path);g.DrawImage(avatar,avatarRect);}g.Restore(saved);}else{using(var b=new SolidBrush(SuiteHost.Red))g.FillEllipse(b,avatarRect);WilsonHome.Text(g,"AW",avatarRect,12,Color.White,true,StringAlignment.Center);}
        for(int i=0;i<3;i++)
        {var r=new RectangleF(LogicalWidth-138+i*46,0,46,h);if(hovered==i)using(var b=new SolidBrush(i==2?Color.FromArgb(196,43,28):Color.FromArgb(70,70,80)))g.FillRectangle(b,r);using(var pen=new Pen(Color.White,1)){float x=r.Left+23,y=h/2;if(i==0)g.DrawLine(pen,x-5,y,x+5,y);else if(i==1)g.DrawRectangle(pen,x-5,y-5,10,10);else{g.DrawLine(pen,x-5,y-5,x+5,y+5);g.DrawLine(pen,x-5,y+5,x+5,y-5);}}}
        if(!changing)animation.Stop();foreach(var id in positions.Keys.Where(id=>!host.Pages.Any(p=>p.Id==id)).ToArray()){positions.Remove(id);hover.Remove(id);}
    }
    private static void Pill(Graphics g,RectangleF r,Color color){using(var path=Round(r,8))using(var b=new SolidBrush(color))g.FillPath(b,path);}
    protected override void OnMouseWheel(MouseEventArgs e){offset=Math.Max(0,Math.Min(Math.Max(0,host.Pages.Count*TabWidth-(LogicalWidth-Start-Right)),offset-e.Delta));Animate();}
    private EditorPage PageAt(Point point){float x=point.X/Unit;if(x<Start||x>=LogicalWidth-Right)return null;return rectangles.FirstOrDefault(p=>p.Value.Contains(new PointF(x,point.Y/Unit))).Key;}
    private bool CaptionAt(Point point){float x=point.X/Unit;return x<BrandWidth||x>=Start&&x<LogicalWidth-Right&&PageAt(point)==null;}
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);if(e.Button!=MouseButtons.Left)return;
        if(CaptionAt(e.Location))
        {
            var screen=PointToScreen(e.Location);var tolerance=SystemInformation.DoubleClickSize;
            bool twice=e.Clicks==2||captionClick.IsRunning&&captionClick.ElapsedMilliseconds<=SystemInformation.DoubleClickTime&&Math.Abs(screen.X-captionPoint.X)<=Math.Max(2,tolerance.Width/2)&&Math.Abs(screen.Y-captionPoint.Y)<=Math.Max(2,tolerance.Height/2);
            if(twice)host.ToggleMaximized();else{captionPoint=screen;captionClick.Restart();Native.ReleaseCapture();Native.SendMessage(host.Handle,0xA1,new IntPtr(2),IntPtr.Zero);}return;
        }
        pressed=PageAt(e.Location);press=new Point((int)(e.X/Unit),(int)(e.Y/Unit));moving=outside=false;if(pressed!=null)Capture=true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);int oldHovered=hovered;var oldPage=hotPage;float x=e.X/Unit;hovered=x>=LogicalWidth-138?Math.Min(2,(int)((x-(LogicalWidth-138))/46)):-1;hotPage=PageAt(e.Location);
        if(pressed!=null&&Capture&&(moving||Math.Abs(x-press.X)>6||Math.Abs(e.Y/Unit-press.Y)>6))
        {
            moving=true;var area=host.RectangleToScreen(host.ClientRectangle);area.Inflate(18,18);bool nextOutside=!area.Contains(Cursor.Position);
            if(nextOutside!=outside){outside=nextOutside;if(outside)tip.Show("Solte para abrir "+Distribution.Names[pressed.App]+" em uma janela separada",this,e.Location,10000);else tip.Hide(this);}
            if(!outside){int index=Math.Max(0,Math.Min(host.Pages.Count-1,(int)((x-Start+offset)/TabWidth)));if(host.Pages.IndexOf(pressed)!=index)host.Reorder(pressed,index);}
        }
        string text=hotPage!=null?hotPage.Title:x>=LogicalWidth-182&&x<LogicalWidth-138?profileName:x>=LogicalWidth-Right&&x<LogicalWidth-182?"Novo documento ou planilha":"";
        if(!moving&&text!=lastTip){lastTip=text;tip.SetToolTip(this,text);}if(oldHovered!=hovered||oldPage!=hotPage||moving)Animate();
    }
    protected override void OnMouseLeave(EventArgs e){hovered=-1;hotPage=null;Animate();base.OnMouseLeave(e);}
    protected override async void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);float x=e.X/Unit;
        if(e.Button==MouseButtons.Right){menuPage=PageAt(e.Location);if(menuPage!=null){foreach(ToolStripItem item in TabMenu.Items)item.Enabled=!menuPage.Attaching&&!menuPage.CommandBusy&&!menuPage.Detaching;TabMenu.Show(this,e.Location);}return;}
        if(e.Button!=MouseButtons.Left)return;Capture=false;tip.Hide(this);
        if(pressed!=null){var page=pressed;pressed=null;if(moving&&outside){outside=moving=false;await host.DetachPage(page,Cursor.Position);}else if(!moving){RectangleF r;if(rectangles.TryGetValue(page,out r)&&x>r.Right-27)host.ClosePage(page);else host.SelectPage(page);}moving=false;return;}
        if(x<BrandWidth)return;if(x<Start){host.SelectPage(null);return;}
        if(x>=LogicalWidth-138){int kind=(int)((x-(LogicalWidth-138))/46);if(kind==0)host.WindowState=FormWindowState.Minimized;else if(kind==1)host.ToggleMaximized();else host.Close();return;}
        if(x>=LogicalWidth-182){host.SelectPage(null);host.Home.Navigate("profile");return;}
        if(x>=LogicalWidth-Right)NewFileMenu.Show(this,new Point((int)((LogicalWidth-Right)*Unit),Height));
    }

}


internal sealed class WorkspaceBroker : IDisposable
{
    private readonly TcpListener listener;
    private readonly string token;
    private readonly SuiteHost host;
    private volatile bool stopped;
    private static readonly string Path=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"AlimentosWilson","workspace.json");
    internal WorkspaceBroker(SuiteHost h)
    {
        host=h;var handle=h.Handle;
        byte[] bytes=new byte[32];using(var rng=new RNGCryptoServiceProvider())rng.GetBytes(bytes);token=BitConverter.ToString(bytes).Replace("-","").ToLowerInvariant();
        listener=new TcpListener(IPAddress.Loopback,0);listener.Start(8);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
        string temp=Path+"."+Guid.NewGuid().ToString("N");File.WriteAllText(temp,new JavaScriptSerializer().Serialize(new{port=((IPEndPoint)listener.LocalEndpoint).Port,token=token}));
        if(File.Exists(Path))File.Replace(temp,Path,null);else File.Move(temp,Path);
        Task.Run((Action)Serve);
    }
    private void Serve()
    {
        while(!stopped)
        {
            try
            {
                using(var client=listener.AcceptTcpClient())
                {
                    client.ReceiveTimeout=1000;client.SendTimeout=1000;
                    using(var stream=client.GetStream())
                    {
                        var buffer=new List<byte>();bool done=false;byte[] block=new byte[4096];
                        while(buffer.Count<65536 && !done){int read=stream.Read(block,0,Math.Min(block.Length,65536-buffer.Count));if(read==0)break;for(int i=0;i<read;i++){if(block[i]==10){done=true;break;}buffer.Add(block[i]);}}
                        if(!done)continue;
                        var json=new JavaScriptSerializer{MaxJsonLength=65536,RecursionLimit=8}.DeserializeObject(Encoding.UTF8.GetString(buffer.ToArray())) as Dictionary<string,object>;
                        if(json==null || !json.ContainsKey("token") || !string.Equals(json["token"] as string,token,StringComparison.Ordinal))continue;
                        object appValue,filesValue;if(!json.TryGetValue("app",out appValue)||!json.TryGetValue("files",out filesValue))continue;
                        if(!(appValue is int))continue;int app=(int)appValue;var array=filesValue as object[];
                        if(app< -1 || app>1 || array==null || array.Length>64 || array.Any(p=>!(p is string) || !System.IO.Path.IsPathRooted((string)p)))continue;
                        string[] files=array.Cast<string>().ToArray();
                        if(stopped || host.IsDisposed)continue;
                        string startup=null;object startValue;
                        if(json.TryGetValue("startup",out startValue) && startValue!=null){var request=startValue as Dictionary<string,object>;object command;if(request==null || !request.TryGetValue("command",out command) || !(command is string) || !new[]{"file.new","file.newFromTemplate"}.Contains((string)command))continue;startup=new JavaScriptSerializer().Serialize(request);if(startup.Length>16384)continue;}
                        host.BeginInvoke((Action)(()=>host.Receive(app,files,startup)));
                        byte[] reply=Encoding.ASCII.GetBytes("OK\n");stream.Write(reply,0,reply.Length);
                    }
                }
            }
            catch(Exception e) { if(!(e is SocketException || e is IOException || e is ObjectDisposedException || e is ArgumentException || e is InvalidOperationException))throw;}
        }
    }
    internal static bool Forward(string[] paths)
    {
        try
        {
            if(!File.Exists(Path)||new FileInfo(Path).Length>4096)return false;
            var json=new JavaScriptSerializer().DeserializeObject(File.ReadAllText(Path)) as Dictionary<string,object>;
            object portValue,tokenValue;if(json==null || !json.TryGetValue("port",out portValue) || !(portValue is int) || (int)portValue<1 || (int)portValue>65535 || !json.TryGetValue("token",out tokenValue) || !(tokenValue is string) || ((string)tokenValue).Length!=64)return false;
            using(var c=new TcpClient())
            {
                if(!c.ConnectAsync(IPAddress.Loopback,(int)json["port"]).Wait(300))return false;c.ReceiveTimeout=1000;c.SendTimeout=1000;
                using(var stream=c.GetStream())
                {
                    var data=Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(new{token=(string)json["token"],app=-1,files=paths.Where(p=>!p.StartsWith("--")).Select(System.IO.Path.GetFullPath).ToArray()})+"\n");
                    if(data.Length>65536)return false;stream.Write(data,0,data.Length);byte[] reply=new byte[3];int count=0;while(count<3){int n=stream.Read(reply,count,3-count);if(n==0)return false;count+=n;}return Encoding.ASCII.GetString(reply)=="OK\n";
                }
            }
        }
        catch(Exception e) { if(!(e is IOException || e is UnauthorizedAccessException || e is SocketException || e is AggregateException || e is ArgumentException || e is KeyNotFoundException || e is InvalidOperationException))throw; return false; }
    }
    public void Dispose()
    {
        stopped=true;listener.Stop();
        try { if(File.Exists(Path) && File.ReadAllText(Path).Contains(token))File.Delete(Path); }catch(IOException){}catch(UnauthorizedAccessException){}
    }
}

internal static class Native
{
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)] internal static extern int SetCurrentProcessExplicitAppUserModelID(string id);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
    internal static void RoundHandle(IntPtr window){int preference=2;DwmSetWindowAttribute(window,33,ref preference,4);}
    internal static void RoundWindow(Form form){if(!form.IsHandleCreated)return;int preference=2;int result=DwmSetWindowAttribute(form.Handle,33,ref preference,4);var old=form.Region;if(result!=0&&form.WindowState==FormWindowState.Normal&&form.Width>24&&form.Height>24){using(var path=WorkspaceStrip.Round(new RectangleF(0,0,form.Width,form.Height),10))form.Region=new Region(path);}else form.Region=null;if(old!=null)old.Dispose();}
    internal static bool Detach(IntPtr window,Rectangle bounds)
    {
        ShowWindowAsync(window,0);SetParent(window,IntPtr.Zero);long style=GetWindowLongPtr(window,-16).ToInt64();SetWindowLongPtr(window,-16,new IntPtr((style&~0xC1000000L)|0x00CF0000L));
        SetWindowLongPtr(window,-8,IntPtr.Zero); // Independent windows must not retain the workspace as owner.
        long ex=GetWindowLongPtr(window,-20).ToInt64();SetWindowLongPtr(window,-20,new IntPtr((ex&~0x00000080L)|0x00040000L));
        SetWindowPos(window,IntPtr.Zero,bounds.X,bounds.Y,bounds.Width,bounds.Height,0x4034);return IsWindow(window)&&GetParent(window)==IntPtr.Zero;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct RECT { internal int Left,Top,Right,Bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct POINT { internal int X,Y; }
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr h,out RECT rect);
    [DllImport("user32.dll")] internal static extern bool ClientToScreen(IntPtr h,ref POINT point);
    [DllImport("user32.dll")] internal static extern bool GetClientRect(IntPtr h,out RECT rect);
    [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] internal static extern bool IsZoomed(IntPtr h);
    internal delegate bool EnumWindow(IntPtr h,IntPtr p);
    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumWindow fn,IntPtr p);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
    [DllImport("user32.dll")] internal static extern IntPtr GetWindow(IntPtr h,uint command);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetClassName(IntPtr h,StringBuilder b,int size);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] internal static extern int GetWindowText(IntPtr h,StringBuilder b,int size);
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] internal static extern IntPtr GetWindowLongPtr(IntPtr h,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")] internal static extern IntPtr SetWindowLongPtr(IntPtr h,int index,IntPtr value);
    [DllImport("user32.dll",SetLastError=true)] internal static extern IntPtr SetParent(IntPtr child,IntPtr parent);
    [DllImport("user32.dll")] internal static extern IntPtr GetParent(IntPtr h);
    [DllImport("user32.dll")] internal static extern bool ShowWindowAsync(IntPtr h,int command);
    [DllImport("user32.dll")] internal static extern bool MoveWindow(IntPtr h,int x,int y,int width,int height,bool redraw);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr h,uint message,IntPtr w,IntPtr l);
    [DllImport("user32.dll")] internal static extern IntPtr SendMessage(IntPtr h,uint message,IntPtr w,IntPtr l);
    [DllImport("user32.dll")] internal static extern bool ReleaseCapture();
    [DllImport("user32.dll")] internal static extern IntPtr SetFocus(IntPtr h);
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window,uint flag);
    [DllImport("user32.dll",SetLastError=true)] private static extern IntPtr SendMessageTimeout(IntPtr h,uint message,IntPtr w,IntPtr l,uint flags,uint timeout,out IntPtr result);
    internal static bool Responsive(IntPtr window){IntPtr result;return SendMessageTimeout(window,0,IntPtr.Zero,IntPtr.Zero,2,50,out result)!=IntPtr.Zero;}
    internal static void Resize(IntPtr window,int x,int y,int width,int height){SetWindowPos(window,IntPtr.Zero,x,y,width,height,0x4014);}
    // Winit requires both focus and activation. An embedded child receives WM_SETFOCUS,
    // but Windows activates its top-level host rather than the child's former title bar.
    internal static void EditorActive(IntPtr window,bool active){PostMessage(window,0x0086,active?new IntPtr(1):IntPtr.Zero,IntPtr.Zero);}
    internal static void FocusEditor(IntPtr window){if(GetForegroundWindow()!=GetAncestor(window,2)){EditorActive(window,false);return;}if(Responsive(window)){EditorActive(window,true);SetFocus(window);}}
    internal static IntPtr FindWindow(int process)
    {
        IntPtr found=IntPtr.Zero;EnumWindows((h,p)=>{uint pid;GetWindowThreadProcessId(h,out pid);if(pid==process && GetWindow(h,4)==IntPtr.Zero){var text=new StringBuilder(512);GetWindowText(h,text,512);var kind=new StringBuilder(256);GetClassName(h,kind,kind.Capacity);string name=kind.ToString();if(text.Length>0 && (name=="Window Class" || name.StartsWith("WindowsForms10.Window.",StringComparison.Ordinal))){found=h;return false;}}return true;},IntPtr.Zero);return found;
    }
}
