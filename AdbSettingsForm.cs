using System;
using System.Drawing;
using System.Windows.Forms;

namespace ImageMacro
{
    // ADB(안드로이드 에뮬레이터) 클릭 설정 창.
    // 매크로 하나의 Adb* 값을 편집한다.
    public class AdbSettingsForm : Form
    {
        readonly MacroItem _m;

        TextBox txtPath=new(),txtTitle=new();
        ComboBox cmbInst=new(),cmbDev=new();
        RadioButton rdAuto=new(),rdManual=new();
        NumericUpDown nudX=new(),nudY=new(),nudW=new(),nudH=new(),nudLong=new();
        TextBox txtResult=new();

        public AdbSettingsForm(MacroItem m)
        {
            _m=m;
            Text="ADB 클릭 설정 (블루스택 등 안드로이드 에뮬레이터)";
            FormBorderStyle=FormBorderStyle.FixedDialog;
            StartPosition=FormStartPosition.CenterParent;
            MaximizeBox=false; MinimizeBox=false;
            ClientSize=new Size(560,492);
            BackColor=Color.FromArgb(245,245,248);
            Font=new Font("맑은 고딕",9);

            int y=12;
            Add(new Label{Text="① adb 실행 파일",Location=new Point(12,y),AutoSize=true,Font=new Font("맑은 고딕",9,FontStyle.Bold)}); y+=22;
            txtPath.Location=new Point(12,y); txtPath.Size=new Size(360,23); Add(txtPath);
            AddBtn("자동 탐색",380,y-1,80,(s,e)=>{
                string p=Adb.AutoDetectAdbPath();
                if(p.Length==0)Say("adb 실행 파일을 찾지 못했습니다. [찾아보기]로 직접 지정하세요.");
                else{txtPath.Text=p;Say("찾음: "+p);}
            });
            AddBtn("찾아보기",464,y-1,80,(s,e)=>{
                using var d=new OpenFileDialog{Filter="adb 실행 파일|*.exe|모든 파일|*.*",Title="adb 실행 파일 선택"};
                if(d.ShowDialog(this)==DialogResult.OK)txtPath.Text=d.FileName;
            });
            y+=30;

            Add(new Label{Text="② 블루스택 인스턴스 (연결만 해줍니다)",Location=new Point(12,y),AutoSize=true,Font=new Font("맑은 고딕",9,FontStyle.Bold)}); y+=22;
            cmbInst.Location=new Point(12,y); cmbInst.Size=new Size(360,23); cmbInst.DropDownStyle=ComboBoxStyle.DropDownList; Add(cmbInst);
            AddBtn("목록 새로고침",380,y-1,110,(s,e)=>LoadInstances());
            AddBtn("연결",494,y-1,50,(s,e)=>{
                if(cmbInst.SelectedItem is InstItem it){
                    Say(Adb.Connect(PathOrAuto(),"127.0.0.1:"+it.Port));
                    cmbDev.Text="127.0.0.1:"+it.Port;
                    LoadDevices();
                }else Say("연결할 인스턴스를 먼저 고르세요.");
            });
            y+=30;

            Add(new Label{Text="③ 기기 (serial) — 비우면 연결된 첫 번째 기기",Location=new Point(12,y),AutoSize=true,Font=new Font("맑은 고딕",9,FontStyle.Bold)}); y+=22;
            cmbDev.Location=new Point(12,y); cmbDev.Size=new Size(360,23); cmbDev.DropDownStyle=ComboBoxStyle.DropDown; Add(cmbDev);
            AddBtn("기기 새로고침",380,y-1,110,(s,e)=>LoadDevices());
            y+=34;

            var gb=new GroupBox{Text="④ 안드로이드 화면 영역 (화면 좌표 → 안드로이드 좌표 변환에 사용)",
                                Location=new Point(12,y),Size=new Size(532,128),Font=new Font("맑은 고딕",9,FontStyle.Bold)};
            Add(gb);
            rdAuto.Text="창 제목으로 자동 인식 — 제목에 포함된 글자:"; rdAuto.Location=new Point(12,22); rdAuto.AutoSize=true;
            rdAuto.Font=new Font("맑은 고딕",9); gb.Controls.Add(rdAuto);
            txtTitle.Location=new Point(330,20); txtTitle.Size=new Size(120,23); gb.Controls.Add(txtTitle);
            var btnFind=new Button{Text="영역 확인",Location=new Point(456,19),Size=new Size(66,25),Font=new Font("맑은 고딕",8.5f)};
            btnFind.Click+=(s,e)=>{
                var r=Adb.FindEmulatorArea(txtTitle.Text,out string title);
                if(r.Width<=0)Say("창을 찾지 못했습니다. 에뮬레이터가 실행 중인지, 제목 글자가 맞는지 확인하세요.");
                else Say($"창: {title}\r\n렌더 영역: X={r.X} Y={r.Y} {r.Width}x{r.Height}  (비율 {(double)r.Width/r.Height:F3})");
            };
            gb.Controls.Add(btnFind);

            rdManual.Text="직접 지정"; rdManual.Location=new Point(12,54); rdManual.AutoSize=true;
            rdManual.Font=new Font("맑은 고딕",9); gb.Controls.Add(rdManual);
            int mx=90;
            foreach(var (lbl,nud) in new (string,NumericUpDown)[]{("X",nudX),("Y",nudY),("가로",nudW),("세로",nudH)}){
                gb.Controls.Add(new Label{Text=lbl,Location=new Point(mx,58),AutoSize=true,Font=new Font("맑은 고딕",9)});
                nud.Location=new Point(mx+(lbl.Length>1?32:18),54); nud.Size=new Size(70,23);
                nud.Minimum=-32000; nud.Maximum=32000; gb.Controls.Add(nud);
                mx+=(lbl.Length>1?32:18)+78;
            }
            var btnFill=new Button{Text="지금 인식된 창 좌표로 채우기",Location=new Point(12,86),Size=new Size(200,25),Font=new Font("맑은 고딕",8.5f)};
            btnFill.Click+=(s,e)=>{
                var r=Adb.FindEmulatorArea(txtTitle.Text,out string title);
                if(r.Width<=0){Say("창을 찾지 못했습니다.");return;}
                nudX.Value=r.X;nudY.Value=r.Y;nudW.Value=r.Width;nudH.Value=r.Height;
                rdManual.Checked=true;
                Say($"채웠습니다 — {title}: {r.X},{r.Y} {r.Width}x{r.Height}");
            };
            gb.Controls.Add(btnFill);
            gb.Controls.Add(new Label{Text="우클릭 = 길게 누르기(ms):",Location=new Point(228,90),AutoSize=true,Font=new Font("맑은 고딕",9)});
            nudLong.Location=new Point(388,86); nudLong.Size=new Size(70,23); nudLong.Minimum=100; nudLong.Maximum=5000; nudLong.Increment=100;
            gb.Controls.Add(nudLong);
            y+=136;

            AddBtn("연결 테스트",12,y,120,(s,e)=>RunTest());
            Add(new Label{Text="※ 테스트는 탭을 보내지 않고 인식 결과만 확인합니다.",Location=new Point(140,y+5),AutoSize=true,ForeColor=Color.Gray,Font=new Font("맑은 고딕",8.5f)});
            y+=32;
            txtResult.Location=new Point(12,y); txtResult.Size=new Size(532,116); txtResult.Multiline=true; txtResult.ReadOnly=true;
            txtResult.ScrollBars=ScrollBars.Vertical; txtResult.BackColor=Color.White; txtResult.Font=new Font("Consolas",9);
            Add(txtResult);
            y+=124;

            var ok=new Button{Text="확인",Location=new Point(376,y),Size=new Size(80,28),DialogResult=DialogResult.OK};
            var cancel=new Button{Text="취소",Location=new Point(464,y),Size=new Size(80,28),DialogResult=DialogResult.Cancel};
            ok.Click+=(s,e)=>Save();
            Add(ok); Add(cancel); AcceptButton=ok; CancelButton=cancel;

            LoadFrom(_m);
        }

        class InstItem
        {
            public string Name=""; public int Port;
            public override string ToString()=>$"{Name}  —  127.0.0.1:{Port}";
        }

        void Add(Control c)=>Controls.Add(c);
        void AddBtn(string text,int x,int y,int w,EventHandler h)
        {
            var b=new Button{Text=text,Location=new Point(x,y),Size=new Size(w,25),Font=new Font("맑은 고딕",8.5f)};
            b.Click+=h; Controls.Add(b);
        }
        void Say(string s)=>txtResult.Text=s;
        string PathOrAuto()=>txtPath.Text.Trim().Length>0?txtPath.Text.Trim():Adb.AutoDetectAdbPath();

        void LoadFrom(MacroItem m)
        {
            txtPath.Text=m.AdbPath.Length>0?m.AdbPath:Adb.AutoDetectAdbPath();
            txtTitle.Text=m.AdbWindowTitle.Length>0?m.AdbWindowTitle:"BlueStacks";
            rdAuto.Checked=!m.AdbManualArea; rdManual.Checked=m.AdbManualArea;
            nudX.Value=Clamp(m.AdbAreaX);nudY.Value=Clamp(m.AdbAreaY);nudW.Value=Clamp(m.AdbAreaW);nudH.Value=Clamp(m.AdbAreaH);
            nudLong.Value=Math.Clamp(m.AdbLongPressMs,100,5000);
            cmbDev.Text=m.AdbSerial;
            LoadInstances();
            LoadDevices();
        }
        static decimal Clamp(int v)=>Math.Clamp(v,-32000,32000);

        void Save()
        {
            _m.AdbPath=txtPath.Text.Trim();
            _m.AdbSerial=cmbDev.Text.Trim();
            _m.AdbWindowTitle=txtTitle.Text.Trim();
            _m.AdbManualArea=rdManual.Checked;
            _m.AdbAreaX=(int)nudX.Value; _m.AdbAreaY=(int)nudY.Value;
            _m.AdbAreaW=(int)nudW.Value; _m.AdbAreaH=(int)nudH.Value;
            _m.AdbLongPressMs=(int)nudLong.Value;
        }

        void LoadInstances()
        {
            cmbInst.Items.Clear();
            foreach(var (name,port) in Adb.BlueStacksInstances())
                cmbInst.Items.Add(new InstItem{Name=name,Port=port});
            if(cmbInst.Items.Count>0)cmbInst.SelectedIndex=0;
            else Say("블루스택 인스턴스를 찾지 못했습니다 (bluestacks.conf 없음). 기기 주소를 직접 입력하세요.");
        }

        void LoadDevices()
        {
            string keep=cmbDev.Text;
            cmbDev.Items.Clear();
            try{ Cursor=Cursors.WaitCursor;
                foreach(var d in Adb.Devices(PathOrAuto()))cmbDev.Items.Add(d);
            } finally { Cursor=Cursors.Default; }
            cmbDev.Text=keep;
            if(cmbDev.Items.Count==0&&txtResult.Text.Length==0)
                Say("연결된 기기가 없습니다. ②에서 인스턴스를 고르고 [연결]을 눌러보세요.");
        }

        void RunTest()
        {
            try{
                Cursor=Cursors.WaitCursor;
                var sb=new System.Text.StringBuilder();
                string adb=PathOrAuto();
                sb.AppendLine("adb 경로: "+(adb.Length>0?adb:"(못 찾음)"));
                if(adb.Length==0){Say(sb.ToString()+"→ ①에서 adb 실행 파일을 지정하세요.");return;}

                var devs=Adb.Devices(adb);
                sb.AppendLine("연결된 기기: "+(devs.Count>0?string.Join(", ",devs):"(없음)"));
                string serial=cmbDev.Text.Trim();
                if(serial.Length==0&&devs.Count>0)serial=devs[0];
                if(serial.Length==0){Say(sb.ToString()+"→ 기기가 없습니다. ②에서 [연결]을 눌러보세요.");return;}
                sb.AppendLine("사용할 기기: "+serial);

                if(!Adb.DisplaySize(adb,serial,out int dw,out int dh,out bool rot)){
                    Say(sb.ToString()+"→ 안드로이드 화면 크기를 읽지 못했습니다.");return;
                }
                sb.AppendLine($"안드로이드 화면: {dw}x{dh} "+(rot?"(회전 반영됨)":"(회전 정보 없음 — 비율로 추정)"));

                Rectangle area;
                if(rdManual.Checked)area=new Rectangle((int)nudX.Value,(int)nudY.Value,(int)nudW.Value,(int)nudH.Value);
                else{
                    area=Adb.FindEmulatorArea(txtTitle.Text,out string title);
                    sb.AppendLine("찾은 창: "+(title.Length>0?title:"(없음)"));
                }
                if(area.Width<=0||area.Height<=0){Say(sb.ToString()+"→ 렌더 영역을 찾지 못했습니다.");return;}
                sb.AppendLine($"렌더 영역: {area.X},{area.Y} {area.Width}x{area.Height}  ({MonitorOf(area)})");

                double arW=(double)area.Width/area.Height, arD=(double)dw/dh;
                double diff=Math.Min(Math.Abs(arW-arD),Math.Abs(arW-1/arD));
                sb.AppendLine($"화면비: 창 {arW:F3} / 안드로이드 {arD:F3}  → 오차 {diff:F3}"
                              +(diff>0.05?"  [주의] 영역이 정확하지 않을 수 있습니다":"  [정상] 잘 맞습니다"));

                int cx=area.X+area.Width/2, cy=area.Y+area.Height/2;
                if(Adb.MapToDevice(area,dw,dh,rot,cx,cy,out int ax,out int ay))
                    sb.AppendLine($"변환 예시: 화면({cx},{cy}) → 안드로이드({ax},{ay})");
                Say(sb.ToString());
            } finally { Cursor=Cursors.Default; }
        }

        static string MonitorOf(Rectangle r)
        {
            var all=Screen.AllScreens;
            var c=new Point(r.X+r.Width/2,r.Y+r.Height/2);
            for(int i=0;i<all.Length;i++)if(all[i].Bounds.Contains(c))return $"모니터{i+1}{(all[i].Primary?"(주)":"")}";
            return "화면밖";
        }
    }
}
