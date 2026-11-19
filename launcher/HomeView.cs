using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Web.Script.Serialization;

internal sealed class HomePreferences
{
    public List<string> Favorites = new List<string>();
    public bool Animations = true;
    public bool Grid;
}

// Home is drawn at logical pixels, scales with the window's DPI, and exposes every action
// to keyboard navigation and accessibility. Files are real, never sample rows in production.
internal sealed class WilsonHome : Control
{
    internal sealed class Target
    {
        internal string Id,Label;
        internal RectangleF Rect;
        internal Action Action;
    }
    private readonly SuiteHost host;
    private readonly List<Target> targets=new List<Target>();
    private readonly Dictionary<string,float> hover=new Dictionary<string,float>();
    private readonly System.Windows.Forms.Timer animation=new System.Windows.Forms.Timer{Interval=16};
    private readonly TextBox search=new TextBox{BorderStyle=BorderStyle.None,Font=new Font("Segoe UI",11),BackColor=Color.White,AccessibleName="Buscar arquivos e modelos"};
    private readonly VScrollBar scroll=new VScrollBar{SmallChange=32,LargeChange=120,Visible=false};
    private readonly List<string> recent=new List<string>();
    private readonly string stateFolder;
    private HomePreferences preferences=new HomePreferences();
    private string page="home",hot="",focus="",person="";
    private Image avatar;
    private float scale=1,offset,contentHeight;
    private bool mainDrawing;
    private string hotGroup="";
    private readonly Dictionary<string,RectangleF> groups=new Dictionary<string,RectangleF>();
    private readonly Dictionary<string,string> dates=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> pendingDates=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly System.Threading.SemaphoreSlim dateSlots=new System.Threading.SemaphoreSlim(3);
    private Point menuPoint;
    private string menuFile;
    internal readonly ContextMenuStrip FileActions=new ContextMenuStrip();
    internal bool Animations {get{return preferences.Animations && SystemInformation.IsMenuAnimationEnabled;}}
    internal string Page {get{return page;}}
    internal string SearchText {get{return search.Text;}set{search.Text=value;}}
    internal string[] RecentFiles {get{return recent.ToArray();}}
    internal bool Favorite(string path){return preferences.Favorites.Contains(path,StringComparer.OrdinalIgnoreCase);}
    internal WilsonHome(SuiteHost owner)
    {
        host=owner;Dock=DockStyle.Fill;BackColor=Color.FromArgb(250,248,247);TabStop=true;AccessibleName="Início do Wilson Control";
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.Selectable,true);
        stateFolder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"AlimentosWilson");
        Controls.Add(search);Controls.Add(scroll);
        search.TextChanged+=(s,e)=>{offset=0;Invalidate();};
        search.HandleCreated+=(s,e)=>Cue(search.Handle,0x1501,new IntPtr(1),"Buscar arquivos e modelos");
        scroll.ValueChanged+=(s,e)=>{offset=scroll.Value;Invalidate();};
        animation.Tick+=(s,e)=>{
            bool changed=false;
            foreach(string key in hover.Keys.ToArray()){float target=key==hot||key==hotGroup?1:0;float next=Animations?hover[key]+(target-hover[key])*0.24f:target;if(Math.Abs(next-target)<0.015f)next=target;if(next!=hover[key])changed=true;hover[key]=next;if(next==0 && key!=hot)hover.Remove(key);}
            Invalidate();if(!changed)animation.Stop();
        };
        FileActions.Items.Add("Abrir",null,(s,e)=>{if(menuFile!=null)host.OpenDocument(SuiteHost.Kind(menuFile),menuFile);});
        FileActions.Items.Add("Adicionar aos favoritos",null,(s,e)=>{if(menuFile!=null)ToggleFavorite(menuFile);});
        FileActions.Items.Add("Remover dos recentes",null,(s,e)=>{if(menuFile!=null){recent.Remove(menuFile);if(!host.Testing)SaveRecent();Invalidate();}});
        Disposed+=(s,e)=>{animation.Dispose();FileActions.Dispose();};
        VisibleChanged+=(s,e)=>{if(!Visible){animation.Stop();hover.Clear();hot=hotGroup="";}};
        if(!owner.Testing)ReadState();
    }
    [System.Runtime.InteropServices.DllImport("user32.dll",EntryPoint="SendMessageW",CharSet=System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr Cue(IntPtr h,int message,IntPtr w,string text);
    internal void UpdateProfile(string name,Image image)
    {name=name=="Conta"?"":name;if(person==name && avatar==image)return;person=name;avatar=image;Invalidate();}
    internal void Navigate(string id){page=id;offset=0;hot="";focus="";Invalidate();}
    internal void AddRecent(string path)
    {
        recent.RemoveAll(p=>string.Equals(p,path,StringComparison.OrdinalIgnoreCase));recent.Insert(0,path);if(recent.Count>40)recent.RemoveRange(40,recent.Count-40);
        if(!host.Testing)SaveRecent();Invalidate();
    }
    internal void ToggleFavorite(string path)
    {if(Favorite(path))preferences.Favorites.RemoveAll(p=>string.Equals(p,path,StringComparison.OrdinalIgnoreCase));else preferences.Favorites.Add(path);SavePreferences();Invalidate();}
    internal string[] FilteredFiles()
    {return recent.Where(p=>(page!="favorites" || Favorite(p))&&(search.Text.Length==0 || Path.GetFileName(p).IndexOf(search.Text,StringComparison.CurrentCultureIgnoreCase)>=0)).ToArray();}
    internal void InvokeAction(string id)
    {var target=targets.FirstOrDefault(t=>t.Id==id);if(target!=null)target.Action();}
    internal RectangleF ActionBounds(string id){var target=targets.FirstOrDefault(t=>t.Id==id);return target==null?RectangleF.Empty:target.Rect;}
    private void ReadState()
    {
        try{
            string path=Path.Combine(stateFolder,"recent.json");
            if(File.Exists(path)&&new FileInfo(path).Length<131072){var files=new JavaScriptSerializer().Deserialize<string[]>(File.ReadAllText(path));if(files!=null)recent.AddRange(files.Where(p=>!string.IsNullOrWhiteSpace(p)).Take(40));}
            path=Path.Combine(stateFolder,"home.json");
            if(File.Exists(path)&&new FileInfo(path).Length<65536){preferences=new JavaScriptSerializer().Deserialize<HomePreferences>(File.ReadAllText(path))??new HomePreferences();if(preferences.Favorites==null)preferences.Favorites=new List<string>();preferences.Favorites=preferences.Favorites.Where(p=>p!=null).Take(200).ToList();}
        }catch(Exception e){if(!DataError(e))throw;}
    }
    private static bool DataError(Exception e){return e is IOException||e is UnauthorizedAccessException||e is ArgumentException||e is InvalidOperationException;}
    private void SaveRecent(){WriteState("recent.json",new JavaScriptSerializer().Serialize(recent));}
    private void SavePreferences(){if(!host.Testing)WriteState("home.json",new JavaScriptSerializer().Serialize(preferences));host.RefreshAnimations();}
    private void WriteState(string name,string value)
    {
        try{Directory.CreateDirectory(stateFolder);string path=Path.Combine(stateFolder,name),temp=path+"."+Guid.NewGuid().ToString("N");File.WriteAllText(temp,value);if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}
        catch(Exception e){if(!DataError(e))throw;host.Notify("Não foi possível salvar as preferências da Home.");}
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);scale=DeviceDpi/96f;var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.TextRenderingHint=TextRenderingHint.ClearTypeGridFit;g.ScaleTransform(scale,scale);
        float w=ClientSize.Width/scale,h=ClientSize.Height/scale,side=w>=1100?244:208;
        targets.Clear();groups.Clear();g.Clear(BackColor);DrawSidebar(g,side,h);
        var saved=g.Save();g.SetClip(new RectangleF(side,0,Math.Max(0,w-side-12),h));g.TranslateTransform(0,-offset);mainDrawing=true;
        float x=side+28,width=Math.Max(300,w-side-56-12);
        if(page=="settings")DrawSettings(g,x,width);
        else if(page=="profile")DrawProfile(g,x,width);
        else DrawWork(g,x,width);
        mainDrawing=false;g.Restore(saved);
        int maximum=Math.Max(0,(int)(contentHeight-h));scroll.SetBounds(ClientSize.Width-12,0,12,ClientSize.Height);
        scroll.Visible=maximum>0;scroll.Maximum=maximum+scroll.LargeChange-1;
        if(offset>maximum){offset=maximum;scroll.Value=maximum;Invalidate();}
    }
    private void Hit(string id,RectangleF rect,string label,Action action)
    {if(mainDrawing)rect.Y-=offset;float side=ClientSize.Width/scale>=1100?244:208;var clip=mainDrawing?new RectangleF(side,0,ClientSize.Width/scale-side-12,ClientSize.Height/scale):new RectangleF(0,0,side,ClientSize.Height/scale);rect=RectangleF.Intersect(rect,clip);if(rect.Width>1&&rect.Height>1)targets.Add(new Target{Id=id,Rect=rect,Label=label,Action=action});}
    private float Hot(string id){float value;return hover.TryGetValue(id,out value)?value:0;}
    private void Card(Graphics g,RectangleF r,string id=null)
    {
        float amount=id==null?0:Hot(id);var shadow=r;shadow.Offset(0,3-amount);
        using(var p=WorkspaceStrip.Round(shadow,14))using(var b=new SolidBrush(Color.FromArgb(15+(int)(amount*8),62,29,24)))g.FillPath(b,p);
        Fill(g,r,Color.White,14);using(var path=WorkspaceStrip.Round(r,14))using(var pen=new Pen(id!=null&&amount>0?Color.FromArgb(90,163,30,39):Color.FromArgb(225,219,217)))g.DrawPath(pen,path);
    }
    private static void Fill(Graphics g,RectangleF r,Color color,float round=0){if(r.Width<=0||r.Height<=0)return;using(var b=new SolidBrush(color)){if(round>0){using(var p=WorkspaceStrip.Round(r,round))g.FillPath(b,p);}else g.FillRectangle(b,r);}}
    internal static void Text(Graphics g,string value,RectangleF rect,float size,Color color,bool bold=false,StringAlignment alignment=StringAlignment.Near,bool wrap=false)
    {using(var f=new Font("Segoe UI",size,bold?FontStyle.Bold:FontStyle.Regular,GraphicsUnit.Pixel))using(var b=new SolidBrush(color))using(var format=new StringFormat{Alignment=alignment,LineAlignment=StringAlignment.Center,Trimming=StringTrimming.EllipsisCharacter,FormatFlags=wrap?0:StringFormatFlags.NoWrap})g.DrawString(value,f,b,rect,format);}
    private static void Line(Graphics g,float x,float y,float end,Color color){using(var p=new Pen(color))g.DrawLine(p,x,y,end,y);}
    private void ActionButton(Graphics g,string id,RectangleF r,string label,Color color,Action action,bool outline=false)
    {
        float amount=Hot(id);if(!outline){using(var path=WorkspaceStrip.Round(r,9))using(var brush=new LinearGradientBrush(r,ControlPaint.Light(color,0.08f+amount*0.13f),color,90))g.FillPath(brush,path);}else{Fill(g,r,Color.FromArgb((int)(amount*18),color),9);using(var path=WorkspaceStrip.Round(r,9))using(var p=new Pen(color,1.1f))g.DrawPath(p,path);}
        Text(g,label,r,15,outline?color:Color.White,true,StringAlignment.Center);FocusRing(g,id,r);Hit(id,r,label,action);
    }
    private void Link(Graphics g,string id,RectangleF r,string label,Color color,Action action)
    {Text(g,label,r,14,color,true);if(Hot(id)>0.1f)Line(g,r.Left,r.Bottom-3,r.Right,Color.FromArgb((int)(Hot(id)*170),color));FocusRing(g,id,r);Hit(id,r,label,action);}
    private void FocusRing(Graphics g,string id,RectangleF r)
    {if(Focused&&focus==id){using(var p=new Pen(Color.FromArgb(150,183,133,27),2){DashStyle=DashStyle.Dot})g.DrawRectangle(p,r.X+2,r.Y+2,r.Width-4,r.Height-4);}}
    internal static void Icon(Graphics g,string name,RectangleF r,Color color)
    {
        using(var p=new Pen(color,1.7f){StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round})
        {
            float x=r.X,y=r.Y,w=r.Width,h=r.Height;
            if(name=="home"){g.DrawLines(p,new[]{new PointF(x,y+h*.45f),new PointF(x+w*.5f,y),new PointF(x+w,y+h*.45f)});g.DrawLines(p,new[]{new PointF(x+w*.15f,y+h*.4f),new PointF(x+w*.15f,y+h),new PointF(x+w*.42f,y+h),new PointF(x+w*.42f,y+h*.67f),new PointF(x+w*.67f,y+h*.67f),new PointF(x+w*.67f,y+h),new PointF(x+w*.85f,y+h),new PointF(x+w*.85f,y+h*.4f)});}
            else if(name=="recent"){g.DrawEllipse(p,r);g.DrawLines(p,new[]{new PointF(x+w*.5f,y+h*.18f),new PointF(x+w*.5f,y+h*.5f),new PointF(x+w*.73f,y+h*.5f)});}
            else if(name=="star"){var points=new PointF[10];for(int i=0;i<10;i++){double a=-Math.PI/2+i*Math.PI/5;float rad=(i%2==0?.5f:.23f)*w;points[i]=new PointF(x+w/2+(float)Math.Cos(a)*rad,y+h/2+(float)Math.Sin(a)*rad);}g.DrawPolygon(p,points);}
            else if(name=="apps"||name=="grid"){for(int a=0;a<2;a++)for(int b=0;b<2;b++)g.DrawRectangle(p,x+a*w*.57f,y+b*h*.57f,w*.34f,h*.34f);}
            else if(name=="search"){g.DrawEllipse(p,x,y,w*.68f,h*.68f);g.DrawLine(p,x+w*.6f,y+h*.6f,x+w,y+h);}
            else if(name=="folder"){g.DrawLines(p,new[]{new PointF(x,y+h*.25f),new PointF(x,y+h),new PointF(x+w,y+h),new PointF(x+w,y+h*.25f),new PointF(x+w*.45f,y+h*.25f),new PointF(x+w*.3f,y),new PointF(x,y),new PointF(x,y+h*.25f)});}
            else if(name=="settings"){g.DrawEllipse(p,x+w*.16f,y+h*.16f,w*.68f,h*.68f);g.DrawEllipse(p,x+w*.37f,y+h*.37f,w*.26f,h*.26f);for(int i=0;i<8;i++){double a=i*Math.PI/4;g.DrawLine(p,x+w/2+(float)Math.Cos(a)*w*.37f,y+h/2+(float)Math.Sin(a)*h*.37f,x+w/2+(float)Math.Cos(a)*w*.5f,y+h/2+(float)Math.Sin(a)*h*.5f);}}
            else if(name=="list"){for(int i=0;i<3;i++)g.DrawLine(p,x,y+i*h*.4f,x+w,y+i*h*.4f);}
            else{g.DrawRectangle(p,x+w*.12f,y,w*.75f,h);for(int i=0;i<3;i++)g.DrawLine(p,x+w*.3f,y+h*(.3f+i*.2f),x+w*.7f,y+h*(.3f+i*.2f));}
        }
    }
    private void DrawSidebar(Graphics g,float w,float h)
    {
        using(var brush=new LinearGradientBrush(new RectangleF(0,0,w,h),Color.FromArgb(164,0,25),Color.FromArgb(90,5,19),90))g.FillRectangle(brush,0,0,w,h);
        using(var shape=new GraphicsPath()){shape.AddPolygon(new[]{new PointF(0,h*.25f),new PointF(w,h*.12f),new PointF(w,h*.5f),new PointF(0,h*.83f)});using(var b=new SolidBrush(Color.FromArgb(17,255,96,94)))g.FillPath(b,shape);}
        using(var wave=new GraphicsPath()){wave.AddBezier(0,h-9,w*.35f,h-38,w*.6f,h-12,w,h-35);wave.AddLine(w,h-35,w,h);wave.AddLine(w,h,0,h);wave.CloseFigure();using(var b=new LinearGradientBrush(new RectangleF(0,h-38,w,38),Color.FromArgb(255,201,48),Color.FromArgb(225,137,11),0f))g.FillPath(b,wave);}
        float logo=w>220?76:60;g.DrawImage(SuiteHost.Brand,14,24,logo,48);Text(g,"Wilson Control",new RectangleF(22+logo,26,w-logo-32,42),w>220?17:14,Color.White,true);
        string[] ids={"home","recent","favorites","models"},labels={"Início","Recentes","Favoritos","Modelos"},icons={"home","recent","star","file"};
        float navStart=h<640?96:110,step=h<560?37:h<640?42:50;
        for(int i=0;i<ids.Length;i++)Nav(g,w,navStart+i*step,ids[i],labels[i],icons[i]);
        float bottom=Math.Max(navStart+step*4+2,h-250);Line(g,24,bottom,w-24,Color.FromArgb(70,255,255,255));
        Nav(g,w,bottom+14,"apps","Aplicativos","apps");Nav(g,w,bottom+64,"settings","Configurações","settings");Line(g,24,bottom+125,w-24,Color.FromArgb(70,255,255,255));
        var profile=new RectangleF(14,h-108,w-28,72);Fill(g,profile,Color.FromArgb(45+(int)(Hot("profile")*24),255,255,255),12);
        DrawAvatar(g,new RectangleF(26,h-93,40,40));Text(g,string.IsNullOrWhiteSpace(person)?"Minha conta":person,new RectangleF(78,h-96,w-98,28),14,Color.White,true);Text(g,"Conta pessoal",new RectangleF(78,h-69,w-98,20),12,Color.FromArgb(240,223,219));Hit("profile",profile,"Conta pessoal",()=>Navigate("profile"));FocusRing(g,"profile",profile);
    }
    private void Nav(Graphics g,float w,float y,string id,string label,string icon)
    {
        float rowHeight=ClientSize.Height/scale<560?34:42;bool selected=page==id;var r=new RectangleF(14,y,w-28,rowHeight);float alpha=(selected?50:0)+Hot(id)*24;
        if(alpha>0)Fill(g,r,Color.FromArgb((int)alpha,255,255,255),10);if(selected)Fill(g,new RectangleF(14,y+6,4,rowHeight-12),Color.FromArgb(255,201,48),2);
        Icon(g,icon,new RectangleF(34,y+(rowHeight-21)/2,21,21),selected?Color.FromArgb(255,219,137):Color.White);Text(g,label,new RectangleF(72,y,w-90,rowHeight),16,Color.White,selected);
        Hit(id,r,label,()=>{if(id=="apps")host.ManageApps();else Navigate(id);});FocusRing(g,id,r);
    }
    private void DrawAvatar(Graphics g,RectangleF r)
    {
        if(avatar!=null){var state=g.Save();using(var path=new GraphicsPath()){path.AddEllipse(r);g.SetClip(path);g.DrawImage(avatar,r);}g.Restore(state);}else{using(var b=new SolidBrush(Color.FromArgb(220,224,219)))g.FillEllipse(b,r);Text(g,string.IsNullOrWhiteSpace(person)?"AW":string.Concat(person.Split(' ').Where(p=>p.Length>0).Take(2).Select(p=>p[0])),r,15,Color.FromArgb(113,21,31),true,StringAlignment.Center);}
    }
    private void Header(Graphics g,float x,float width,string title,string subtitle)
    {
        Text(g,title,new RectangleF(x,20,width,48),34,Color.FromArgb(24,24,27),true);Text(g,subtitle,new RectangleF(x,64,width,28),18,Color.FromArgb(103,102,105),true);
    }
    private float SearchBox(Graphics g,float x,float width)
    {
        float boxWidth=width>980?Math.Min(450,width*.46f):width;float y=width>980?28:104;float left=width>980?x+width-boxWidth:x;
        var r=new RectangleF(left,y,boxWidth,44);Fill(g,new RectangleF(left,y+3,boxWidth,44),Color.FromArgb(15,60,24,20),12);Fill(g,r,Color.White,12);
        using(var path=WorkspaceStrip.Round(r,12))using(var p=new Pen(Color.FromArgb(195,190,187)))g.DrawPath(p,path);Icon(g,"search",new RectangleF(left+15,y+12,19,19),Color.DimGray);
        search.Visible= y-offset>=0 && y-offset+44<ClientSize.Height/scale;
        search.SetBounds((int)((left+44)*scale),(int)((y+11-offset)*scale),(int)((boxWidth-58)*scale),(int)(24*scale));
        return width>980?108:166;
    }
    private void DrawWork(Graphics g,float x,float width)
    {
        string first=person.Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();int hour=DateTime.Now.Hour;string greeting=(hour<12?"Bom dia":hour<18?"Boa tarde":"Boa noite")+(first==null?".":", "+first+".");
        Header(g,x,width>980?width-Math.Min(450,width*.46f)-24:width,page=="home"?greeting:page=="models"?"Modelos para começar.":page=="favorites"?"Seus favoritos.":"Arquivos recentes.","Crie ou abra um arquivo.");
        float top=SearchBox(g,x,width);
        if(page=="models"){DrawModelGallery(g,x,top,width);return;}
        if(page!="home"){float height=DrawRecent(g,x,top,width,false);contentHeight=top+height+30;return;}
        bool pair=width>=690;float cardWidth=pair?(width-18)/2:width;
        AppCard(g,new RectangleF(x,top,cardWidth,118),0);AppCard(g,new RectangleF(pair?x+cardWidth+18:x,pair?top:top+134,cardWidth,118),1);
        float rowsY=top+(pair?142:276);float rowsH=DrawRecent(g,x,rowsY,width,true);
        Link(g,"allrecent",new RectangleF(x+width-155,rowsY+rowsH+8,155,32),"Ver todos os recentes ›",Color.FromArgb(130,18,33),()=>Navigate("recent"));
        contentHeight=rowsY+rowsH+56;
    }
    private void AppCard(Graphics g,RectangleF r,int app)
    {
        string id="app"+app;var group=r;group.Y-=offset;groups[id]=group;float lift=Animations?Hot(id):0;r.Y-=lift;Card(g,r,id);
        float left=r.X+88;Color accent=app==0?Color.FromArgb(170,8,33):Color.FromArgb(168,111,8);
        g.DrawImage(SuiteHost.Logos[app],r.X+22,r.Y+17,48,48);
        Text(g,Distribution.Names[app],new RectangleF(left,r.Y+16,r.Width-110,34),25,Color.FromArgb(28,26,27),true);
        float actionWidth=r.Width-40,buttonWidth=Math.Min(186,actionWidth*.57f);
        ActionButton(g,id+"new",new RectangleF(r.X+20,r.Y+68,buttonWidth,34),app==0?"Nova planilha":"Novo documento",accent,()=>host.OpenDocument(app,null));
        Link(g,id+"open",new RectangleF(r.X+buttonWidth+32,r.Y+68,actionWidth-buttonWidth-12,34),app==0?"Abrir planilha":"Abrir documento",accent,()=>host.OpenPicker(app));
    }

    private float DrawRecent(Graphics g,float x,float y,float width,bool compact)
    {
        var files=FilteredFiles();int count=compact?Math.Min(files.Length,preferences.Grid?4:5):files.Length;float body=preferences.Grid?Math.Max(154,(float)Math.Ceiling(count/(width>=700?3f:2f))*132):Math.Max(168,count*40+44);float height=body+60;
        var r=new RectangleF(x,y,width,height);Card(g,r);Icon(g,"folder",new RectangleF(x+18,y+19,23,20),Color.FromArgb(103,29,33));Text(g,page=="favorites"?"Favoritos":"Arquivos recentes",new RectangleF(x+54,y+8,width-230,42),22,Color.FromArgb(29,25,27),true);
        Link(g,"openfile",new RectangleF(x+width-220,y+12,116,32),"Abrir arquivo",Color.FromArgb(130,18,33),()=>host.OpenPicker());
        foreach(bool grid in new[]{false,true}){bool choice=grid;var rect=new RectangleF(x+width-(grid?44:80),y+15,28,28);if(preferences.Grid==grid||Hot(grid?"grid":"list")>0)Fill(g,rect,Color.FromArgb(247,231,231),6);Icon(g,grid?"grid":"list",new RectangleF(rect.X+6,rect.Y+6,16,16),Color.FromArgb(125,35,41));Hit(grid?"grid":"list",rect,grid?"Exibir em grade":"Exibir em lista",()=>{preferences.Grid=choice;SavePreferences();Invalidate();});}
        if(count==0){Text(g,search.Text.Length>0?"Nenhum arquivo encontrado.":page=="favorites"?"Marque a estrela de um arquivo para salvá-lo aqui.":"Os arquivos que você abrir aparecerão aqui.",new RectangleF(x+24,y+89,width-48,48),15,Color.FromArgb(117,111,111));return height;}
        if(preferences.Grid)
        {
            int cols=width>=700?3:2;float cw=(width-36-(cols-1)*12)/cols;
            for(int i=0;i<count;i++){string path=files[i];var tile=new RectangleF(x+18+(i%cols)*(cw+12),y+60+(i/cols)*132,cw,118);Fill(g,tile,Hot("file"+i)>0?Color.FromArgb(251,240,237):Color.FromArgb(250,248,247),9);g.DrawImage(SuiteHost.Logos[SuiteHost.Kind(path)],tile.X+12,tile.Y+14,32,32);Text(g,Path.GetFileName(path),new RectangleF(tile.X+12,tile.Y+56,cw-24,27),14,Color.FromArgb(44,35,36),true);Text(g,Folder(path),new RectangleF(tile.X+12,tile.Y+83,cw-24,21),12,Color.Gray);Hit("file"+i,tile,"Abrir "+Path.GetFileName(path),()=>host.OpenDocument(SuiteHost.Kind(path),path));}
        }
        else
        {
            float nameWidth=width*.47f,localWidth=width*.22f;var header=new RectangleF(x+16,y+56,width-32,32);Fill(g,header,Color.FromArgb(241,238,236),7);
            Text(g,"Nome",new RectangleF(x+30,y+56,nameWidth-20,32),13,Color.FromArgb(91,87,88),true);Text(g,"Local",new RectangleF(x+nameWidth,y+56,localWidth,32),13,Color.FromArgb(91,87,88),true);if(width>=620)Text(g,"Modificado",new RectangleF(x+nameWidth+localWidth,y+56,width*.2f,32),13,Color.FromArgb(91,87,88),true);
            for(int i=0;i<count;i++)
            {
                string path=files[i];float row=y+91+i*40;var rect=new RectangleF(x+16,row,width-32,38);if(Hot("file"+i)>0)Fill(g,rect,Color.FromArgb((int)(Hot("file"+i)*23),164,30,33),6);
                g.DrawImage(SuiteHost.Logos[SuiteHost.Kind(path)],x+29,row+7,24,24);Text(g,Path.GetFileName(path),new RectangleF(x+63,row,nameWidth-75,38),14,Color.FromArgb(43,37,38),true);Text(g,Folder(path),new RectangleF(x+nameWidth,row,localWidth-12,38),13,Color.FromArgb(102,96,96));if(width>=620)Text(g,Modified(path),new RectangleF(x+nameWidth+localWidth,row,width*.2f-24,38),13,Color.FromArgb(102,96,96));
                Hit("file"+i,new RectangleF(rect.X,rect.Y,rect.Width-78,rect.Height),"Abrir "+Path.GetFileName(path),()=>host.OpenDocument(SuiteHost.Kind(path),path));FocusRing(g,"file"+i,rect);
                var star=new RectangleF(x+width-86,row+4,30,30);Text(g,Favorite(path)?"★":"☆",star,24,Favorite(path)?Color.FromArgb(199,137,8):Color.FromArgb(114,106,107),false,StringAlignment.Center);Hit("favorite"+i,star,(Favorite(path)?"Remover dos favoritos: ":"Favoritar: ")+Path.GetFileName(path),()=>ToggleFavorite(path));
                var more=new RectangleF(x+width-49,row+4,30,30);Text(g,"···",more,23,Color.FromArgb(89,79,80),true,StringAlignment.Center);Hit("more"+i,more,"Mais opções: "+Path.GetFileName(path),()=>FileMenu(path));Line(g,x+25,row+39,x+width-25,Color.FromArgb(239,234,231));
            }
        }
        return height;
    }
    private static string Folder(string path){try{return new DirectoryInfo(Path.GetDirectoryName(path)).Name;}catch(ArgumentException){return "—";}}
    private static string ReadModified(string path){try{var info=new FileInfo(path);if(!info.Exists)return "Indisponível";DateTime t=info.LastWriteTime;if(t.Date==DateTime.Today)return "Hoje, "+t.ToString("HH:mm");if(t.Date==DateTime.Today.AddDays(-1))return "Ontem";return t.ToString("dd/MM/yyyy");}catch(IOException){return "—";}catch(UnauthorizedAccessException){return "—";}catch(ArgumentException){return "—";}}
    private string Modified(string path)
    {
        lock(dates){string date;if(dates.TryGetValue(path,out date))return date;if(!pendingDates.Add(path))return "…";}
        System.Threading.Tasks.Task.Run(async ()=>{await dateSlots.WaitAsync();try{string result=ReadModified(path);lock(dates){dates[path]=result;pendingDates.Remove(path);}if(!IsDisposed&&IsHandleCreated)try{BeginInvoke((Action)Invalidate);}catch(InvalidOperationException){}}finally{dateSlots.Release();}});
        return "…";
    }
    private void FileMenu(string path)
    {
        menuFile=path;FileActions.Items[1].Text=Favorite(path)?"Remover dos favoritos":"Adicionar aos favoritos";
        // Keep the menu alive until the owning Home is disposed. Disposing on Closed can
        // interrupt ToolStrip's own OnItemClicked/reparent handling and recreate a dead HWND.
        FileActions.Show(this,menuPoint);
    }

    private void ModelRow(Graphics g,RectangleF r,int app,string key,string title)
    {
        string id="model"+key;if(Hot(id)>0)Fill(g,r,Color.FromArgb(249,239,232),8);Miniature(g,new RectangleF(r.X+2,r.Y+4,76,55),app);Text(g,title,new RectangleF(r.X+90,r.Y+8,r.Width-90,51),14,Color.FromArgb(54,43,44),true,StringAlignment.Near,true);Hit(id,r,"Criar "+title,()=>host.OpenTemplate(app,key));FocusRing(g,id,r);
    }
    private static void Miniature(Graphics g,RectangleF r,int app)
    {Fill(g,r,Color.White,6);using(var path=WorkspaceStrip.Round(r,6))using(var p=new Pen(Color.FromArgb(207,196,187)))g.DrawPath(p,path);Fill(g,new RectangleF(r.X+10,r.Y+9,r.Width-20,5),app==0?Color.FromArgb(174,29,43):Color.FromArgb(185,128,22),1);using(var p=new Pen(Color.FromArgb(215,210,204))){for(int i=0;i<4;i++)g.DrawLine(p,r.X+10,r.Y+22+i*7,r.Right-10,r.Y+22+i*7);if(app==0)for(int i=0;i<4;i++)g.DrawLine(p,r.X+10+i*(r.Width-20)/3,r.Y+18,r.X+10+i*(r.Width-20)/3,r.Bottom-9);}}
    private void DrawModelGallery(Graphics g,float x,float y,float width)
    {
        string[] keys={"budget","report","sales","letter"},names={"Orçamento mensal","Relatório profissional","Controle de vendas","Carta profissional"};int[] apps={0,1,0,1};int cols=width>900?3:2;float cw=(width-(cols-1)*18)/cols;int index=0;
        for(int i=0;i<keys.Length;i++){int k=i;if(search.Text.Length>0&&names[i].IndexOf(search.Text,StringComparison.CurrentCultureIgnoreCase)<0)continue;var r=new RectangleF(x+(index%cols)*(cw+18),y+(index/cols)*204,cw,184);Card(g,r,"model"+keys[i]);Miniature(g,new RectangleF(r.X+20,r.Y+20,r.Width-40,105),apps[i]);Text(g,names[i],new RectangleF(r.X+20,r.Y+133,r.Width-40,34),17,Color.FromArgb(85,33,36),true);Hit("model"+keys[i],r,"Criar "+names[i],()=>host.OpenTemplate(apps[k],keys[k]));index++;}
        if(index==0)Text(g,"Nenhum modelo encontrado.",new RectangleF(x,y,width,48),16,Color.DimGray);contentHeight=y+(float)Math.Ceiling(index/(double)cols)*204+30;
    }
    private void DrawSettings(Graphics g,float x,float width)
    {
        search.Visible=false;Header(g,x,width,"Do seu jeito.","Preferências do Wilson Control.");var r=new RectangleF(x,112,width,100);Card(g,r);Text(g,"Animações suaves",new RectangleF(x+24,128,width-160,30),20,Color.FromArgb(62,35,38),true);Text(g,"Transições e destaques ao passar o mouse.",new RectangleF(x+24,159,width-160,30),15,Color.DimGray);ActionButton(g,"motion",new RectangleF(r.Right-122,140,98,36),preferences.Animations?"Ativadas":"Desativadas",preferences.Animations?Color.FromArgb(155,18,34):Color.Gray,()=>{preferences.Animations=!preferences.Animations;SavePreferences();Invalidate();});
        var list=new RectangleF(x,232,width,118);Card(g,list);Text(g,"Exibição de arquivos",new RectangleF(x+24,244,width-48,35),20,Color.FromArgb(62,35,38),true);ActionButton(g,"settinglist",new RectangleF(x+24,293,155,36),"Lista",Color.FromArgb(139,20,35),()=>{preferences.Grid=false;SavePreferences();Invalidate();},preferences.Grid);ActionButton(g,"settinggrid",new RectangleF(x+194,293,155,36),"Grade",Color.FromArgb(139,20,35),()=>{preferences.Grid=true;SavePreferences();Invalidate();},!preferences.Grid);contentHeight=390;
    }
    private void DrawProfile(Graphics g,float x,float width)
    {
        search.Visible=false;Header(g,x,width,"Sua conta.","Mesmo perfil no Excel AW e no Word AW.");var r=new RectangleF(x,116,width,200);Card(g,r);DrawAvatar(g,new RectangleF(x+28,144,70,70));Text(g,string.IsNullOrWhiteSpace(person)?"Minha conta":person,new RectangleF(x+122,140,width-150,42),26,Color.FromArgb(56,32,35),true);Text(g,"Conta pessoal",new RectangleF(x+122,186,width-150,30),16,Color.DimGray);ActionButton(g,"editname",new RectangleF(x+28,247,190,40),"Editar nome",Color.FromArgb(152,19,35),EditName);Text(g,"A foto e as imagens do perfil podem ser alteradas no menu Conta dos aplicativos.",new RectangleF(x,340,width,36),14,Color.DimGray);contentHeight=408;
    }
    private void EditName()
    {
        using(var dialog=new Form{Text="Nome do perfil",FormBorderStyle=FormBorderStyle.FixedDialog,StartPosition=FormStartPosition.CenterParent,ClientSize=new Size(380,150),MaximizeBox=false,MinimizeBox=false})
        {var field=new TextBox{Text=person,MaxLength=100,Bounds=new Rectangle(20,25,340,28)};var save=new Button{Text="Salvar",DialogResult=DialogResult.OK,Bounds=new Rectangle(250,94,110,32)};dialog.Controls.Add(field);dialog.Controls.Add(save);dialog.AcceptButton=save;if(dialog.ShowDialog(host)!=DialogResult.OK)return;
            try{string path=Path.Combine(stateFolder,"profile.json");var data=new Dictionary<string,object>();if(File.Exists(path)){if(new FileInfo(path).Length>16*1024*1024)throw new IOException();data=new JavaScriptSerializer{MaxJsonLength=16*1024*1024}.DeserializeObject(File.ReadAllText(path)) as Dictionary<string,object>??data;}data["name"]=field.Text.Trim();WriteState("profile.json",new JavaScriptSerializer{MaxJsonLength=16*1024*1024}.Serialize(data));}
            catch(Exception e){if(!DataError(e))throw;host.Notify("Não foi possível salvar o nome do perfil.");}
        }
    }
    private Target At(Point location){var point=new PointF(location.X/scale,location.Y/scale);return targets.LastOrDefault(t=>t.Rect.Contains(point));}
    protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);menuPoint=e.Location;var target=At(e.Location);string next=target==null?"":target.Id;var point=new PointF(e.X/scale,e.Y/scale);string group=groups.FirstOrDefault(p=>p.Value.Contains(point)).Key??"";Cursor=target==null?Cursors.Default:Cursors.Hand;if(next==hot&&group==hotGroup)return;hot=next;hotGroup=group;foreach(string key in new[]{hot,hotGroup})if(key.Length>0&&!hover.ContainsKey(key))hover[key]=0;animation.Start();}
    protected override void OnMouseLeave(EventArgs e){hot="";hotGroup="";Cursor=Cursors.Default;animation.Start();base.OnMouseLeave(e);}
    protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);if(e.Button!=MouseButtons.Left)return;var target=At(e.Location);if(target==null)return;focus=target.Id;Focus();target.Action();Invalidate();}
    protected override void OnMouseWheel(MouseEventArgs e){base.OnMouseWheel(e);if(scroll.Visible)scroll.Value=Math.Max(0,Math.Min(scroll.Maximum-scroll.LargeChange+1,scroll.Value-e.Delta/2));}
    protected override bool ProcessDialogKey(Keys keyData)
    {if((keyData&Keys.KeyCode)==Keys.Tab && targets.Count>0){int index=targets.FindIndex(t=>t.Id==focus);index=(index+((keyData&Keys.Shift)!=0?-1:1)+targets.Count)%targets.Count;focus=targets[index].Id;Focus();Invalidate();return true;}return base.ProcessDialogKey(keyData);}
    protected override bool ProcessCmdKey(ref Message msg,Keys keyData)
    {if(keyData==(Keys.Control|Keys.F)){if(!search.Visible){Navigate("home");Refresh();}search.Focus();return true;}if(keyData==(Keys.Control|Keys.O)){host.OpenPicker();return true;}if(keyData==Keys.Enter||keyData==Keys.Space){var target=targets.FirstOrDefault(t=>t.Id==focus);if(target!=null){target.Action();return true;}}return base.ProcessCmdKey(ref msg,keyData);}
    protected override AccessibleObject CreateAccessibilityInstance(){return new HomeAccessible(this);}
    private sealed class HomeAccessible : ControlAccessibleObject
    {
        private readonly WilsonHome home;internal HomeAccessible(WilsonHome h):base(h){home=h;}
        public override int GetChildCount(){return base.GetChildCount()+home.targets.Count;}
        public override AccessibleObject GetChild(int index){int count=base.GetChildCount();if(index<count)return base.GetChild(index);index-=count;return index<0||index>=home.targets.Count?null:new TargetAccessible(home,home.targets[index],this);}
    }
    private sealed class TargetAccessible : AccessibleObject
    {
        private readonly WilsonHome home;private readonly Target target;private readonly AccessibleObject parent;
        internal TargetAccessible(WilsonHome h,Target t,AccessibleObject p){home=h;target=t;parent=p;}
        public override string Name {get{return target.Label;}set{}}
        public override AccessibleRole Role {get{return AccessibleRole.PushButton;}}
        public override AccessibleObject Parent {get{return parent;}}
        public override Rectangle Bounds {get{return home.RectangleToScreen(new Rectangle((int)(target.Rect.X*home.scale),(int)(target.Rect.Y*home.scale),(int)(target.Rect.Width*home.scale),(int)(target.Rect.Height*home.scale)));}}
        public override AccessibleStates State {get{return AccessibleStates.Focusable|(home.focus==target.Id?AccessibleStates.Focused:AccessibleStates.None);}}
        public override string DefaultAction {get{return "Abrir";}}
        public override void DoDefaultAction(){if(home.InvokeRequired)home.BeginInvoke(target.Action);else target.Action();}
    }
}
