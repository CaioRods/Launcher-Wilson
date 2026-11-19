using System;
using System.Drawing;
using System.Windows.Forms;

// Edge input belongs to the top-level window, never to the document layout.
internal sealed class WindowChrome : IDisposable
{
    private readonly Form owner;private readonly Grip[] grips;
    internal WindowChrome(Form form)
    {
        owner=form;grips=new[]{new Grip(form,13,Cursors.SizeNWSE),new Grip(form,12,Cursors.SizeNS),new Grip(form,14,Cursors.SizeNESW),new Grip(form,10,Cursors.SizeWE),new Grip(form,11,Cursors.SizeWE),new Grip(form,16,Cursors.SizeNESW),new Grip(form,15,Cursors.SizeNS),new Grip(form,17,Cursors.SizeNWSE)};
        owner.Controls.AddRange(grips);owner.SizeChanged+=Arrange;Arrange(null,EventArgs.Empty);
    }
    private void Arrange(object sender,EventArgs e)
    {
        int b=Math.Max(6,(int)(6*owner.DeviceDpi/96f)),w=owner.ClientSize.Width,h=owner.ClientSize.Height;
        Rectangle[] bounds={new Rectangle(0,0,b,b),new Rectangle(b,0,Math.Max(0,w-2*b),b),new Rectangle(w-b,0,b,b),new Rectangle(0,b,b,Math.Max(0,h-2*b)),new Rectangle(w-b,b,b,Math.Max(0,h-2*b)),new Rectangle(0,h-b,b,b),new Rectangle(b,h-b,Math.Max(0,w-2*b),b),new Rectangle(w-b,h-b,b,b)};
        for(int i=0;i<grips.Length;i++){grips[i].Bounds=bounds[i];grips[i].Visible=owner.WindowState==FormWindowState.Normal;grips[i].BringToFront();}
    }
    public void Dispose(){owner.SizeChanged-=Arrange;foreach(var grip in grips)grip.Dispose();}
    private sealed class Grip : Control
    {
        private readonly Form owner;private readonly int hit;
        internal Grip(Form form,int direction,Cursor cursor){owner=form;hit=direction;Cursor=cursor;TabStop=false;SetStyle(ControlStyles.SupportsTransparentBackColor,true);BackColor=Color.Transparent;}
        protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button!=MouseButtons.Left)return;Native.ReleaseCapture();Native.SendMessage(owner.Handle,0x00A1,new IntPtr(hit),IntPtr.Zero);}
    }
}
