using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace ImageMacro
{
    // 안드로이드 에뮬레이터(블루스택 등)에 adb 로 입력을 보내기 위한 헬퍼.
    // 화면 좌표 → 에뮬레이터 창의 렌더 영역 → 안드로이드 화면 좌표 순으로 변환한다.
    public static class Adb
    {
        // ── Win32 ──────────────────────────────────────────────
        delegate bool EnumProc(IntPtr h,IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb,IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr h,EnumProc cb,IntPtr l);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowTextW(IntPtr h,StringBuilder s,int n);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassNameW(IntPtr h,StringBuilder s,int n);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h,out RECT r);
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h,out RECT r);
        [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr h,ref PT p);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [StructLayout(LayoutKind.Sequential)] struct RECT{public int Left,Top,Right,Bottom;}
        [StructLayout(LayoutKind.Sequential)] struct PT{public int X,Y;}
        static Rectangle ToRect(RECT r)=>new Rectangle(r.Left,r.Top,r.Right-r.Left,r.Bottom-r.Top);

        // ── adb 실행 파일 자동 탐색 ────────────────────────────
        // 블루스택은 자체 adb(HD-Adb.exe)를 설치 폴더에 함께 넣어둔다.
        public static string AutoDetectAdbPath()
        {
            var cands=new List<string>();
            string? inst=ReadBsRegistry("InstallDir");
            if(!string.IsNullOrEmpty(inst))cands.Add(Path.Combine(inst,"HD-Adb.exe"));
            foreach(var pf in new[]{Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)}){
                if(string.IsNullOrEmpty(pf))continue;
                cands.Add(Path.Combine(pf,"BlueStacks_nxt","HD-Adb.exe"));
                cands.Add(Path.Combine(pf,"BlueStacks","HD-Adb.exe"));
                cands.Add(Path.Combine(pf,"LDPlayer","LDPlayer9","adb.exe"));
                cands.Add(Path.Combine(pf,"Nox","bin","nox_adb.exe"));
            }
            cands.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                   "Android","Sdk","platform-tools","adb.exe"));
            foreach(var c in cands)if(File.Exists(c))return c;

            // PATH 에서 찾기
            string path=Environment.GetEnvironmentVariable("PATH")??"";
            foreach(var dir in path.Split(';')){
                if(string.IsNullOrWhiteSpace(dir))continue;
                try{var p=Path.Combine(dir.Trim(),"adb.exe");if(File.Exists(p))return p;}catch{}
            }
            return "";
        }

        static string? ReadBsRegistry(string valueName)
        {
            foreach(var key in new[]{@"SOFTWARE\BlueStacks_nxt",@"SOFTWARE\WOW6432Node\BlueStacks_nxt"}){
                try{
                    using var k=Microsoft.Win32.Registry.LocalMachine.OpenSubKey(key);
                    if(k?.GetValue(valueName) is string v&&v.Length>0)return v;
                }catch{}
            }
            return null;
        }

        // ── 블루스택 인스턴스 목록 (bluestacks.conf 의 adb 포트) ──
        public static List<(string name,int port)> BlueStacksInstances()
        {
            var list=new List<(string,int)>();
            var confs=new List<string>();
            string? dataDir=ReadBsRegistry("UserDefinedDir");
            if(!string.IsNullOrEmpty(dataDir))confs.Add(Path.Combine(dataDir,"bluestacks.conf"));
            confs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                                   "BlueStacks_nxt","bluestacks.conf"));
            foreach(var conf in confs){
                if(!File.Exists(conf))continue;
                var ports=new Dictionary<string,int>();
                var names=new Dictionary<string,string>();
                try{
                    foreach(var line in File.ReadAllLines(conf)){
                        var mp=Regex.Match(line,"^bst\\.instance\\.([^.]+)\\.status\\.adb_port=\"(\\d+)\"");
                        if(mp.Success){ports[mp.Groups[1].Value]=int.Parse(mp.Groups[2].Value);continue;}
                        var mn=Regex.Match(line,"^bst\\.instance\\.([^.]+)\\.display_name=\"(.*)\"");
                        if(mn.Success)names[mn.Groups[1].Value]=mn.Groups[2].Value;
                    }
                }catch{continue;}
                foreach(var kv in ports)
                    list.Add((names.TryGetValue(kv.Key,out var dn)&&dn.Length>0?dn+" ("+kv.Key+")":kv.Key,kv.Value));
                if(list.Count>0)break;
            }
            return list;
        }

        // 에뮬레이터가 켜져 있는지 (프로세스 기준)
        public static bool IsEmulatorRunning(out string which)
        {
            which="";
            foreach(var name in new[]{"HD-Player","BlueStacks","BlueStacksAppPlayer","dnplayer","LdVBoxHeadless","Nox","NoxVMHandle"}){
                try{ if(Process.GetProcessesByName(name).Length>0){which=name;return true;} }catch{}
            }
            return false;
        }

        // ── adb 실행 ───────────────────────────────────────────
        public static string Run(string adbPath,string args,int timeoutMs=8000)
        {
            if(string.IsNullOrEmpty(adbPath)||!File.Exists(adbPath))return "[오류] adb 실행 파일을 찾을 수 없습니다.";
            try{
                var psi=new ProcessStartInfo(adbPath,args){
                    UseShellExecute=false,CreateNoWindow=true,
                    RedirectStandardOutput=true,RedirectStandardError=true,
                    StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8
                };
                using var p=Process.Start(psi);
                if(p==null)return "[오류] adb 실행 실패";
                string so=p.StandardOutput.ReadToEnd(),se=p.StandardError.ReadToEnd();
                if(!p.WaitForExit(timeoutMs)){try{p.Kill(true);}catch{}return "[오류] adb 응답 없음(시간 초과)";}
                return (so+se).Trim();
            }catch(Exception ex){return "[오류] "+ex.Message;}
        }

        static string SerialArg(string serial)=>string.IsNullOrWhiteSpace(serial)?"":"-s "+serial.Trim()+" ";

        public static List<string> Devices(string adbPath)
        {
            var list=new List<string>();
            foreach(var line in Run(adbPath,"devices").Split('\n')){
                var t=line.Trim();
                if(t.Length==0||t.StartsWith("List of devices")||t.StartsWith("*")||t.StartsWith("["))continue;
                var parts=t.Split('\t');
                if(parts.Length>=2&&parts[1].Trim()=="device")list.Add(parts[0].Trim());
            }
            return list;
        }

        public static string Connect(string adbPath,string hostPort)=>Run(adbPath,"connect "+hostPort);

        // 현재 회전 상태가 반영된 안드로이드 화면 크기.
        // dumpsys 의 cur=WxH 는 회전까지 반영된 값이라 우선 사용하고, 실패하면 wm size 로 대체한다.
        public static bool DisplaySize(string adbPath,string serial,out int w,out int h,out bool rotationKnown)
        {
            w=h=0; rotationKnown=false;
            string d=Run(adbPath,SerialArg(serial)+"shell dumpsys window displays");
            var mc=Regex.Match(d,"cur=(\\d+)x(\\d+)");
            if(mc.Success){w=int.Parse(mc.Groups[1].Value);h=int.Parse(mc.Groups[2].Value);rotationKnown=true;return true;}
            string s=Run(adbPath,SerialArg(serial)+"shell wm size");
            var ms=Regex.Match(s,"(\\d+)x(\\d+)");
            if(ms.Success){w=int.Parse(ms.Groups[1].Value);h=int.Parse(ms.Groups[2].Value);return true;}
            return false;
        }

        // ── 에뮬레이터 렌더 영역 찾기 ──────────────────────────
        // 블루스택은 안드로이드 화면을 'BlueStacksApp' 자식 창에 그린다.
        // 그 창을 못 찾으면 가장 큰 자식 창, 그것도 없으면 부모 창의 클라이언트 영역을 쓴다.
        public static Rectangle FindEmulatorArea(string titleContains,out string foundTitle)
        {
            foundTitle="";
            IntPtr target=IntPtr.Zero; string targetTitle="";
            string needle=(titleContains??"").Trim();
            EnumWindows((h,l)=>{
                if(!IsWindowVisible(h))return true;
                var sb=new StringBuilder(512); GetWindowTextW(h,sb,sb.Capacity);
                string t=sb.ToString();
                if(t.Length==0)return true;
                if(needle.Length>0&&t.IndexOf(needle,StringComparison.OrdinalIgnoreCase)<0)return true;
                if(GetWindowRect(h,out var wr)){
                    var r=ToRect(wr);
                    if(r.Width<200||r.Height<200)return true;   // 트레이 아이콘/툴팁 등 제외
                }
                target=h; targetTitle=t; return false;
            },IntPtr.Zero);

            if(target==IntPtr.Zero)return Rectangle.Empty;
            foundTitle=targetTitle;

            Rectangle best=Rectangle.Empty; bool exact=false;
            EnumChildWindows(target,(h,l)=>{
                if(!IsWindowVisible(h))return true;
                var cn=new StringBuilder(256); GetClassNameW(h,cn,cn.Capacity);
                if(!GetWindowRect(h,out var cr))return true;
                var r=ToRect(cr);
                if(r.Width<50||r.Height<50)return true;
                if(cn.ToString().IndexOf("BlueStacksApp",StringComparison.OrdinalIgnoreCase)>=0){best=r;exact=true;return false;}
                if((long)r.Width*r.Height>(long)best.Width*best.Height)best=r;
                return true;
            },IntPtr.Zero);

            if(exact||best.Width>0)return best;

            // 자식 창이 없으면 부모 창의 클라이언트 영역
            if(GetClientRect(target,out var cl)){
                var pt=new PT{X=0,Y=0};
                if(ClientToScreen(target,ref pt))
                    return new Rectangle(pt.X,pt.Y,cl.Right-cl.Left,cl.Bottom-cl.Top);
            }
            return Rectangle.Empty;
        }

        // ── 좌표 변환 ──────────────────────────────────────────
        // 화면 좌표 → 안드로이드 좌표. 렌더 영역 밖이면 false.
        public static bool MapToDevice(Rectangle area,int devW,int devH,bool rotationKnown,
                                       int screenX,int screenY,out int ax,out int ay)
        {
            ax=ay=0;
            if(area.Width<=0||area.Height<=0||devW<=0||devH<=0)return false;

            // wm size 는 회전을 반영하지 않으므로, 창 비율과 더 잘 맞는 쪽으로 가로/세로를 바꿔준다.
            if(!rotationKnown){
                double ar=(double)area.Width/area.Height;
                if(Math.Abs(ar-(double)devW/devH)>Math.Abs(ar-(double)devH/devW)){int t=devW;devW=devH;devH=t;}
            }

            double rx=(screenX-area.X)/(double)area.Width;
            double ry=(screenY-area.Y)/(double)area.Height;
            if(rx<0||rx>1||ry<0||ry>1)return false;
            ax=(int)Math.Round(rx*(devW-1));
            ay=(int)Math.Round(ry*(devH-1));
            return true;
        }

        public static string Tap(string adbPath,string serial,int x,int y)
            =>Run(adbPath,SerialArg(serial)+"shell input tap "+x+" "+y);

        // 안드로이드에는 우클릭이 없어서 길게 누르기로 대신한다.
        public static string LongPress(string adbPath,string serial,int x,int y,int ms)
            =>Run(adbPath,SerialArg(serial)+"shell input swipe "+x+" "+y+" "+x+" "+y+" "+ms);
    }
}
