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
    internal bool CloseSent;
}

internal sealed class SuiteHost : Form
{
    internal readonly List<EditorPage> Pages = new List<EditorPage>();
    internal EditorPage ActivePage;
    internal readonly Panel Stage = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(248,248,249) };
    private readonly WorkspaceStrip strip;
    private readonly Panel home = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
    private readonly Label status = new Label { Dock = DockStyle.Bottom, Height = 26, Padding = new Padding(18,4,0,0) };
    private readonly System.Windows.Forms.Timer poll = new System.Windows.Forms.Timer { Interval = 100 };
    private readonly Func<int,string> locate;
    private WorkspaceBroker broker;
    private bool closeAll, completeClose;
    private readonly System.Windows.Forms.Timer movement=new System.Windows.Forms.Timer{Interval=16};
    private readonly Stopwatch transition=new Stopwatch();
    internal bool Testing;
    private ListBox recent;
    private static readonly string StateDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AlimentosWilson");
    private static readonly string RecentPath = System.IO.Path.Combine(StateDir,"recent.json");
    internal static readonly Color Red = Color.FromArgb(161,57,54), Gold = Color.FromArgb(168,113,15);
    internal static readonly Image[] Logos = { LoadArt("excel"), LoadArt("word") };
    internal static readonly Image Brand = LoadArt("wilson");

    internal SuiteHost(Func<int,string> find = null, bool testing = false)
    {
        Testing = testing;
        locate = find ?? (Func<int,string>)Distribution.InstalledPath;
        Text = "AWcontrol · Alimentos Wilson";
        Font = new Font("Segoe UI",10);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96,96);
        FormBorderStyle = FormBorderStyle.None;
        MinimumSize = new Size(820,540);
        Size = new Size(1440,900);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(248,248,249);
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        strip = new WorkspaceStrip(this) { Dock = DockStyle.Top, Height = 36 };
        Controls.Add(Stage); Controls.Add(status); Controls.Add(strip);
        Stage.Controls.Add(home);
        MakeHome();
        AllowDrop = true;
        DragEnter += (s,e) => { if(e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect=DragDropEffects.Copy; };
        DragDrop += (s,e) => { var paths=e.Data.GetData(DataFormats.FileDrop) as string[]; if(paths!=null) foreach(var p in paths) OpenDocument(Kind(p),p); };
        Stage.Resize += (s,e) => ResizeEditors();
        poll.Tick += (s,e) => { TickEditors(); strip.RefreshProfile(); };
        poll.Start();
        movement.Tick+=(s,e)=>{if(ActivePage==null || ActivePage.Window==IntPtr.Zero || transition.ElapsedMilliseconds>=150){movement.Stop();ResizeEditors();return;}float t=(float)transition.ElapsedMilliseconds/150;int x=(int)(12*Math.Pow(1-t,3));Native.MoveWindow(ActivePage.Window,x,0,Stage.ClientSize.Width,Stage.ClientSize.Height,true);};
        Shown += (s,e) => { MaximizedBounds=Screen.FromHandle(Handle).WorkingArea; };
        FormClosing += ClosingWorkspace;
        FormClosed += (s,e) => { poll.Dispose(); movement.Dispose(); if(broker!=null) broker.Dispose(); foreach(var p in Pages) p.Process.Dispose(); };
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
        var page=new EditorPage { App=app,Path=path,Title=path==null?(app==0?"Nova planilha":"Novo documento"):System.IO.Path.GetFileName(path) };
        try
        {
            var info=new ProcessStartInfo(exe,path==null?"":Quote(path)) { UseShellExecute=false,WorkingDirectory=System.IO.Path.GetDirectoryName(exe) };
            info.EnvironmentVariables["AWCONTROL_EMBEDDED"]="1";
            info.EnvironmentVariables["AWCONTROL_APP"]=app.ToString();
            if(startup!=null)info.EnvironmentVariables["AWCONTROL_STARTUP"]=startup;
            page.Process=Process.Start(info);
            if(page.Process==null){ status.Text="Não foi possível abrir "+Distribution.Names[app];return; }
            Pages.Add(page);SelectPage(page);status.Text="Abrindo "+page.Title+"…";
            if(path!=null)AddRecent(path);
        }
        catch(Exception e) { if(!(e is System.ComponentModel.Win32Exception || e is IOException || e is UnauthorizedAccessException))throw; status.Text="Não foi possível abrir "+Distribution.Names[app]+": "+e.Message; }
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
        closeAll=false;
        ActivePage=page;
        foreach(var p in Pages) if(p.Window!=IntPtr.Zero) Native.ShowWindow(p.Window,p==page?5:0);
        home.Visible=page==null;
        if(page==null)home.BringToFront();
        else if(page.Window!=IntPtr.Zero){ Native.FocusEditor(page.Window);status.Text=page.Title; }
        ResizeEditors();strip.EnsureVisible(page);strip.Animate();
        if(page!=null && page.Window!=IntPtr.Zero && SystemInformation.IsMenuAnimationEnabled){transition.Restart();movement.Start();}
    }
    internal void ClosePage(EditorPage page)
    {
        if(page.Process.HasExited)return;
        SelectPage(page);
        page.CloseSent=true;
        if(page.Window!=IntPtr.Zero) {Native.PostMessage(page.Window,0x0010,IntPtr.Zero,IntPtr.Zero);status.Text="Fechando página. Confirme o salvamento no editor, se solicitado."; }
    }
    internal void Reorder(EditorPage page, int index)
    { if(index<0 || index>=Pages.Count)return;Pages.Remove(page);Pages.Insert(index,page);strip.Animate(); }
    private void ResizeEditors()
    {
        foreach(var p in Pages)if(p.Window!=IntPtr.Zero && !p.Process.HasExited)
            Native.MoveWindow(p.Window,0,0,Stage.ClientSize.Width,Stage.ClientSize.Height,true);
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
            if(p.Window==IntPtr.Zero)
            {
                IntPtr window=Native.FindWindow(p.Process.Id);
                if(window!=IntPtr.Zero)
                {
                    long style=Native.GetWindowLongPtr(window,-16).ToInt64();
                    Native.SetWindowLongPtr(window,-16,new IntPtr((style & ~0x80CF0000L) | 0x46000000L)); // child, clip siblings
                    Native.SetParent(window,Stage.Handle);
                    if(Native.GetParent(window)!=Stage.Handle) { status.Text="Não foi possível integrar a página. Feche o app e tente novamente.";continue; }
                    long ex=Native.GetWindowLongPtr(window,-20).ToInt64();Native.SetWindowLongPtr(window,-20,new IntPtr(ex & ~0x00040000L));
                    p.Window=window;Native.SetWindowPos(window,IntPtr.Zero,0,0,Stage.ClientSize.Width,Stage.ClientSize.Height,0x0034);
                    Native.ShowWindow(window,p==ActivePage?5:0);ResizeEditors();
                    if(p==ActivePage)Native.FocusEditor(window);
                    if(p.CloseSent)Native.PostMessage(window,0x0010,IntPtr.Zero,IntPtr.Zero);
                    status.Text=p.Title;
                }
                else if(p.Opening.Elapsed.TotalSeconds>90)status.Text="O editor está demorando para iniciar. Aguarde ou feche esta página.";
            }
            else
            {
                var title=new StringBuilder(512);Native.GetWindowText(p.Window,title,title.Capacity);
                string name=title.ToString().Replace(" — Word AW","").Replace(" - Word AW","").Replace(" — Excel AW","").Replace(" - Excel AW","");
                if(name.Length>0 && name!=p.Title){p.Title=name;strip.Animate();}
            }
        }
        if(closeAll)
        {
            if(Pages.Count==0){ completeClose=true;Close(); }
            else if(!Pages.Any(p=>p.CloseSent)){var next=Pages[0];ClosePage(next);closeAll=true;}
        }
    }
    private void ClosingWorkspace(object sender,FormClosingEventArgs e)
    {
        if(completeClose || Pages.Count==0)return;
        e.Cancel=true;
        // Close in document order, so each save/cancel prompt is visible. Cancel keeps the suite alive.
        if(Pages.Any(p=>p.CloseSent)) { foreach(var p in Pages)p.CloseSent=false; }
        ClosePage(ActivePage??Pages[0]);closeAll=true;
    }
    private void ManageApps()
    {
        using(var installer=new Central(app=>OpenDocument(app,null))) installer.ShowDialog(this);
    }
    private void OpenPicker()
    {
        using(var d=new OpenFileDialog { Multiselect=true,Filter="Documentos e planilhas|*.xlsx;*.xlsm;*.csv;*.tsv;*.ods;*.docx;*.doc;*.odt;*.rtf;*.txt;*.md|Todos os arquivos|*.*" })
            if(d.ShowDialog(this)==DialogResult.OK)foreach(var p in d.FileNames)OpenDocument(Kind(p),p);
    }
    private void MakeHome()
    {
        var layout=new TableLayoutPanel { Dock=DockStyle.Top,AutoSize=true,ColumnCount=1,Padding=new Padding(40,34,40,30) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));home.Controls.Add(layout);
        layout.Controls.Add(new Label{Text="Seu trabalho, em um só lugar.",Font=new Font("Segoe UI",28,FontStyle.Bold),AutoSize=true,Margin=new Padding(0,0,0,8)});
        layout.Controls.Add(new Label{Text="Crie e alterne entre planilhas e documentos nas abas acima.",AutoSize=true,ForeColor=Color.FromArgb(95,96,104),Margin=new Padding(0,0,0,26)});
        var cards=new FlowLayoutPanel { AutoSize=true,Dock=DockStyle.Top,WrapContents=true,Margin=new Padding(0) };
        for(int a=0;a<2;a++)
        {
            int app=a;
            var card=new Panel{Size=new Size(324,180),BackColor=Color.White,Margin=new Padding(0,0,20,20),Padding=new Padding(18)};
            card.Controls.Add(new PictureBox{Image=Logos[a],SizeMode=PictureBoxSizeMode.Zoom,Bounds=new Rectangle(18,18,48,48)});
            card.Controls.Add(new Label{Text=Distribution.Names[a],Font=new Font("Segoe UI",18,FontStyle.Bold),AutoSize=true,Location=new Point(80,20)});
            card.Controls.Add(new Label{Text=a==0?"Números, tabelas e ideias organizadas.":"Textos, relatórios e novas histórias.",AutoSize=true,Location=new Point(18,80),ForeColor=Color.DimGray});
            var create=HomeButton(a==0?"Nova planilha":"Novo documento",a==0?Red:Gold);create.Bounds=new Rectangle(18,119,288,42);create.Click+=(s,e)=>OpenDocument(app,null);card.Controls.Add(create);
            cards.Controls.Add(card);
        }
        layout.Controls.Add(cards);
        var actions=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,Margin=new Padding(0,2,0,26)};
        var open=HomeButton("Abrir arquivo…",Red);open.Click+=(s,e)=>OpenPicker();actions.Controls.Add(open);
        var manage=HomeButton("Instalar / atualizar apps",Color.FromArgb(83,85,96));manage.Click+=(s,e)=>ManageApps();actions.Controls.Add(manage);layout.Controls.Add(actions);
        layout.Controls.Add(new Label{Text="Recentes",Font=new Font("Segoe UI",17,FontStyle.Bold),AutoSize=true,Margin=new Padding(0,0,0,12)});
        recent=new ListBox{Dock=DockStyle.Top,Height=220,BorderStyle=BorderStyle.None,BackColor=home.BackColor,ItemHeight=32,Font=new Font("Segoe UI",11)};
        recent.DoubleClick+=(s,e)=>{string p=recent.SelectedItem as string;if(p!=null)OpenDocument(Kind(p),p);};layout.Controls.Add(recent);
        ReadRecent();status.Text="AWcontrol · Excel AW e Word AW";
    }
    private static Button HomeButton(string label,Color color)
    {
        var b=new Button{Text=label,Size=new Size(224,42),Margin=new Padding(0,0,14,0),FlatStyle=FlatStyle.Flat,BackColor=color,ForeColor=Color.White,Cursor=Cursors.Hand};b.FlatAppearance.BorderSize=0;
        using(var path=WorkspaceStrip.Round(new Rectangle(0,0,b.Width,b.Height),9))b.Region=new Region(path);
        b.Resize+=(s,e)=>{using(var p=WorkspaceStrip.Round(new Rectangle(0,0,b.Width,b.Height),9)){var old=b.Region;b.Region=new Region(p);if(old!=null)old.Dispose();}};
        return b;
    }
    private void ReadRecent()
    {
        if(Testing)return;
        try{if(File.Exists(RecentPath) && new FileInfo(RecentPath).Length<131072) {var paths=new JavaScriptSerializer().Deserialize<string[]>(File.ReadAllText(RecentPath));recent.Items.AddRange(paths.Take(40).Cast<object>().ToArray());}}
        catch(Exception e) { if(!(e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is InvalidOperationException))throw;}
    }
    private void AddRecent(string path)
    {
        if(Testing)return;
        recent.Items.Remove(path);recent.Items.Insert(0,path);while(recent.Items.Count>40)recent.Items.RemoveAt(40);
        try{Directory.CreateDirectory(StateDir);File.WriteAllText(RecentPath,new JavaScriptSerializer().Serialize(recent.Items.Cast<string>().ToArray()));}
        catch(Exception e) { if(!(e is IOException || e is UnauthorizedAccessException))throw;}
    }
    protected override void WndProc(ref Message m)
    {
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
    private readonly SuiteHost host;
    private readonly System.Windows.Forms.Timer animation = new System.Windows.Forms.Timer { Interval=16 };
    private readonly Dictionary<Guid,float> positions = new Dictionary<Guid,float>();
    private readonly Dictionary<EditorPage,RectangleF> rectangles=new Dictionary<EditorPage,RectangleF>();
    private EditorPage pressed;
    private Point press;
    private bool moving;
    private float offset;
    private int hovered=-1;
    private Image avatar;
    private DateTime profileStamp;
    private string profileName="Conta";
    private readonly ToolTip tip=new ToolTip();
    internal WorkspaceStrip(SuiteHost h)
    {
        host=h;DoubleBuffered=true;SetStyle(ControlStyles.ResizeRedraw,true);BackColor=Color.FromArgb(59,62,72);
        animation.Tick+=(s,e)=>Invalidate();Disposed+=(s,e)=>{animation.Dispose();tip.Dispose();if(avatar!=null)avatar.Dispose();};AccessibleName="Páginas do AWcontrol";
    }
    internal void RefreshProfile()
    {
        string path=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"AlimentosWilson","profile.json");
        try
        {
            var info=new FileInfo(path);if(!info.Exists || info.LastWriteTimeUtc==profileStamp || info.Length>16*1024*1024)return;profileStamp=info.LastWriteTimeUtc;
            var data=new JavaScriptSerializer{MaxJsonLength=16*1024*1024}.DeserializeObject(File.ReadAllText(path)) as Dictionary<string,object>;
            if(data==null)return;object value;if(data.TryGetValue("name",out value)&&value is string)profileName=(string)value;
            Image next=null;
            if(data.TryGetValue("photo",out value)){var pixels=value as object[];if(pixels!=null && pixels.Length>0 && pixels.Length<4*1024*1024){byte[] bytes=pixels.Select(Convert.ToByte).ToArray();using(var stream=new MemoryStream(bytes))using(var image=Image.FromStream(stream)){if(image.Width<=4096 && image.Height<=4096)next=new Bitmap(image);}}}
            var previous=avatar;avatar=next;if(previous!=null)previous.Dispose();tip.SetToolTip(this,profileName);Invalidate();
        }
        catch(Exception e){if(!(e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is InvalidOperationException || e is FormatException || e is OverflowException))throw;}
    }
    internal void Animate(){animation.Start();Invalidate();}
    internal void EnsureVisible(EditorPage page)
    {if(page!=null){int index=host.Pages.IndexOf(page);float available=Width-350;offset=Math.Max(0,Math.Min(offset,index*190));if((index+1)*190-offset>available)offset=(index+1)*190-available;}else offset=0;}
    internal static GraphicsPath Round(RectangleF r,float radius)
    {var p=new GraphicsPath();float d=radius*2;p.AddArc(r.Left,r.Top,d,d,180,90);p.AddArc(r.Right-d,r.Top,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.Left,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;}
    protected override void OnPaint(PaintEventArgs e)
    {
        var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;rectangles.Clear();bool changing=false;
        DrawPill(g,new RectangleF(4,4,112,28),SuiteHost.Red);g.DrawImage(SuiteHost.Brand,10,7,26,22);TextRenderer.DrawText(g,"AWcontrol",Font,new Rectangle(42,4,72,28),Color.White,TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix);
        var saved=g.Save();g.SetClip(new Rectangle(120,0,Math.Max(0,Width-120-222),Height));
        for(int i=0;i<host.Pages.Count;i++)
        {
            var p=host.Pages[i];float target=120+i*190-offset,current;
            if(!positions.TryGetValue(p.Id,out current))current=target+20;
            float next=SystemInformation.IsMenuAnimationEnabled?current+(target-current)*0.28f:target;if(Math.Abs(target-next)<0.4f)next=target;else changing=true;positions[p.Id]=next;
            var r=new RectangleF(next+2,4,184,28);rectangles[p]=r;
            bool active=host.ActivePage==p;
            if(active)DrawPill(g,r,Color.FromArgb(249,249,250));
            using(var b=new SolidBrush(p.App==0?SuiteHost.Red:SuiteHost.Gold))g.FillRectangle(b,r.Left+8,r.Top,168,2);
            g.DrawImage(SuiteHost.Logos[p.App],r.Left+8,r.Top+6,16,16);
            TextRenderer.DrawText(g,p.Title,Font,new Rectangle((int)r.Left+30,4,126,28),active?Color.FromArgb(40,40,44):Color.White,TextFormatFlags.EndEllipsis|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix);
            using(var pen=new Pen(active?Color.FromArgb(50,50,55):Color.White,1)){float x=r.Right-12,y=r.Top+14;g.DrawLine(pen,x-3,y-3,x+3,y+3);g.DrawLine(pen,x-3,y+3,x+3,y-3);}
        }
        g.Restore(saved);
        int plus=Width-222;using(var font=new Font(Font.FontFamily,16))TextRenderer.DrawText(g,"+",font,new Rectangle(plus,0,36,36),Color.White,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
        var avatarRect=new Rectangle(Width-178,5,26,26);
        if(avatar!=null){var state=g.Save();using(var path=new GraphicsPath()){path.AddEllipse(avatarRect);g.SetClip(path);g.DrawImage(avatar,avatarRect);}g.Restore(state);}
        else {using(var b=new SolidBrush(SuiteHost.Red))g.FillEllipse(b,avatarRect);TextRenderer.DrawText(g,"AW",Font,avatarRect,Color.White,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);}
        for(int i=0;i<3;i++)
        {
            var r=new Rectangle(Width-138+i*46,0,46,36);if(hovered==i)using(var b=new SolidBrush(i==2?Color.FromArgb(196,43,28):Color.FromArgb(82,85,96)))g.FillRectangle(b,r);
            using(var pen=new Pen(Color.White,1))
            {float x=r.Left+23,y=18;if(i==0)g.DrawLine(pen,x-5,y,x+5,y);else if(i==1)g.DrawRectangle(pen,x-5,y-5,10,10);else{g.DrawLine(pen,x-5,y-5,x+5,y+5);g.DrawLine(pen,x-5,y+5,x+5,y-5);}}
        }
        if(!changing)animation.Stop();
        foreach(var id in positions.Keys.Where(id=>!host.Pages.Any(p=>p.Id==id)).ToArray())positions.Remove(id);
    }
    private static void DrawPill(Graphics g,RectangleF r,Color color){using(var path=Round(r,7))using(var b=new SolidBrush(color))g.FillPath(b,path);}
    protected override void OnMouseWheel(MouseEventArgs e){offset=Math.Max(0,Math.Min(Math.Max(0,host.Pages.Count*190-(Width-342)),offset-e.Delta));Animate();}
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);if(e.Button!=MouseButtons.Left)return;
        if(e.X>=Width-138)return;
        pressed=rectangles.FirstOrDefault(p=>p.Value.Contains(e.Location)).Key;press=e.Location;moving=false;
        if(pressed!=null){Capture=true;return;}
        if(e.X>=120 && e.X<Width-222){Native.ReleaseCapture();Native.SendMessage(host.Handle,0xA1,new IntPtr(2),IntPtr.Zero);}
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);hovered=e.X>=Width-138?Math.Min(2,(e.X-(Width-138))/46):-1;
        if(pressed!=null && Capture && (moving || Math.Abs(e.X-press.X)>6))
        {moving=true;int index=Math.Max(0,Math.Min(host.Pages.Count-1,(int)((e.X-120+offset)/190)));if(host.Pages.IndexOf(pressed)!=index)host.Reorder(pressed,index);}
        Invalidate();
    }
    protected override void OnMouseLeave(EventArgs e){hovered=-1;Invalidate();base.OnMouseLeave(e);}
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);Capture=false;if(e.Button!=MouseButtons.Left)return;
        if(pressed!=null)
        {var page=pressed;pressed=null;if(!moving){RectangleF r;if(rectangles.TryGetValue(page,out r) && e.X>r.Right-23)host.ClosePage(page);else host.SelectPage(page);}return;}
        if(e.X<120){host.SelectPage(null);return;}
        if(e.X>=Width-138){int kind=(e.X-(Width-138))/46;if(kind==0)host.WindowState=FormWindowState.Minimized;else if(kind==1)host.WindowState=host.WindowState==FormWindowState.Maximized?FormWindowState.Normal:FormWindowState.Maximized;else host.Close();return;}
        if(e.X>=Width-182 && e.X<Width-138){host.SelectPage(null);return;}
        if(e.X>=Width-222){var menu=new ContextMenuStrip();menu.Items.Add("Nova planilha",null,(s,a)=>host.OpenDocument(0,null));menu.Items.Add("Novo documento",null,(s,a)=>host.OpenDocument(1,null));menu.Closed+=(s,a)=>menu.Dispose();menu.Show(this,new Point(Width-222,Height));}
    }
    protected override void OnDoubleClick(EventArgs e){base.OnDoubleClick(e);var p=PointToClient(Cursor.Position);if(p.X>=120 && p.X<Width-222 && !rectangles.Any(r=>r.Value.Contains(p)))host.WindowState=host.WindowState==FormWindowState.Maximized?FormWindowState.Normal:FormWindowState.Maximized;}
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
    [StructLayout(LayoutKind.Sequential)] internal struct RECT { internal int Left,Top,Right,Bottom; }
    [DllImport("user32.dll")] internal static extern bool GetClientRect(IntPtr h,out RECT rect);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr h);
    internal delegate bool EnumWindow(IntPtr h,IntPtr p);
    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumWindow fn,IntPtr p);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
    [DllImport("user32.dll")] internal static extern IntPtr GetWindow(IntPtr h,uint command);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] internal static extern int GetWindowText(IntPtr h,StringBuilder b,int size);
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] internal static extern IntPtr GetWindowLongPtr(IntPtr h,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")] internal static extern IntPtr SetWindowLongPtr(IntPtr h,int index,IntPtr value);
    [DllImport("user32.dll")] internal static extern IntPtr SetParent(IntPtr child,IntPtr parent);
    [DllImport("user32.dll")] internal static extern IntPtr GetParent(IntPtr h);
    [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr h,int command);
    [DllImport("user32.dll")] internal static extern bool MoveWindow(IntPtr h,int x,int y,int width,int height,bool redraw);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr h,uint message,IntPtr w,IntPtr l);
    [DllImport("user32.dll")] internal static extern IntPtr SendMessage(IntPtr h,uint message,IntPtr w,IntPtr l);
    [DllImport("user32.dll")] internal static extern bool ReleaseCapture();
    [DllImport("user32.dll")] internal static extern IntPtr SetFocus(IntPtr h);
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint current,uint target,bool attach);
    internal static void FocusEditor(IntPtr window)
    {
        uint pid;uint target=GetWindowThreadProcessId(window,out pid),current=GetCurrentThreadId();bool attached=current!=target&&AttachThreadInput(current,target,true);
        try{SetFocus(window);}finally{if(attached)AttachThreadInput(current,target,false);}
    }
    internal static IntPtr FindWindow(int process)
    {
        IntPtr found=IntPtr.Zero;EnumWindows((h,p)=>{uint pid;GetWindowThreadProcessId(h,out pid);if(pid==process && GetWindow(h,4)==IntPtr.Zero){var text=new StringBuilder(512);GetWindowText(h,text,512);if(text.Length>0 && !text.ToString().Contains("IME")){found=h;return false;}}return true;},IntPtr.Zero);return found;
    }
}
