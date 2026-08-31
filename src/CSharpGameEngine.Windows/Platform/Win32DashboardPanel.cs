using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
namespace CSharpGameEngine.Platform.Windows;
public sealed record DashboardLine(string Label,string Value);
public sealed record DashboardBar(string Label,int Value,int Maximum);
public sealed record DashboardDocument(string Title,string Subtitle,IReadOnlyList<DashboardLine>Lines,IReadOnlyList<DashboardBar>Bars,string Footer,string MachineStatus);
public sealed class Win32DashboardPanel:IDisposable {
    public const string WindowClassName="CSharpGameEngine.ShowcaseDashboard";
    private const uint WsChild=0x40000000;
    private const uint WsVisible=0x10000000;
    private const uint WsExNoActivate=0x08000000;
    private const uint WmPaint=0x000F;
    private const uint WmEraseBackground=0x0014;
    private const uint WmPrintClient=0x0318;
    private const uint WmNcHitTest=0x0084;
    private const int HtTransparent=-1;
    private const int SwHide=0;
    private const int SwShowNoActivate=4;
    private const int DtLeft=0;
    private const int DtSingleLine=0x20;
    private const int DtNoPrefix=0x800;
    private const int Srccopy=0x00CC0020;
    private static readonly ConcurrentDictionary<nint,Win32DashboardPanel>Panels=[];
    private static readonly object RegistrationLock=new();
    private static readonly WindowProcedure Procedure=WindowProc;
    private static bool Registered;
    private readonly nint Parent;
    private nint HandleValue;
    private DashboardDocument Document=new("C# Game Engine","Loading",[],[],string.Empty,"CGE|page=loading");
    private int LastSuccessfulTextDrawCount;
    private bool Disposed;
    public Win32DashboardPanel(nint Parent){
        if(Parent==0){ throw new ArgumentException("A parent HWND is required.",nameof(Parent)); }
        EnsureRegistered();
        this.Parent=Parent;
        this.HandleValue=CreateWindowEx(WsExNoActivate,WindowClassName,this.Document.MachineStatus,WsChild|WsVisible,0,0,1,1,Parent,0,GetModuleHandle(null),0);
        if(this.HandleValue==0){ throw new InvalidOperationException($"Dashboard CreateWindowEx failed: 0x{Marshal.GetLastWin32Error():X8}."); }
        Panels[this.HandleValue]=this;
        this.Resize();
    }
    public nint Handle=>this.HandleValue;
    public bool Visible { get;private set;}=true;
    public int PixelWidth { get;private set;}
    public int PixelHeight { get;private set;}
    public void Update(DashboardDocument Document){
        ObjectDisposedException.ThrowIf(this.Disposed,this);
        this.Document=Document??throw new ArgumentNullException(nameof(Document));
        _=SetWindowText(this.HandleValue,Document.MachineStatus);
        _=InvalidateRect(this.HandleValue,0,false);
        _=UpdateWindow(this.HandleValue);
        _=SetWindowText(this.HandleValue,$"{Document.MachineStatus}|paint={this.LastSuccessfulTextDrawCount}");
    }
    public void Resize(){
        ObjectDisposedException.ThrowIf(this.Disposed,this);
        _=GetClientRect(this.Parent,out var ParentRect);
        var ParentWidth=Math.Max(1,ParentRect.Right-ParentRect.Left);
        var ParentHeight=Math.Max(1,ParentRect.Bottom-ParentRect.Top);
        var Dpi=(int)Math.Max(96,GetDpiForWindow(this.Parent));
        var Desired=360*Dpi/96;
        this.PixelWidth=Math.Max(1,Math.Min(Desired,ParentWidth*45/100));
        this.PixelHeight=ParentHeight;
        _=MoveWindow(this.HandleValue,ParentWidth-this.PixelWidth,0,this.PixelWidth,this.PixelHeight,true);
        _=SetWindowPos(this.HandleValue,0,0,0,0,0,0x0001|0x0002|0x0010);
    }
    public void SetVisible(bool Visible){
        ObjectDisposedException.ThrowIf(this.Disposed,this);
        this.Visible=Visible;
        _=ShowWindow(this.HandleValue,Visible?SwShowNoActivate:SwHide);
    }
    public void SaveSnapshot(string PathValue){
        ObjectDisposedException.ThrowIf(this.Disposed,this);
        ArgumentException.ThrowIfNullOrWhiteSpace(PathValue);
        _=GetClientRect(this.HandleValue,out var Client);
        var Width=Math.Max(1,Client.Right);
        var Height=Math.Max(1,Client.Bottom);
        var WindowDc=GetDC(this.HandleValue);
        var MemoryDc=CreateCompatibleDC(WindowDc);
        var Info=new NativeBitmapInfo { Header=new NativeBitmapInfoHeader { Size=40,Width=Width,Height=-Height,Planes=1,BitCount=32,SizeImage=Width*Height*4 } };
        var Bitmap=CreateDIBSection(WindowDc,ref Info,0,out var Bits,0,0);
        if(WindowDc==0||MemoryDc==0||Bitmap==0||Bits==0){ throw new InvalidOperationException("Dashboard snapshot DIB allocation failed."); }
        var Old=SelectObject(MemoryDc,Bitmap);
        try {
            this.Render(this.HandleValue,MemoryDc);
            var Bytes=new byte[Info.Header.SizeImage];
            Marshal.Copy(Bits,Bytes,0,Bytes.Length);
            var FullPath=Path.GetFullPath(PathValue);
            Directory.CreateDirectory(Path.GetDirectoryName(FullPath)!);
            var Temporary=FullPath+".tmp-"+Environment.ProcessId;
            using(var Stream=File.Create(Temporary)){
                using var Writer=new BinaryWriter(Stream,Encoding.UTF8,true);
                Writer.Write((byte)'B');Writer.Write((byte)'M');Writer.Write(54+Bytes.Length);Writer.Write((short)0);Writer.Write((short)0);Writer.Write(54);
                Writer.Write(40);Writer.Write(Width);Writer.Write(-Height);Writer.Write((short)1);Writer.Write((short)32);Writer.Write(0);Writer.Write(Bytes.Length);Writer.Write(0);Writer.Write(0);Writer.Write(0);Writer.Write(0);Writer.Write(Bytes);
            }
            File.Move(Temporary,FullPath,true);
        } finally {
            _=SelectObject(MemoryDc,Old);_=DeleteObject(Bitmap);_=DeleteDC(MemoryDc);_=ReleaseDC(this.HandleValue,WindowDc);
        }
    }
    private void Paint(nint Hwnd){
        var PaintDc=BeginPaint(Hwnd,out var Paint);
        if(PaintDc==0){ return; }
        try { this.Render(Hwnd,PaintDc); } finally { _=EndPaint(Hwnd,ref Paint); }
    }
    private void Render(nint Hwnd,nint PaintDc){
        _=GetClientRect(Hwnd,out var Client);
            var Width=Math.Max(1,Client.Right);
            var Height=Math.Max(1,Client.Bottom);
            var MemoryDc=CreateCompatibleDC(PaintDc);
            var Bitmap=CreateCompatibleBitmap(PaintDc,Width,Height);
            var OldBitmap=SelectObject(MemoryDc,Bitmap);
            try {
                Fill(MemoryDc,new NativeRect(0,0,Width,Height),Rgb(16,22,31));
                var Dpi=(int)Math.Max(96,GetDpiForWindow(Hwnd));
                var Scale=Dpi/96f;
                var Padding=(int)(20*Scale);
                var Y=Padding;
                var TitleFont=CreateFont(-(int)(23*Scale),0,0,0,600,0,0,0,1,0,0,5,0,"Segoe UI");
                var BodyFont=CreateFont(-(int)(15*Scale),0,0,0,400,0,0,0,1,0,0,5,0,"Segoe UI");
                var SuccessfulTextDraws=0;
                try {
                    SuccessfulTextDraws+=Draw(MemoryDc,this.Document.Title,Padding,ref Y,Width-Padding*2,(int)(34*Scale),TitleFont,Rgb(235,242,252));
                    SuccessfulTextDraws+=Draw(MemoryDc,this.Document.Subtitle,Padding,ref Y,Width-Padding*2,(int)(26*Scale),BodyFont,Rgb(125,211,252));
                    Y+=(int)(8*Scale);
                    foreach(var Line in this.Document.Lines){
                        SuccessfulTextDraws+=DrawPair(MemoryDc,Line,Padding,ref Y,Width-Padding*2,(int)(23*Scale),BodyFont);
                    }
                    foreach(var Bar in this.Document.Bars){
                        Y+=(int)(8*Scale);
                        SuccessfulTextDraws+=Draw(MemoryDc,$"{Bar.Label}  {Bar.Value}/{Bar.Maximum}",Padding,ref Y,Width-Padding*2,(int)(22*Scale),BodyFont,Rgb(203,213,225));
                        var Track=new NativeRect(Padding,Y,Width-Padding,Math.Min(Height,Y+(int)(8*Scale)));
                        Fill(MemoryDc,Track,Rgb(51,65,85));
                        var Ratio=Bar.Maximum<=0?0:Math.Clamp((double)Bar.Value/Bar.Maximum,0,1);
                        Fill(MemoryDc,new NativeRect(Track.Left,Track.Top,Track.Left+(int)((Track.Right-Track.Left)*Ratio),Track.Bottom),Rgb(56,189,248));
                        Y=Track.Bottom;
                    }
                    var FooterY=Math.Max(Y+(int)(18*Scale),Height-(int)(58*Scale));
                    SuccessfulTextDraws+=Draw(MemoryDc,this.Document.Footer,Padding,ref FooterY,Width-Padding*2,(int)(44*Scale),BodyFont,Rgb(148,163,184));
                    this.LastSuccessfulTextDrawCount=SuccessfulTextDraws;
                } finally {
                    if(TitleFont!=0){ _=DeleteObject(TitleFont); }
                    if(BodyFont!=0){ _=DeleteObject(BodyFont); }
                }
                _=BitBlt(PaintDc,0,0,Width,Height,MemoryDc,0,0,Srccopy);
            } finally {
                if(OldBitmap!=0){ _=SelectObject(MemoryDc,OldBitmap); }
                if(Bitmap!=0){ _=DeleteObject(Bitmap); }
                if(MemoryDc!=0){ _=DeleteDC(MemoryDc); }
            }
    }
    private static int DrawPair(nint Dc,DashboardLine Line,int X,ref int Y,int Width,int Height,nint Font){
        _=SetBkMode(Dc,1);_=SetTextColor(Dc,Rgb(148,163,184));var Old=SelectObject(Dc,Font);
        var Left=new NativeRect(X,Y,X+Width*55/100,Y+Height);var LeftResult=DrawText(Dc,Line.Label,-1,ref Left,DtLeft|DtSingleLine|DtNoPrefix);
        _=SetTextColor(Dc,Rgb(226,232,240));var Right=new NativeRect(X+Width*55/100,Y,X+Width,Y+Height);var RightResult=DrawText(Dc,Line.Value,-1,ref Right,DtLeft|DtSingleLine|DtNoPrefix);
        _=SelectObject(Dc,Old);Y+=Height;return(LeftResult>0?1:0)+(RightResult>0?1:0);
    }
    private static int Draw(nint Dc,string Text,int X,ref int Y,int Width,int Height,nint Font,uint Color){
        _=SetBkMode(Dc,1);_=SetTextColor(Dc,Color);var Old=SelectObject(Dc,Font);var Rect=new NativeRect(X,Y,X+Width,Y+Height);var Result=DrawText(Dc,Text,-1,ref Rect,DtLeft|DtSingleLine|DtNoPrefix);_=SelectObject(Dc,Old);Y+=Height;return Result>0?1:0;
    }
    private static void Fill(nint Dc,NativeRect Rect,uint Color){ var Brush=CreateSolidBrush(Color);try{_=FillRect(Dc,ref Rect,Brush);}finally{_=DeleteObject(Brush);} }
    private static uint Rgb(byte Red,byte Green,byte Blue)=>(uint)(Red|(Green<<8)|(Blue<<16));
    public void Dispose(){
        if(this.Disposed){ return; }
        this.Disposed=true;
        if(this.HandleValue!=0){ Panels.TryRemove(this.HandleValue,out _);_=DestroyWindow(this.HandleValue);this.HandleValue=0; }
    }
    private static void EnsureRegistered(){
        if(Registered){ return; }
        lock(RegistrationLock){
            if(Registered){ return; }
            var Class=new NativeWindowClass { Size=(uint)Marshal.SizeOf<NativeWindowClass>(),Procedure=Marshal.GetFunctionPointerForDelegate(Procedure),Instance=GetModuleHandle(null),ClassName=WindowClassName };
            if(RegisterClassEx(ref Class)==0){ throw new InvalidOperationException($"Dashboard RegisterClassEx failed: 0x{Marshal.GetLastWin32Error():X8}."); }
            Registered=true;
        }
    }
    private static nint WindowProc(nint Hwnd,uint Message,nint WParam,nint LParam){
        if(Message==WmNcHitTest){ return HtTransparent; }
        if(Message==WmEraseBackground){ return 1; }
        if(Message==WmPaint&&Panels.TryGetValue(Hwnd,out var Panel)){ Panel.Paint(Hwnd);return 0; }
        if(Message==WmPrintClient&&Panels.TryGetValue(Hwnd,out Panel)){ Panel.Render(Hwnd,WParam);return 0; }
        return DefWindowProc(Hwnd,Message,WParam,LParam);
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]private delegate nint WindowProcedure(nint Hwnd,uint Message,nint WParam,nint LParam);
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]private struct NativeWindowClass { public uint Size;public uint Style;public nint Procedure;public int ClassExtra;public int WindowExtra;public nint Instance;public nint Icon;public nint Cursor;public nint Background;public string? MenuName;public string ClassName;public nint SmallIcon; }
    [StructLayout(LayoutKind.Sequential)]private struct NativeRect(int Left,int Top,int Right,int Bottom) { public int Left=Left;public int Top=Top;public int Right=Right;public int Bottom=Bottom; }
    [StructLayout(LayoutKind.Sequential)]private struct NativePaint { public nint Dc;public int Erase;public NativeRect Paint;public int Restore;public int Update;[MarshalAs(UnmanagedType.ByValArray,SizeConst=32)]public byte[]Reserved; }
    [StructLayout(LayoutKind.Sequential)]private struct NativeBitmapInfoHeader { public int Size;public int Width;public int Height;public short Planes;public short BitCount;public int Compression;public int SizeImage;public int XPelsPerMeter;public int YPelsPerMeter;public int ColorsUsed;public int ColorsImportant; }
    [StructLayout(LayoutKind.Sequential)]private struct NativeBitmapInfo { public NativeBitmapInfoHeader Header;public uint Color; }
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true,EntryPoint="RegisterClassExW")]private static extern ushort RegisterClassEx(ref NativeWindowClass WindowClass);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true,EntryPoint="CreateWindowExW")]private static extern nint CreateWindowEx(uint ExtendedStyle,string ClassName,string WindowName,uint Style,int X,int Y,int Width,int Height,nint Parent,nint Menu,nint Instance,nint Parameter);
    [DllImport("user32.dll",EntryPoint="DefWindowProcW")]private static extern nint DefWindowProc(nint Hwnd,uint Message,nint WParam,nint LParam);
    [DllImport("user32.dll")]private static extern nint BeginPaint(nint Hwnd,out NativePaint Paint);
    [DllImport("user32.dll")]private static extern nint GetDC(nint Hwnd);
    [DllImport("user32.dll")]private static extern int ReleaseDC(nint Hwnd,nint Dc);
    [DllImport("user32.dll")]private static extern bool EndPaint(nint Hwnd,ref NativePaint Paint);
    [DllImport("user32.dll")]private static extern bool GetClientRect(nint Hwnd,out NativeRect Rect);
    [DllImport("user32.dll")]private static extern uint GetDpiForWindow(nint Hwnd);
    [DllImport("user32.dll")]private static extern bool MoveWindow(nint Hwnd,int X,int Y,int Width,int Height,bool Repaint);
    [DllImport("user32.dll")]private static extern bool SetWindowPos(nint Hwnd,nint InsertAfter,int X,int Y,int Width,int Height,uint Flags);
    [DllImport("user32.dll")]private static extern bool ShowWindow(nint Hwnd,int Command);
    [DllImport("user32.dll")]private static extern bool DestroyWindow(nint Hwnd);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,EntryPoint="SetWindowTextW")]private static extern bool SetWindowText(nint Hwnd,string Text);
    [DllImport("user32.dll")]private static extern bool InvalidateRect(nint Hwnd,nint Rect,bool Erase);
    [DllImport("user32.dll")]private static extern bool UpdateWindow(nint Hwnd);
    [DllImport("user32.dll")]private static extern int FillRect(nint Dc,ref NativeRect Rect,nint Brush);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,EntryPoint="DrawTextW")]private static extern int DrawText(nint Dc,string Text,int Count,ref NativeRect Rect,int Format);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,EntryPoint="GetModuleHandleW")]private static extern nint GetModuleHandle(string? ModuleName);
    [DllImport("gdi32.dll")]private static extern nint CreateCompatibleDC(nint Dc);
    [DllImport("gdi32.dll")]private static extern nint CreateCompatibleBitmap(nint Dc,int Width,int Height);
    [DllImport("gdi32.dll")]private static extern nint CreateDIBSection(nint Dc,ref NativeBitmapInfo Info,uint Usage,out nint Bits,nint Section,uint Offset);
    [DllImport("gdi32.dll")]private static extern nint SelectObject(nint Dc,nint ObjectValue);
    [DllImport("gdi32.dll")]private static extern bool DeleteObject(nint ObjectValue);
    [DllImport("gdi32.dll")]private static extern bool DeleteDC(nint Dc);
    [DllImport("gdi32.dll")]private static extern nint CreateSolidBrush(uint Color);
    [DllImport("gdi32.dll")]private static extern uint SetTextColor(nint Dc,uint Color);
    [DllImport("gdi32.dll")]private static extern int SetBkMode(nint Dc,int Mode);
    [DllImport("gdi32.dll",CharSet=CharSet.Unicode,EntryPoint="TextOutW")]private static extern bool TextOut(nint Dc,int X,int Y,string Text,int Count);
    [DllImport("gdi32.dll")]private static extern bool BitBlt(nint Destination,int X,int Y,int Width,int Height,nint Source,int SourceX,int SourceY,int RasterOperation);
    [DllImport("gdi32.dll",CharSet=CharSet.Unicode,EntryPoint="CreateFontW")]private static extern nint CreateFont(int Height,int Width,int Escapement,int Orientation,int Weight,uint Italic,uint Underline,uint StrikeOut,uint CharacterSet,uint OutputPrecision,uint ClipPrecision,uint Quality,uint PitchAndFamily,string FaceName);
}
