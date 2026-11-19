using System;
using System.Drawing;
using System.Windows.Forms;

// Edge input belongs to the top-level window, never to the document layout.
internal sealed class WindowChrome : IDisposable
{
    private readonly Form owner;private readonly Grip[] grips;
    private Point origin;private Rectangle initial;private int hit;private bool resizing;
    internal WindowChrome(Form form)
    {
        owner=form;grips=new[]{new Grip(this,13,Cursors.SizeNWSE),new Grip(this,12,Cursors.SizeNS),new Grip(this,14,Cursors.SizeNESW),new Grip(this,10,Cursors.SizeWE),new Grip(this,11,Cursors.SizeWE),new Grip(this,16,Cursors.SizeNESW),new Grip(this,15,Cursors.SizeNS),new Grip(this,17,Cursors.SizeNWSE)};
        owner.Controls.AddRange(grips);owner.SizeChanged+=Arrange;owner.MouseMove+=Move;owner.MouseUp+=End;owner.MouseCaptureChanged+=CaptureChanged;Arrange(null,EventArgs.Empty);
    }
    private void Arrange(object sender,EventArgs e)
    {
        int b=Math.Max(6,(int)(6*owner.DeviceDpi/96f)),w=owner.ClientSize.Width,h=owner.ClientSize.Height;
        Rectangle[] bounds={new Rectangle(0,0,b,b),new Rectangle(b,0,Math.Max(0,w-2*b),b),new Rectangle(w-b,0,b,b),new Rectangle(0,b,b,Math.Max(0,h-2*b)),new Rectangle(w-b,b,b,Math.Max(0,h-2*b)),new Rectangle(0,h-b,b,b),new Rectangle(b,h-b,Math.Max(0,w-2*b),b),new Rectangle(w-b,h-b,b,b)};
        for(int i=0;i<grips.Length;i++){grips[i].Bounds=bounds[i];grips[i].Visible=owner.WindowState==FormWindowState.Normal;grips[i].BringToFront();}
    }
    internal void BeginResize(int direction)
    {
        if(owner.WindowState!=FormWindowState.Normal)return;hit=direction;origin=System.Windows.Forms.Cursor.Position;initial=owner.Bounds;resizing=true;owner.Capture=true;
    }
    private void Move(object sender,MouseEventArgs e){if(resizing)ResizeFromPointer();}
    private void End(object sender,MouseEventArgs e){if(resizing){ResizeFromPointer();resizing=false;owner.Capture=false;}}
    private void CaptureChanged(object sender,EventArgs e){if(!owner.Capture)resizing=false;}
    private void ResizeFromPointer()
    {
        Point current=System.Windows.Forms.Cursor.Position;int dx=current.X-origin.X,dy=current.Y-origin.Y;bool left=hit==10||hit==13||hit==16,right=hit==11||hit==14||hit==17,top=hit==12||hit==13||hit==14,bottom=hit==15||hit==16||hit==17;
        int minW=Math.Max(1,owner.MinimumSize.Width),minH=Math.Max(1,owner.MinimumSize.Height),maxW=owner.MaximumSize.Width>0?owner.MaximumSize.Width:32767,maxH=owner.MaximumSize.Height>0?owner.MaximumSize.Height:32767;
        int width=Math.Max(minW,Math.Min(maxW,initial.Width+(left?-dx:right?dx:0))),height=Math.Max(minH,Math.Min(maxH,initial.Height+(top?-dy:bottom?dy:0)));
        owner.SetBounds(left?initial.Right-width:initial.X,top?initial.Bottom-height:initial.Y,width,height);
    }
    public void Dispose(){owner.SizeChanged-=Arrange;owner.MouseMove-=Move;owner.MouseUp-=End;owner.MouseCaptureChanged-=CaptureChanged;foreach(var grip in grips)grip.Dispose();}
    private sealed class Grip : Control
    {
        private readonly WindowChrome chrome;private readonly int hit;
        internal Grip(WindowChrome controller,int direction,Cursor cursor){chrome=controller;hit=direction;Cursor=cursor;TabStop=false;SetStyle(ControlStyles.SupportsTransparentBackColor,true);BackColor=Color.Transparent;}
        protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button==MouseButtons.Left)chrome.BeginResize(hit);}
    }
}
