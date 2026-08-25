using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Windows.Forms;
using OpenCvSharp;

namespace ImageMacro
{
    public partial class Form1 : Form
    {
        [DllImport("user32.dll")] static extern void mouse_event(uint f, int x, int y, uint d, IntPtr e);
        [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT pt);
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mod, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);
        [DllImport("user32.dll")] static extern void keybd_event(byte bVk, byte bScan, uint flags, IntPtr extra);
        [DllImport("user32.dll")] static extern short VkKeyScan(char ch);
        [DllImport("user32.dll")] static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint dwAffinity);
        [DllImport("user32.dll",SetLastError=true)] static extern uint SendInput(uint n,INPUT[] a,int sz);
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT pt);
        [DllImport("user32.dll")] static extern bool ScreenToClient(IntPtr hwnd,ref POINT pt);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd,uint msg,IntPtr wp,IntPtr lp);
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT{public ushort wVk,wScan;public uint dwFlags,time;public IntPtr extra;}
        [StructLayout(LayoutKind.Explicit)] struct INPUT{[FieldOffset(0)]public uint type;[FieldOffset(8)]public KEYBDINPUT ki;}

        const uint LDOWN=0x0002,LUP=0x0004,RDOWN=0x0008,RUP=0x0010,KEY_UP=0x0002;
        const int  WM_HOTKEY=0x0312,HK_START=1,HK_STOP=2;
        const uint MOD_NONE=0,MOD_ALT=1,MOD_CTRL=2,MOD_SHIFT=4;
        const uint WDA_EXCLUDEFROMCAPTURE=0x11;
        const uint WM_LBUTTONDOWN=0x0201,WM_LBUTTONUP=0x0202,WM_RBUTTONDOWN=0x0204,WM_RBUTTONUP=0x0205;
        const int  WIN_W=1060,WIN_H=780,LEFT_W=190,MID_W=305,MID_X=LEFT_W;
        const int  RIGHT_X=LEFT_W+MID_W,RIGHT_W=WIN_W-LEFT_W-MID_W;

        List<MacroItem> _macros=new(); MacroItem? _current=null; MacroStep? _selStep=null;
        MacroStep? _clipboard=null; volatile bool _isRunning=false; Thread? _thread=null;
        bool _settingHk=false; int _settingHkTarget=0; bool _suppressStepEvt=false;
        volatile bool _restartRequested=false; bool _capturingStepHk=false; bool _pickingPos=false;
        volatile ClickMode _clickMode=ClickMode.Normal;
        volatile int _searchMonitor=0;                  // 0=전체, 1..N=해당 모니터만
        // ADB 클릭용 (실행 시작 시 확정)
        string _adbPath="",_adbSerial="",_adbTitle="BlueStacks";
        bool _adbManual=false,_adbRotKnown=false; Rectangle _adbManualRect=Rectangle.Empty;
        int _adbW=0,_adbH=0,_adbLongMs=600;
        Rectangle _adbArea=Rectangle.Empty; long _adbAreaAt=0;
        int _selectedCardIdx=-1;

        ListBox  lstMacros=new();
        Button   btnNewMacro=new(),btnDelMacro=new(),btnSave=new(),btnLoad=new(),btnRun=new(),btnStop=new();
        Panel    pnlMid=new(); Panel pnlStepFlow=new();
        Button   btnAddStep=new(),btnDelStep=new(),btnStepUp=new(),btnStepDn=new(),btnFlowView=new();
        TextBox  txtName=new(); NumericUpDown nudScanMs=new(),nudRepeat=new(),nudLoopDelay=new();
        ComboBox cmbLoopUnit=new();
        Label    lblStartHk=new(),lblStopHk=new(); Button btnSetStart=new(),btnSetStop=new();
        RadioButton rdTOStop=new(),rdTORestart=new(); NumericUpDown nudRestartDelay=new(); Label lblRestartDelay=new();
        Panel    pnlRightScroll=new(),pnlRightInner=new();
        ComboBox cmbStepType=new();
        CheckBox chkEnabled=new(),chkStartOff=new(); NumericUpDown nudWaitAfter=new(); NumericUpDown nudJump=new(); TextBox txtWatchTargets=new();
        // image panel
        Panel    pnlImage=new(); PictureBox picPreview=new();
        Button   btnPickImg=new(),btnTestImg=new();
        Label    lblImgPath=new(),lblTestResult=new();
        RadioButton rdClickL=new(),rdClickR=new();
        NumericUpDown nudClicks=new(),nudCDelay=new(),nudTimeout=new();
        TrackBar tbConf=new(); Label lblConf=new();
        Panel    pnlClickPos=new(); Label lblClickInfo=new(); Button btnResetClick=new();
        NumericUpDown nudOffsetX=new(),nudOffsetY=new();
        NumericUpDown nudGroupId=new(); Panel pnlGroupRow=new();
        System.Drawing.Point? _previewClickPt=null;
        // key panel
        Panel   pnlKey=new(); TextBox txtKeyText=new(),txtHotKey=new(); Label lblHkHint=new();
        // move panel
        Panel   pnlMove=new(); NumericUpDown nudMoveX=new(),nudMoveY=new();
        RadioButton rdMoveAbs=new(),rdMoveRel=new(); Button btnPickPos=new(); Label lblPickPos=new();
        RadioButton rdMoveOnly=new(),rdMoveLeft=new(),rdMoveRight=new();
        // delay panel
        Panel   pnlDelay=new(); NumericUpDown nudDelayMs=new(); ComboBox cmbDelayUnit=new(); Label lblDelayHint=new();
        // notification panel
        Panel   pnlNotif=new(); TextBox txtNotifText=new();
        // 스텝 켜고 끄기
        Panel   pnlToggleRow=new(); TextBox txtToggleTargets=new(); ComboBox cmbToggleAction=new();
        Panel   pnlClickCfg=new(),pnlConfRow=new(); Label lblPreviewHint=new(); int _confRowY=0;
        ComboBox cmbClickMode=new(); Button btnAdbCfg=new(); ComboBox cmbMonitor=new(); ToolTip? _tips=null;
        CheckBox chkEventMode=new(); CheckBox chkAntiCapture=new();
        Label   lblStatus=new();

        public Form1(){InitializeComponent();BuildUI();}

        void BuildUI()
        {
            Text="이미지 자동화 매크로"; ClientSize=new System.Drawing.Size(WIN_W,WIN_H);
            FormBorderStyle=FormBorderStyle.Sizable; MaximizeBox=true; BackColor=Color.FromArgb(245,245,248);
            BuildLeft(); BuildMid(); BuildRight();
            this.Load+=(s,e)=>{this.MinimumSize=this.Size; SetWindowDisplayAffinity(this.Handle,WDA_EXCLUDEFROMCAPTURE); chkAntiCapture.Checked=true; HookInputCommit(this);};
            this.MouseDown+=(s,e)=>DropFocus();
            // 모니터를 꽂거나 빼거나 해상도를 바꾸면 '검색 범위' 목록을 다시 만든다
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged+=OnDisplaySettingsChanged;
            KeyPreview=true; KeyDown+=Form1_KeyDown;
            KeyDown+=(s,e)=>{
                if(_settingHk)return;
                if(_capturingStepHk){OnStepHkKeyDown(e);return;}
                if(e.Control&&e.KeyCode==Keys.C&&_selStep!=null){_clipboard=MacroItem.CloneStep(_selStep);e.Handled=true;}
                if(e.Control&&e.KeyCode==Keys.V&&_clipboard!=null&&_current!=null){int ins=_selectedCardIdx>=0?_selectedCardIdx+1:_current.Steps.Count;_current.Steps.Insert(ins,MacroItem.CloneStep(_clipboard));RefreshStepList(ins);e.Handled=true;}
            };
        }

        void BuildLeft()
        {
            var p=new Panel{Location=new System.Drawing.Point(0,0),Size=new System.Drawing.Size(LEFT_W,WIN_H),BackColor=Color.FromArgb(38,38,42)};
            p.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Bottom;
            Controls.Add(p);
            p.Controls.Add(new Label{Text="매크로 목록",ForeColor=Color.White,Font=new Font("맑은 고딕",10,FontStyle.Bold),Location=new System.Drawing.Point(10,12),AutoSize=true});
            lstMacros.Location=new System.Drawing.Point(8,38); lstMacros.Size=new System.Drawing.Size(174,507);
            lstMacros.BackColor=Color.FromArgb(52,52,58); lstMacros.ForeColor=Color.White; lstMacros.BorderStyle=BorderStyle.None; lstMacros.Font=new Font("맑은 고딕",9);
            lstMacros.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Bottom|AnchorStyles.Right;
            lstMacros.SelectedIndexChanged+=OnMacroSelected; p.Controls.Add(lstMacros);
            DkBtn(p,btnNewMacro,"+ 새 매크로",553,Color.FromArgb(55,130,55),OnNewMacro); btnNewMacro.Anchor=AnchorStyles.Bottom|AnchorStyles.Left;
            DkBtn(p,btnDelMacro,"X 삭제",     586,Color.FromArgb(130,55,55),OnDelMacro); btnDelMacro.Anchor=AnchorStyles.Bottom|AnchorStyles.Left;
            DkBtn(p,btnSave,   "파일로 저장",   626,Color.FromArgb(55,85,145),OnSave);     btnSave.Anchor=AnchorStyles.Bottom|AnchorStyles.Left;
            DkBtn(p,btnLoad,   "파일 불러오기", 659,Color.FromArgb(55,85,145),OnLoadFile); btnLoad.Anchor=AnchorStyles.Bottom|AnchorStyles.Left;
            p.Controls.Add(new Label{Location=new System.Drawing.Point(8,702),Size=new System.Drawing.Size(174,1),BackColor=Color.FromArgb(80,80,88),Anchor=AnchorStyles.Bottom|AnchorStyles.Left|AnchorStyles.Right});
            DkBtn(p,btnRun,  "▶ 실행",712,Color.FromArgb(45,150,75),OnRun);   btnRun.Anchor=AnchorStyles.Bottom|AnchorStyles.Left;
            DkBtn(p,btnStop, "■ 정지", 746,Color.FromArgb(170,55,55),OnStopMacro); btnStop.Anchor=AnchorStyles.Bottom|AnchorStyles.Left;
            btnRun.Enabled=false; btnStop.Enabled=false;
        }

        void BuildMid()
        {
            pnlMid.Location=new System.Drawing.Point(MID_X,0); pnlMid.Size=new System.Drawing.Size(MID_W,WIN_H); pnlMid.BackColor=Color.White;
            pnlMid.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Bottom;
            Controls.Add(pnlMid);
            pnlMid.Controls.Add(new Label{Text="실행 흐름",Font=new Font("맑은 고딕",10,FontStyle.Bold),ForeColor=Color.FromArgb(50,50,60),Location=new System.Drawing.Point(10,10),AutoSize=true});
            btnFlowView.Text="전체 흐름 보기"; btnFlowView.Location=new System.Drawing.Point(MID_W-126,6); btnFlowView.Size=new System.Drawing.Size(118,24); btnFlowView.FlatStyle=FlatStyle.Flat; btnFlowView.FlatAppearance.BorderColor=Color.FromArgb(120,120,180); btnFlowView.Font=new Font("맑은 고딕",8); btnFlowView.ForeColor=Color.FromArgb(60,60,120); btnFlowView.BackColor=Color.FromArgb(235,235,248); btnFlowView.Cursor=Cursors.Hand; btnFlowView.Click+=(s,e)=>ShowFlowWindow(); pnlMid.Controls.Add(btnFlowView);

            // 카드 기반 스텝 플로우
            int flowH=WIN_H-38-64-360;
            pnlStepFlow.Location=new System.Drawing.Point(4,34); pnlStepFlow.Size=new System.Drawing.Size(MID_W-8,flowH);
            pnlStepFlow.AutoScroll=true; pnlStepFlow.BackColor=Color.FromArgb(248,248,252);
            pnlStepFlow.BorderStyle=BorderStyle.FixedSingle;
            pnlStepFlow.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Bottom;
            pnlMid.Controls.Add(pnlStepFlow);

            // 라벨은 LX, 입력칸은 모두 IX 에서 시작 — 글자 길이와 상관없이 세로 줄을 맞춘다
            const int LX=8,IX=126,IW=MID_W-IX-8;
            int by=34+flowH+4,bw=69;
            MdBtn(pnlMid,btnAddStep,"+ 추가",LX,by,Color.FromArgb(55,130,55),(s,e)=>AddStepOfType(StepType.Sequential)); btnAddStep.Anchor=AnchorStyles.Bottom|AnchorStyles.Left;
            MdBtn(pnlMid,btnDelStep,"X 삭제",LX+bw,by,Color.FromArgb(130,55,55),OnDelStep); btnDelStep.Anchor=AnchorStyles.Bottom|AnchorStyles.Left;
            MdBtn(pnlMid,btnStepUp,"▲ 위로",LX+bw*2,by,Color.FromArgb(80,80,140),OnStepUp); btnStepUp.Anchor=AnchorStyles.Bottom|AnchorStyles.Left;
            MdBtn(pnlMid,btnStepDn,"▼ 아래",LX+bw*3,by,Color.FromArgb(80,80,140),OnStepDn); btnStepDn.Anchor=AnchorStyles.Bottom|AnchorStyles.Left;
            var tipStep=new ToolTip{AutoPopDelay=12000,InitialDelay=400,ReshowDelay=200};
            tipStep.SetToolTip(btnAddStep,"스텝 추가"); tipStep.SetToolTip(btnDelStep,"스텝 삭제");
            tipStep.SetToolTip(btnStepUp,"선택한 스텝을 위로"); tipStep.SetToolTip(btnStepDn,"선택한 스텝을 아래로");
            _tips=tipStep;

            int cy=by+34;
            // 라벨 + 입력칸 한 줄을 같은 규칙으로 배치
            void Row(string label,Control c,int h=23){
                var l=MkL(label,LX,cy+(h-15)/2); l.Anchor=AnchorStyles.Bottom|AnchorStyles.Left; pnlMid.Controls.Add(l);
                c.Location=new System.Drawing.Point(IX,cy); c.Anchor=AnchorStyles.Bottom|AnchorStyles.Left; pnlMid.Controls.Add(c);
            }

            txtName.Size=new System.Drawing.Size(IW,23); txtName.TextChanged+=OnNameChanged;
            Row("매크로 이름:",txtName); cy+=29;

            nudScanMs.Size=new System.Drawing.Size(80,23); nudScanMs.Minimum=100; nudScanMs.Maximum=5000; nudScanMs.Value=300;
            nudScanMs.ValueChanged+=(s,e)=>{if(_current!=null)_current.ScanInterval=(int)nudScanMs.Value;};
            Row("다시 찾는 간격(ms):",nudScanMs); cy+=29;

            nudRepeat.Size=new System.Drawing.Size(80,23); nudRepeat.Minimum=0; nudRepeat.Maximum=9999; nudRepeat.Value=0;
            nudRepeat.ValueChanged+=(s,e)=>{if(_current!=null)_current.RepeatCount=(int)nudRepeat.Value;};
            Row("반복 횟수(0=무한):",nudRepeat); cy+=29;

            // 한 번 다 돌고 다음 반복까지 쉬는 시간 — 숫자 + 단위
            nudLoopDelay.Size=new System.Drawing.Size(80,23); nudLoopDelay.Minimum=0; nudLoopDelay.Maximum=9999; nudLoopDelay.Value=0;
            nudLoopDelay.ValueChanged+=(s,e)=>{if(_suppressStepEvt)return;if(_current!=null)_current.LoopDelay=(int)nudLoopDelay.Value;};
            Row("반복 사이 대기:",nudLoopDelay);
            cmbLoopUnit.Location=new System.Drawing.Point(IX+86,cy); cmbLoopUnit.Size=new System.Drawing.Size(85,23);
            cmbLoopUnit.DropDownStyle=ComboBoxStyle.DropDownList; cmbLoopUnit.Font=new Font("맑은 고딕",8.5f);
            cmbLoopUnit.Items.AddRange(new object[]{"밀리초","초","분"}); cmbLoopUnit.SelectedIndex=1;
            cmbLoopUnit.SelectedIndexChanged+=(s,e)=>{if(_suppressStepEvt)return;if(_current!=null)_current.LoopDelayUnit=cmbLoopUnit.SelectedIndex;};
            cmbLoopUnit.Anchor=AnchorStyles.Bottom|AnchorStyles.Left; pnlMid.Controls.Add(cmbLoopUnit); cy+=32;

            // 이미지를 못 찾았을 때 — 라디오를 세로로 쌓아 글자가 잘리지 않게 한다
            var pTO=new Panel{Location=new System.Drawing.Point(LX,cy),Size=new System.Drawing.Size(MID_W-16,90),BorderStyle=BorderStyle.FixedSingle,BackColor=Color.FromArgb(248,248,252)};
            pTO.Anchor=AnchorStyles.Bottom|AnchorStyles.Left;
            pnlMid.Controls.Add(pTO);
            pTO.Controls.Add(new Label{Text="이미지를 못 찾으면:",Font=new Font("맑은 고딕",8.5f,FontStyle.Bold),Location=new System.Drawing.Point(8,6),AutoSize=true});
            rdTOStop.Text="매크로 정지"; rdTOStop.Location=new System.Drawing.Point(10,24); rdTOStop.AutoSize=true; rdTOStop.Checked=true; rdTOStop.Font=new Font("맑은 고딕",8.5f); rdTOStop.ForeColor=Color.FromArgb(160,40,40); rdTOStop.CheckedChanged+=OnTimeoutActionChanged; pTO.Controls.Add(rdTOStop);
            rdTORestart.Text="처음부터 다시 실행"; rdTORestart.Location=new System.Drawing.Point(10,44); rdTORestart.AutoSize=true; rdTORestart.Font=new Font("맑은 고딕",8.5f); rdTORestart.ForeColor=Color.FromArgb(0,120,50); rdTORestart.CheckedChanged+=OnTimeoutActionChanged; pTO.Controls.Add(rdTORestart);
            lblRestartDelay.Text="다시 실행 전 대기(ms):"; lblRestartDelay.Location=new System.Drawing.Point(10,68); lblRestartDelay.AutoSize=true; lblRestartDelay.ForeColor=Color.Gray; pTO.Controls.Add(lblRestartDelay);
            nudRestartDelay.Location=new System.Drawing.Point(IX+20,65); nudRestartDelay.Size=new System.Drawing.Size(80,23); nudRestartDelay.Minimum=0; nudRestartDelay.Maximum=30000; nudRestartDelay.Value=1000; nudRestartDelay.ValueChanged+=(s,e)=>{if(_current!=null)_current.RestartDelay=(int)nudRestartDelay.Value;}; pTO.Controls.Add(nudRestartDelay);
            cy+=98;

            lblStartHk.Size=new System.Drawing.Size(IW-56,23); lblStartHk.BorderStyle=BorderStyle.FixedSingle; lblStartHk.BackColor=Color.White; lblStartHk.Text="F5"; lblStartHk.TextAlign=ContentAlignment.MiddleLeft;
            Row("실행 단축키:",lblStartHk);
            btnSetStart.Text="변경"; btnSetStart.Location=new System.Drawing.Point(IX+IW-50,cy-1); btnSetStart.Size=new System.Drawing.Size(50,25); btnSetStart.Click+=(s,e)=>BeginHkCapture(1); btnSetStart.Anchor=AnchorStyles.Bottom|AnchorStyles.Left; pnlMid.Controls.Add(btnSetStart); cy+=29;

            lblStopHk.Size=new System.Drawing.Size(IW-56,23); lblStopHk.BorderStyle=BorderStyle.FixedSingle; lblStopHk.BackColor=Color.White; lblStopHk.Text="F6"; lblStopHk.TextAlign=ContentAlignment.MiddleLeft;
            Row("정지 단축키:",lblStopHk);
            btnSetStop.Text="변경"; btnSetStop.Location=new System.Drawing.Point(IX+IW-50,cy-1); btnSetStop.Size=new System.Drawing.Size(50,25); btnSetStop.Click+=(s,e)=>BeginHkCapture(2); btnSetStop.Anchor=AnchorStyles.Bottom|AnchorStyles.Left; pnlMid.Controls.Add(btnSetStop); cy+=31;

            cmbMonitor.Size=new System.Drawing.Size(IW,23); cmbMonitor.DropDownStyle=ComboBoxStyle.DropDownList; cmbMonitor.Font=new Font("맑은 고딕",8.5f);
            cmbMonitor.SelectedIndexChanged+=(s,e)=>{if(_suppressStepEvt)return;if(_current!=null)_current.SearchMonitor=cmbMonitor.SelectedIndex;_searchMonitor=cmbMonitor.SelectedIndex;};
            Row("검색 범위:",cmbMonitor); FillMonitorCombo(); cy+=29;

            cmbClickMode.Size=new System.Drawing.Size(IW-88,23); cmbClickMode.DropDownStyle=ComboBoxStyle.DropDownList; cmbClickMode.Font=new Font("맑은 고딕",8.5f);
            cmbClickMode.Items.AddRange(new object[]{"일반 클릭","커서 없이 클릭","ADB(에뮬레이터)"});
            cmbClickMode.SelectedIndex=0;
            cmbClickMode.SelectedIndexChanged+=(s,e)=>{
                if(_suppressStepEvt)return;
                var m=(ClickMode)cmbClickMode.SelectedIndex;
                if(_current!=null){_current.ClickMode=m;_current.BackgroundClick=m==ClickMode.Background;}
                btnAdbCfg.Enabled=m==ClickMode.Adb;
                if(m==ClickMode.Adb&&_current!=null)TryAutoConnectAdb(_current,true);
            };
            Row("클릭 방식:",cmbClickMode);
            btnAdbCfg.Text="ADB 설정"; btnAdbCfg.Location=new System.Drawing.Point(IX+IW-84,cy-1); btnAdbCfg.Size=new System.Drawing.Size(84,25); btnAdbCfg.Font=new Font("맑은 고딕",8.5f); btnAdbCfg.Enabled=false;
            btnAdbCfg.Click+=(s,e)=>{if(_current==null)return;using var f=new AdbSettingsForm(_current);f.ShowDialog(this);};
            btnAdbCfg.Anchor=AnchorStyles.Bottom|AnchorStyles.Left; pnlMid.Controls.Add(btnAdbCfg); cy+=31;

            chkEventMode.Text="항상 감시 모드 (순서 없이 뜨면 바로 클릭)"; chkEventMode.Location=new System.Drawing.Point(LX,cy); chkEventMode.AutoSize=true; chkEventMode.Font=new Font("맑은 고딕",8.5f); chkEventMode.ForeColor=Color.FromArgb(0,100,160); chkEventMode.CheckedChanged+=(s,e)=>{if(_current!=null)_current.EventMode=chkEventMode.Checked;}; chkEventMode.Anchor=AnchorStyles.Bottom|AnchorStyles.Left; pnlMid.Controls.Add(chkEventMode); cy+=24;
            chkAntiCapture.Text="화면 녹화 숨김 모드"; chkAntiCapture.Location=new System.Drawing.Point(LX,cy); chkAntiCapture.AutoSize=true; chkAntiCapture.Font=new Font("맑은 고딕",8.5f); chkAntiCapture.ForeColor=Color.FromArgb(140,60,60);
            chkAntiCapture.CheckedChanged+=(s,e)=>SetWindowDisplayAffinity(this.Handle,chkAntiCapture.Checked?WDA_EXCLUDEFROMCAPTURE:0);
            tipStep.SetToolTip(nudScanMs,"이미지를 못 찾았을 때 다음 번 찾기까지 쉬는 시간입니다.\n짧을수록 빨리 반응하지만 CPU를 더 씁니다.");
            tipStep.SetToolTip(nudLoopDelay,"스텝을 한 바퀴 다 돌고 나서 다음 반복까지 쉬는 시간입니다.");
            tipStep.SetToolTip(chkEventMode,"켜면 스텝을 순서대로 실행하지 않습니다.\n'먼저 뜨는 것 클릭' 스텝들만 계속 지켜보다가\n화면에 뜨는 즉시 클릭합니다. (돌발 상황 대응용)");
            tipStep.SetToolTip(cmbClickMode,"일반 클릭 : 실제 마우스 커서를 옮겨서 클릭\n커서 없이 클릭 : 창에 클릭 신호만 보냄 (커서가 안 움직임)\nADB(에뮬레이터) : 블루스택 등에 adb로 터치 전송");
            tipStep.SetToolTip(cmbMonitor,"이미지를 찾을 화면 범위입니다.\n한 모니터만 고르면 그만큼 빨라집니다.");
            chkAntiCapture.Anchor=AnchorStyles.Bottom|AnchorStyles.Left; pnlMid.Controls.Add(chkAntiCapture);
        }

        void AddStepOfType(StepType type)
        {
            if(_current==null)return;
            int ins=_selectedCardIdx>=0?_selectedCardIdx+1:_current.Steps.Count;
            _current.Steps.Insert(ins,new MacroStep{Type=type});
            RefreshStepList(ins);
            SelectStep(ins);      // 오른쪽 편집 패널까지 새 스텝으로 바꾼다
            btnRun.Enabled=true;
        }

        void BuildRight()
        {
            pnlRightScroll.Location=new System.Drawing.Point(RIGHT_X,0); pnlRightScroll.Size=new System.Drawing.Size(RIGHT_W,WIN_H); pnlRightScroll.AutoScroll=true; pnlRightScroll.BackColor=Color.FromArgb(245,245,248); pnlRightScroll.Enabled=false;
            pnlRightScroll.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right|AnchorStyles.Bottom;
            Controls.Add(pnlRightScroll);
            pnlRightInner.Location=new System.Drawing.Point(0,0); pnlRightInner.Size=new System.Drawing.Size(RIGHT_W-20,840); pnlRightInner.BackColor=Color.FromArgb(245,245,248); pnlRightScroll.Controls.Add(pnlRightInner);
            int rx=12,ry=10,pw=RIGHT_W-52; // 36(여백) + 16(스크롤바)
            int ix=rx+150;   // 라벨 다음에 입력칸이 시작되는 위치 (전부 여기에 맞춘다)
            const int PIX=150;  // 하위 패널(원점 0) 안에서의 입력칸 시작 위치

            // 타입 선택 (ComboBox)
            pnlRightInner.Controls.Add(MkSL("── 스텝 종류 ─────────────────────────────────────────────",rx,ry)); ry+=18;
            pnlRightInner.Controls.Add(MkL("종류:",rx,ry+3));
            cmbStepType.Location=new System.Drawing.Point(ix,ry); cmbStepType.Size=new System.Drawing.Size(220,24); cmbStepType.DropDownStyle=ComboBoxStyle.DropDownList; cmbStepType.Font=new Font("맑은 고딕",9);
            cmbStepType.Items.AddRange(new object[]{"이미지 찾아 클릭","여러 이미지 중 먼저 뜨는 것 클릭","키보드 입력","마우스 이동·클릭","시간 대기","알림 띄우기","이미지 보이면 스텝 켜고 끄기"});
            cmbStepType.SelectedIndex=0; cmbStepType.SelectedIndexChanged+=OnStepTypeChanged;
            pnlRightInner.Controls.Add(cmbStepType); ry+=32;

            // 공통
            pnlRightInner.Controls.Add(MkSL("── 모든 스텝 공통 ────────────────────────────────────────────",rx,ry)); ry+=18;
            pnlRightInner.Controls.Add(MkL("실행 후 대기(ms):",rx,ry+2));
            nudWaitAfter.Location=new System.Drawing.Point(ix,ry); nudWaitAfter.Size=new System.Drawing.Size(90,23); nudWaitAfter.Minimum=0; nudWaitAfter.Maximum=30000; nudWaitAfter.Value=500; nudWaitAfter.ValueChanged+=(s,e)=>{if(_selStep!=null)_selStep.WaitAfter=(int)nudWaitAfter.Value;}; pnlRightInner.Controls.Add(nudWaitAfter);
            chkEnabled.Text="이 스텝 사용"; chkEnabled.Location=new System.Drawing.Point(ix+112,ry+3); chkEnabled.AutoSize=true; chkEnabled.Checked=true; chkEnabled.CheckedChanged+=OnEnabled; pnlRightInner.Controls.Add(chkEnabled);
            chkStartOff.Text="시작할 때 꺼둠"; chkStartOff.Location=new System.Drawing.Point(ix+220,ry+3); chkStartOff.AutoSize=true; chkStartOff.Font=new Font("맑은 고딕",9);
            chkStartOff.CheckedChanged+=(s,e)=>{if(_suppressStepEvt)return;if(_selStep!=null){_selStep.StartDisabled=chkStartOff.Checked;RefreshStepList();}};
            pnlRightInner.Controls.Add(chkStartOff);
            ry+=32;

            pnlRightInner.Controls.Add(MkL("끝나면 갈 스텝:",rx,ry+4));
            nudJump.Location=new System.Drawing.Point(ix,ry); nudJump.Size=new System.Drawing.Size(60,23); nudJump.Minimum=0; nudJump.Maximum=999; nudJump.Value=0;
            nudJump.ValueChanged+=(s,e)=>{if(_selStep!=null){_selStep.JumpOnSuccess=(int)nudJump.Value;RefreshStepList();}};
            pnlRightInner.Controls.Add(nudJump);
            pnlRightInner.Controls.Add(new Label{Text="번으로 이동  (0 = 바로 아래 스텝)",Location=new System.Drawing.Point(ix+68,ry+4),AutoSize=true,ForeColor=Color.Gray,Font=new Font("맑은 고딕",8)});
            ry+=28;

            pnlRightInner.Controls.Add(MkL("끝난 뒤 지켜볼 스텝:",rx,ry+4));
            txtWatchTargets.Location=new System.Drawing.Point(ix,ry); txtWatchTargets.Size=new System.Drawing.Size(140,23); txtWatchTargets.Font=new Font("맑은 고딕",9); txtWatchTargets.PlaceholderText="예: 2,3,4";
            txtWatchTargets.TextChanged+=(s,e)=>{if(_selStep!=null){_selStep.WatchTargets=txtWatchTargets.Text;RefreshStepList();}};
            pnlRightInner.Controls.Add(txtWatchTargets);
            pnlRightInner.Controls.Add(new Label{Text="먼저 뜨는 쪽으로 이동 (비우면 안 함)",Location=new System.Drawing.Point(ix+148,ry+4),AutoSize=true,ForeColor=Color.Gray,Font=new Font("맑은 고딕",8)});
            ry+=32;

            // ── 이미지 패널 ──
            pnlImage.Location=new System.Drawing.Point(rx,ry); pnlImage.Size=new System.Drawing.Size(pw,492); pnlImage.BackColor=Color.Transparent; pnlRightInner.Controls.Add(pnlImage);
            {
                int y=0;
                // 그룹 ID 행
                pnlGroupRow.Location=new System.Drawing.Point(0,y); pnlGroupRow.Size=new System.Drawing.Size(pw,30); pnlGroupRow.BackColor=Color.FromArgb(255,248,230); pnlGroupRow.BorderStyle=BorderStyle.FixedSingle; pnlImage.Controls.Add(pnlGroupRow);
                pnlGroupRow.Controls.Add(new Label{Text="같이 볼 묶음 번호:",Font=new Font("맑은 고딕",8.5f,FontStyle.Bold),Location=new System.Drawing.Point(6,7),AutoSize=true,ForeColor=Color.FromArgb(140,60,0)});
                nudGroupId.Location=new System.Drawing.Point(PIX,5); nudGroupId.Size=new System.Drawing.Size(55,22); nudGroupId.Minimum=1; nudGroupId.Maximum=99; nudGroupId.Value=1; nudGroupId.ValueChanged+=(s,e)=>{if(_selStep!=null){_selStep.GroupId=(int)nudGroupId.Value;RefreshStepList();}}; pnlGroupRow.Controls.Add(nudGroupId);
                pnlGroupRow.Controls.Add(new Label{Text="(같은 번호끼리 한꺼번에 감시)",ForeColor=Color.Gray,Font=new Font("맑은 고딕",8),Location=new System.Drawing.Point(PIX+64,8),AutoSize=true});
                // 켜고 끄기 설정 줄 (묶음 번호 줄과 같은 자리 — 둘은 같이 안 보인다)
                pnlToggleRow.Location=new System.Drawing.Point(0,y); pnlToggleRow.Size=new System.Drawing.Size(pw,30); pnlToggleRow.BackColor=Color.FromArgb(248,240,255); pnlToggleRow.BorderStyle=BorderStyle.FixedSingle; pnlImage.Controls.Add(pnlToggleRow);
                pnlToggleRow.Controls.Add(new Label{Text="이 이미지가 보이면 스텝",Font=new Font("맑은 고딕",8.5f,FontStyle.Bold),Location=new System.Drawing.Point(6,7),AutoSize=true,ForeColor=Color.FromArgb(110,30,150)});
                txtToggleTargets.Location=new System.Drawing.Point(PIX,4); txtToggleTargets.Size=new System.Drawing.Size(90,23); txtToggleTargets.Font=new Font("맑은 고딕",9); txtToggleTargets.PlaceholderText="예: 7,8,9";
                txtToggleTargets.TextChanged+=(s,e)=>{if(_suppressStepEvt)return;if(_selStep!=null){_selStep.ToggleTargets=txtToggleTargets.Text;RefreshStepList();}};
                pnlToggleRow.Controls.Add(txtToggleTargets);
                pnlToggleRow.Controls.Add(new Label{Text="번을",Font=new Font("맑은 고딕",8.5f,FontStyle.Bold),Location=new System.Drawing.Point(PIX+96,7),AutoSize=true,ForeColor=Color.FromArgb(110,30,150)});
                cmbToggleAction.Location=new System.Drawing.Point(PIX+128,4); cmbToggleAction.Size=new System.Drawing.Size(160,23); cmbToggleAction.DropDownStyle=ComboBoxStyle.DropDownList; cmbToggleAction.Font=new Font("맑은 고딕",8.5f);
                cmbToggleAction.Items.AddRange(new object[]{"끄기","켜기","반대로","만 켜기 (나머지 끄기)"});
                cmbToggleAction.SelectedIndex=0;
                cmbToggleAction.SelectedIndexChanged+=(s,e)=>{if(_suppressStepEvt)return;if(_selStep!=null){_selStep.ToggleAction=(ToggleAction)cmbToggleAction.SelectedIndex;RefreshStepList();}};
                pnlToggleRow.Controls.Add(cmbToggleAction);
                y+=38;
                // 한 줄: [파일: ──경로──] [선택] [인식 테스트]
                pnlImage.Controls.Add(MkL("이미지 파일:",0,y+3));
                lblImgPath.Location=new System.Drawing.Point(PIX,y); lblImgPath.Size=new System.Drawing.Size(pw-PIX-152,23);
                lblImgPath.BorderStyle=BorderStyle.FixedSingle; lblImgPath.BackColor=Color.White;
                lblImgPath.Font=new Font("맑은 고딕",8); lblImgPath.Text="(선택 안됨)";
                pnlImage.Controls.Add(lblImgPath);
                btnPickImg.Text="선택";
                btnPickImg.Location=new System.Drawing.Point(pw-146,y-1); btnPickImg.Size=new System.Drawing.Size(50,26);
                btnPickImg.Click+=OnPickImage;
                pnlImage.Controls.Add(btnPickImg);
                btnTestImg.Text="찾기 테스트";
                btnTestImg.Location=new System.Drawing.Point(pw-92,y-1); btnTestImg.Size=new System.Drawing.Size(92,26);
                btnTestImg.Click+=OnTestImage;
                pnlImage.Controls.Add(btnTestImg);
                y+=30;
                // 테스트 결과
                lblTestResult.Location=new System.Drawing.Point(0,y); lblTestResult.Size=new System.Drawing.Size(pw,20); lblTestResult.Font=new Font("맑은 고딕",8.5f,FontStyle.Bold); lblTestResult.Text=""; pnlImage.Controls.Add(lblTestResult);
                y+=22;
                // 미리보기
                picPreview.Location=new System.Drawing.Point(0,y); picPreview.Size=new System.Drawing.Size(pw,140); picPreview.BorderStyle=BorderStyle.FixedSingle; picPreview.SizeMode=PictureBoxSizeMode.Zoom; picPreview.BackColor=Color.FromArgb(230,230,235); picPreview.Cursor=Cursors.Cross; picPreview.Paint+=PicPreview_Paint; picPreview.MouseClick+=PicPreview_Click; pnlImage.Controls.Add(picPreview);
                lblPreviewHint.Text="▲ 미리보기 클릭 = 클릭 위치 지정"; lblPreviewHint.Location=new System.Drawing.Point(0,y+142); lblPreviewHint.Size=new System.Drawing.Size(pw,16); lblPreviewHint.ForeColor=Color.FromArgb(0,110,180); lblPreviewHint.Font=new Font("맑은 고딕",8); pnlImage.Controls.Add(lblPreviewHint);
                y+=160;
                // 클릭 위치 오프셋
                pnlClickPos.Location=new System.Drawing.Point(0,y); pnlClickPos.Size=new System.Drawing.Size(pw,54); pnlClickPos.BorderStyle=BorderStyle.FixedSingle; pnlClickPos.BackColor=Color.FromArgb(245,255,245); pnlImage.Controls.Add(pnlClickPos);
                pnlClickPos.Controls.Add(new Label{Text="클릭 위치 조정 (이미지 중심 기준):",Font=new Font("맑은 고딕",8,FontStyle.Bold),Location=new System.Drawing.Point(8,6),AutoSize=true});
                pnlClickPos.Controls.Add(new Label{Text="X:",Location=new System.Drawing.Point(8,31),AutoSize=true});
                nudOffsetX.Location=new System.Drawing.Point(26,28); nudOffsetX.Size=new System.Drawing.Size(70,23); nudOffsetX.Minimum=-2000; nudOffsetX.Maximum=2000; nudOffsetX.Value=0; nudOffsetX.ValueChanged+=(s,e)=>{if(_selStep!=null){_selStep.ClickOffsetX=(int)nudOffsetX.Value;_selStep.UseCustomOffset=(_selStep.ClickOffsetX!=0||_selStep.ClickOffsetY!=0);UpdateClickPanel();picPreview.Invalidate();}}; pnlClickPos.Controls.Add(nudOffsetX);
                pnlClickPos.Controls.Add(new Label{Text="Y:",Location=new System.Drawing.Point(106,31),AutoSize=true});
                nudOffsetY.Location=new System.Drawing.Point(124,28); nudOffsetY.Size=new System.Drawing.Size(70,23); nudOffsetY.Minimum=-2000; nudOffsetY.Maximum=2000; nudOffsetY.Value=0; nudOffsetY.ValueChanged+=(s,e)=>{if(_selStep!=null){_selStep.ClickOffsetY=(int)nudOffsetY.Value;_selStep.UseCustomOffset=(_selStep.ClickOffsetX!=0||_selStep.ClickOffsetY!=0);UpdateClickPanel();picPreview.Invalidate();}}; pnlClickPos.Controls.Add(nudOffsetY);
                lblClickInfo.Location=new System.Drawing.Point(204,31); lblClickInfo.AutoSize=true; lblClickInfo.Font=new Font("맑은 고딕",8); lblClickInfo.ForeColor=Color.DarkGreen; lblClickInfo.Text="(이미지 중앙)"; pnlClickPos.Controls.Add(lblClickInfo);
                btnResetClick.Text="중앙으로 되돌리기"; btnResetClick.Location=new System.Drawing.Point(pw-136,26); btnResetClick.Size=new System.Drawing.Size(128,26); btnResetClick.Click+=BtnResetClick_Click; pnlClickPos.Controls.Add(btnResetClick);
                y+=62;
                // 클릭 설정 (켜고 끄기 스텝에서는 통째로 숨긴다)
                pnlClickCfg.Location=new System.Drawing.Point(0,y); pnlClickCfg.Size=new System.Drawing.Size(pw,126); pnlClickCfg.BackColor=Color.Transparent; pnlImage.Controls.Add(pnlClickCfg);
                {
                    int c=0;
                    pnlClickCfg.Controls.Add(MkSL("── 클릭 설정 ───────────────────────────────────────────────",0,c)); c+=18;
                    rdClickL.Text="좌클릭"; rdClickL.Location=new System.Drawing.Point(0,c); rdClickL.AutoSize=true; rdClickL.Checked=true; rdClickR.Text="우클릭"; rdClickR.Location=new System.Drawing.Point(PIX,c); rdClickR.AutoSize=true;
                    rdClickL.CheckedChanged+=OnClickTypeChanged; rdClickR.CheckedChanged+=OnClickTypeChanged; pnlClickCfg.Controls.Add(rdClickL); pnlClickCfg.Controls.Add(rdClickR); c+=26;
                    pnlClickCfg.Controls.Add(MkL("클릭 횟수:",0,c+4)); nudClicks.Location=new System.Drawing.Point(PIX,c); nudClicks.Size=new System.Drawing.Size(80,23); nudClicks.Minimum=1; nudClicks.Maximum=100; nudClicks.Value=1; nudClicks.ValueChanged+=OnNudClicks; pnlClickCfg.Controls.Add(nudClicks); c+=26;
                    pnlClickCfg.Controls.Add(MkL("클릭 사이 대기(ms):",0,c+4)); nudCDelay.Location=new System.Drawing.Point(PIX,c); nudCDelay.Size=new System.Drawing.Size(80,23); nudCDelay.Minimum=50; nudCDelay.Maximum=5000; nudCDelay.Value=100; nudCDelay.ValueChanged+=OnNudCDelay; pnlClickCfg.Controls.Add(nudCDelay); c+=26;
                    pnlClickCfg.Controls.Add(MkL("찾기 제한 시간(ms):",0,c+4)); nudTimeout.Location=new System.Drawing.Point(PIX,c); nudTimeout.Size=new System.Drawing.Size(80,23); nudTimeout.Minimum=0; nudTimeout.Maximum=60000; nudTimeout.Value=0; nudTimeout.ValueChanged+=(s,e)=>{if(_selStep!=null)_selStep.Timeout=(int)nudTimeout.Value;}; pnlClickCfg.Controls.Add(nudTimeout);
                    pnlClickCfg.Controls.Add(new Label{Text="(0 = 제한 없음)",Location=new System.Drawing.Point(PIX+88,c+4),AutoSize=true,ForeColor=Color.Gray,Font=new Font("맑은 고딕",8)});
                }
                y+=126;
                _confRowY=y;
                pnlConfRow.Location=new System.Drawing.Point(0,y); pnlConfRow.Size=new System.Drawing.Size(pw,50); pnlConfRow.BackColor=Color.Transparent; pnlImage.Controls.Add(pnlConfRow);
                pnlConfRow.Controls.Add(MkSL("── 이미지 일치도 ────────────────────────────────────────────",0,0));
                pnlConfRow.Controls.Add(MkL("일치도 기준:",0,23)); tbConf.Location=new System.Drawing.Point(PIX,18); tbConf.Size=new System.Drawing.Size(280,28); tbConf.Minimum=50; tbConf.Maximum=99; tbConf.Value=80; tbConf.TickFrequency=5; tbConf.Scroll+=OnConf; pnlConfRow.Controls.Add(tbConf);
                lblConf.Text="80%"; lblConf.Location=new System.Drawing.Point(PIX+290,23); lblConf.AutoSize=true; pnlConfRow.Controls.Add(lblConf);
            }

            // ── 키 입력 패널 ──
            pnlKey.Location=new System.Drawing.Point(rx,ry); pnlKey.Size=new System.Drawing.Size(pw,260); pnlKey.BackColor=Color.Transparent; pnlRightInner.Controls.Add(pnlKey);
            {
                int y=0;
                pnlKey.Controls.Add(MkSL("── 텍스트 타이핑 ────────────────────────────────────────────",0,y)); y+=18;
                pnlKey.Controls.Add(new Label{Text="입력할 텍스트:",Location=new System.Drawing.Point(0,y+2),AutoSize=true});
                txtKeyText.Location=new System.Drawing.Point(0,y+20); txtKeyText.Size=new System.Drawing.Size(pw,26); txtKeyText.Font=new Font("맑은 고딕",9); txtKeyText.PlaceholderText="타이핑할 텍스트 (비우면 단축키만 실행)";
                txtKeyText.TextChanged+=(s,e)=>{if(_selStep!=null)_selStep.KeyText=txtKeyText.Text;}; pnlKey.Controls.Add(txtKeyText); y+=54;
                pnlKey.Controls.Add(MkSL("── 단축키 입력 ──────────────────────────────────────────",0,y)); y+=18;
                pnlKey.Controls.Add(new Label{Text="누를 단축키:",Location=new System.Drawing.Point(0,y+4),AutoSize=true});
                txtHotKey.Location=new System.Drawing.Point(PIX,y); txtHotKey.Size=new System.Drawing.Size(124,24); txtHotKey.ReadOnly=true; txtHotKey.BackColor=Color.White; txtHotKey.Font=new Font("맑은 고딕",9); txtHotKey.Text="(없음)"; pnlKey.Controls.Add(txtHotKey);
                var btnCapHk=new Button{Text="단축키 지정",Location=new System.Drawing.Point(PIX+134,y-1),Size=new System.Drawing.Size(96,26)};
                btnCapHk.Click+=(s,e)=>{_capturingStepHk=true;txtHotKey.Text="입력 대기...";txtHotKey.BackColor=Color.LightYellow;SetStatus("단축키를 누르세요...");};
                pnlKey.Controls.Add(btnCapHk);
                var btnClearHk=new Button{Text="지우기",Location=new System.Drawing.Point(PIX+238,y-1),Size=new System.Drawing.Size(64,26)};
                btnClearHk.Click+=(s,e)=>{if(_selStep!=null){_selStep.HotKey="";txtHotKey.Text="(없음)";txtHotKey.BackColor=Color.White;}};
                pnlKey.Controls.Add(btnClearHk); y+=32;
                lblHkHint.Text="예: Enter, F1, Ctrl+C, Alt+F4, Ctrl+Shift+S 등";
                lblHkHint.Location=new System.Drawing.Point(0,y); lblHkHint.AutoSize=true; lblHkHint.ForeColor=Color.Gray; lblHkHint.Font=new Font("맑은 고딕",8); pnlKey.Controls.Add(lblHkHint); y+=22;
                var pHkEx=new Panel{Location=new System.Drawing.Point(0,y),Size=new System.Drawing.Size(pw,74),BackColor=Color.FromArgb(248,248,252),BorderStyle=BorderStyle.FixedSingle};
                pnlKey.Controls.Add(pHkEx);
                pHkEx.Controls.Add(new Label{Text="실행 방법",Font=new Font("맑은 고딕",8,FontStyle.Bold),Location=new System.Drawing.Point(6,5),AutoSize=true});
                pHkEx.Controls.Add(new Label{Text="텍스트만 입력 시: 문자를 순서대로 타이핑",ForeColor=Color.DimGray,Font=new Font("맑은 고딕",8),Location=new System.Drawing.Point(6,22),AutoSize=true});
                pHkEx.Controls.Add(new Label{Text="단축키만 입력 시: 키 조합을 한 번 누르기",ForeColor=Color.DimGray,Font=new Font("맑은 고딕",8),Location=new System.Drawing.Point(6,38),AutoSize=true});
                pHkEx.Controls.Add(new Label{Text="둘 다 입력 시: 텍스트 타이핑 후 단축키 실행",ForeColor=Color.DimGray,Font=new Font("맑은 고딕",8),Location=new System.Drawing.Point(6,54),AutoSize=true});
            }

            // ── 마우스 이동 패널 ──
            pnlMove.Location=new System.Drawing.Point(rx,ry); pnlMove.Size=new System.Drawing.Size(pw,250); pnlMove.BackColor=Color.Transparent; pnlRightInner.Controls.Add(pnlMove);
            {
                int y=0;
                pnlMove.Controls.Add(MkSL("── 좌표 방식 ──────────────────────────────────────────────",0,y)); y+=18;
                rdMoveAbs.Text="화면 고정 위치 (픽셀 좌표)"; rdMoveAbs.Location=new System.Drawing.Point(0,y); rdMoveAbs.AutoSize=true; rdMoveAbs.Checked=true; rdMoveAbs.CheckedChanged+=(s,e)=>{if(_selStep!=null)_selStep.MoveRelative=rdMoveRel.Checked;}; pnlMove.Controls.Add(rdMoveAbs);
                rdMoveRel.Text="현재 위치에서 이동 (상대 거리)"; rdMoveRel.Location=new System.Drawing.Point(0,y+22); rdMoveRel.AutoSize=true; rdMoveRel.CheckedChanged+=(s,e)=>{if(_selStep!=null)_selStep.MoveRelative=rdMoveRel.Checked;}; pnlMove.Controls.Add(rdMoveRel); y+=48;
                pnlMove.Controls.Add(MkSL("── 위치 지정 ──────────────────────────────────────────────",0,y)); y+=18;
                pnlMove.Controls.Add(MkL("X:",0,y+4)); nudMoveX.Location=new System.Drawing.Point(24,y); nudMoveX.Size=new System.Drawing.Size(100,23); nudMoveX.Minimum=-32000; nudMoveX.Maximum=32000; nudMoveX.Value=0; nudMoveX.ValueChanged+=(s,e)=>{if(_selStep!=null)_selStep.MoveX=(int)nudMoveX.Value;}; pnlMove.Controls.Add(nudMoveX);
                pnlMove.Controls.Add(MkL("Y:",140,y+4)); nudMoveY.Location=new System.Drawing.Point(164,y); nudMoveY.Size=new System.Drawing.Size(100,23); nudMoveY.Minimum=-32000; nudMoveY.Maximum=32000; nudMoveY.Value=0; nudMoveY.ValueChanged+=(s,e)=>{if(_selStep!=null)_selStep.MoveY=(int)nudMoveY.Value;}; pnlMove.Controls.Add(nudMoveY);
                btnPickPos.Text="현재 마우스 위치 사용"; btnPickPos.Location=new System.Drawing.Point(280,y-1); btnPickPos.Size=new System.Drawing.Size(pw-280,26); btnPickPos.Click+=OnPickMousePos; pnlMove.Controls.Add(btnPickPos); y+=32;
                lblPickPos.Text="위 버튼 클릭 후 3초 안에 원하는 위치로 마우스를 이동하세요."; lblPickPos.Location=new System.Drawing.Point(0,y); lblPickPos.AutoSize=true; lblPickPos.ForeColor=Color.Gray; lblPickPos.Font=new Font("맑은 고딕",8); pnlMove.Controls.Add(lblPickPos);
                y+=26;
                pnlMove.Controls.Add(MkSL("── 클릭 설정 ──────────────────────────────────────────────",0,y)); y+=18;
                var pMoveClick=new Panel{Location=new System.Drawing.Point(0,y),Size=new System.Drawing.Size(pw,26),BackColor=Color.Transparent};
                pnlMove.Controls.Add(pMoveClick);
                rdMoveOnly.Text="이동만";  rdMoveOnly.Location=new System.Drawing.Point(0,2);   rdMoveOnly.AutoSize=true; rdMoveOnly.Checked=true; rdMoveOnly.Font=new Font("맑은 고딕",8.5f); rdMoveOnly.CheckedChanged+=(s,e)=>{if(rdMoveOnly.Checked&&_selStep!=null)_selStep.MoveAction=MoveAction.MoveOnly;};  pMoveClick.Controls.Add(rdMoveOnly);
                rdMoveLeft.Text="이동 후 좌클릭";  rdMoveLeft.Location=new System.Drawing.Point(80,2);  rdMoveLeft.AutoSize=true;  rdMoveLeft.Font=new Font("맑은 고딕",8.5f); rdMoveLeft.CheckedChanged+=(s,e)=>{if(rdMoveLeft.Checked&&_selStep!=null)_selStep.MoveAction=MoveAction.LeftClick;};  pMoveClick.Controls.Add(rdMoveLeft);
                rdMoveRight.Text="이동 후 우클릭"; rdMoveRight.Location=new System.Drawing.Point(210,2); rdMoveRight.AutoSize=true; rdMoveRight.Font=new Font("맑은 고딕",8.5f); rdMoveRight.CheckedChanged+=(s,e)=>{if(rdMoveRight.Checked&&_selStep!=null)_selStep.MoveAction=MoveAction.RightClick;}; pMoveClick.Controls.Add(rdMoveRight);
            }

            // ── 대기 패널 ──
            pnlDelay.Location=new System.Drawing.Point(rx,ry); pnlDelay.Size=new System.Drawing.Size(pw,80); pnlDelay.BackColor=Color.Transparent; pnlRightInner.Controls.Add(pnlDelay);
            {
                int y=0;
                pnlDelay.Controls.Add(MkSL("── 대기 시간 ──────────────────────────────────────────────",0,y)); y+=18;
                pnlDelay.Controls.Add(MkL("대기 시간:",0,y+4));
                nudDelayMs.Location=new System.Drawing.Point(PIX,y); nudDelayMs.Size=new System.Drawing.Size(90,23); nudDelayMs.Minimum=1; nudDelayMs.Maximum=99999; nudDelayMs.Value=1000;
                nudDelayMs.ValueChanged+=(s,e)=>{if(_suppressStepEvt)return;if(_selStep!=null){_selStep.DelayMs=(int)nudDelayMs.Value;UpdateDelayHint();RefreshStepList();}}; pnlDelay.Controls.Add(nudDelayMs);
                cmbDelayUnit.Location=new System.Drawing.Point(PIX+96,y); cmbDelayUnit.Size=new System.Drawing.Size(85,23); cmbDelayUnit.DropDownStyle=ComboBoxStyle.DropDownList; cmbDelayUnit.Font=new Font("맑은 고딕",8.5f);
                cmbDelayUnit.Items.AddRange(new object[]{"밀리초","초","분"}); cmbDelayUnit.SelectedIndex=0;
                cmbDelayUnit.SelectedIndexChanged+=(s,e)=>{if(_suppressStepEvt)return;if(_selStep!=null){_selStep.DelayUnit=cmbDelayUnit.SelectedIndex;UpdateDelayHint();RefreshStepList();}};
                pnlDelay.Controls.Add(cmbDelayUnit);
                lblDelayHint.Location=new System.Drawing.Point(PIX+190,y+4); lblDelayHint.AutoSize=true; lblDelayHint.ForeColor=Color.Gray; lblDelayHint.Font=new Font("맑은 고딕",8);
                pnlDelay.Controls.Add(lblDelayHint);
            }

            // ── 알림 패널 ──
            pnlNotif.Location=new System.Drawing.Point(rx,ry); pnlNotif.Size=new System.Drawing.Size(pw,110); pnlNotif.BackColor=Color.Transparent; pnlRightInner.Controls.Add(pnlNotif);
            {
                int y=0;
                pnlNotif.Controls.Add(MkSL("── 알림 내용 ────────────────────────────────────────────",0,y)); y+=18;
                pnlNotif.Controls.Add(new Label{Text="알림 내용:",Location=new System.Drawing.Point(0,y+2),AutoSize=true});
                txtNotifText.Location=new System.Drawing.Point(0,y+22); txtNotifText.Size=new System.Drawing.Size(pw,26); txtNotifText.Font=new Font("맑은 고딕",9); txtNotifText.PlaceholderText="화면에 표시할 메시지를 입력하세요";
                txtNotifText.TextChanged+=(s,e)=>{if(_selStep!=null)_selStep.NotificationText=txtNotifText.Text;}; pnlNotif.Controls.Add(txtNotifText);
                pnlNotif.Controls.Add(new Label{Text="실행하면 화면 오른쪽 아래에 3초간 표시됩니다.",Location=new System.Drawing.Point(0,y+54),AutoSize=true,ForeColor=Color.Gray,Font=new Font("맑은 고딕",8)});
            }

            pnlRightInner.Height=ry+492;
            ShowPanelForType(StepType.Sequential);
        }

        void ShowPanelForType(StepType t)
        {
            bool img=t==StepType.Sequential||t==StepType.Simultaneous||t==StepType.ToggleSteps;
            pnlImage.Visible=img;
            pnlKey.Visible=t==StepType.KeyInput;
            pnlMove.Visible=t==StepType.MouseMove;
            pnlDelay.Visible=t==StepType.Delay;
            pnlNotif.Visible=t==StepType.Notification;
            pnlGroupRow.Visible=t==StepType.Simultaneous;
            pnlToggleRow.Visible=t==StepType.ToggleSteps;
            // 켜고 끄기 스텝은 클릭을 하지 않으니 클릭 관련 설정을 숨기고 일치도만 남긴다
            bool clicks=t!=StepType.ToggleSteps;
            pnlClickPos.Visible=clicks; pnlClickCfg.Visible=clicks; lblPreviewHint.Visible=clicks;
            pnlConfRow.Top=clicks?_confRowY:pnlClickPos.Top;
        }

        // ══════════════════════════════════════════════════════
        //  카드 시스템
        // ══════════════════════════════════════════════════════
        static Color GetTypeColor(StepType t)=>t switch{StepType.Sequential=>Color.FromArgb(0,80,180),StepType.Simultaneous=>Color.FromArgb(160,80,0),StepType.KeyInput=>Color.FromArgb(80,0,160),StepType.MouseMove=>Color.FromArgb(0,120,120),StepType.Delay=>Color.FromArgb(96,96,96),StepType.Notification=>Color.FromArgb(0,128,80),StepType.ToggleSteps=>Color.FromArgb(130,40,170),_=>Color.Gray};
        static Color GetCardBg(StepType t)=>t switch{StepType.Sequential=>Color.FromArgb(240,245,255),StepType.Simultaneous=>Color.FromArgb(255,248,240),StepType.KeyInput=>Color.FromArgb(245,240,255),StepType.MouseMove=>Color.FromArgb(240,255,255),StepType.Delay=>Color.FromArgb(245,245,245),StepType.Notification=>Color.FromArgb(240,255,245),StepType.ToggleSteps=>Color.FromArgb(248,240,255),_=>Color.White};
        static string GetTypeIcon(StepType t)=>"";
        static string GetTypeName(StepType t)=>t switch{StepType.Sequential=>"이미지 찾아 클릭",StepType.Simultaneous=>"먼저 뜨는 것 클릭",StepType.KeyInput=>"키보드 입력",StepType.MouseMove=>"마우스 이동·클릭",StepType.Delay=>"시간 대기",StepType.Notification=>"알림 띄우기",StepType.ToggleSteps=>"스텝 켜고 끄기",_=>""};
        static string GetCardContent(MacroStep st)=>st.Type switch{
            StepType.KeyInput=>string.IsNullOrEmpty(st.KeyText)?$"단축키: {st.HotKey}":$"타이핑: \"{st.KeyText}\"",
            StepType.MouseMove=>st.MoveAction==MoveAction.MoveOnly?$"이동: ({st.MoveX},{st.MoveY})":$"{(st.MoveAction==MoveAction.LeftClick?"좌클릭":"우클릭")}: ({st.MoveX},{st.MoveY})",
            StepType.Delay=>$"{st.DelayMs}{MacroItem.UnitName(st.DelayUnit)} 대기",
            StepType.Notification=>string.IsNullOrEmpty(st.NotificationText)?"(메시지 없음)":$"\"{st.NotificationText}\"",
            StepType.ToggleSteps=>string.IsNullOrEmpty(st.ImagePath)?"(이미지 미지정)"
                :$"{Path.GetFileName(st.ImagePath)} 보이면 {(string.IsNullOrWhiteSpace(st.ToggleTargets)?"(대상 없음)":st.ToggleTargets)} {ToggleActionName(st.ToggleAction)}",
            _=>string.IsNullOrEmpty(st.ImagePath)?"(이미지 미지정)":Path.GetFileName(st.ImagePath)};

        Panel MakeStepCard(int idx,MacroStep step,int cardW,bool selected)
        {
            int cardH=50;
            var card=new Panel{Size=new System.Drawing.Size(cardW,cardH),BackColor=selected?Color.FromArgb(220,235,255):GetCardBg(step.Type),Cursor=Cursors.Hand,Tag=idx};
            // 좌측 컬러 바
            card.Controls.Add(new Panel{Location=new System.Drawing.Point(0,0),Size=new System.Drawing.Size(4,cardH),BackColor=GetTypeColor(step.Type)});
            // 번호 + 타입 (한 줄)
            string num=step.Type==StepType.Simultaneous?$"G{step.GroupId}":$"{idx+1}";
            card.Controls.Add(new Label{Text=$"{num}. {GetTypeName(step.Type)}",Location=new System.Drawing.Point(9,4),AutoSize=true,Font=new Font("맑은 고딕",8.5f,FontStyle.Bold),ForeColor=GetTypeColor(step.Type),BackColor=Color.Transparent});
            // 내용
            string content=GetCardContent(step);
            card.Controls.Add(new Label{Text=content,Location=new System.Drawing.Point(8,24),Size=new System.Drawing.Size(cardW-80,20),Font=new Font("맑은 고딕",8),ForeColor=step.Enabled?Color.FromArgb(60,60,60):Color.Silver,BackColor=Color.Transparent});
            // 인라인 JumpOnSuccess 뱃지
            if(step.JumpOnSuccess>0){
                int jt=step.JumpOnSuccess;
                var badge=new Label{Text=$"→ {jt}",AutoSize=false,Size=new System.Drawing.Size(38,18),TextAlign=ContentAlignment.MiddleCenter,Location=new System.Drawing.Point(cardW-72,16),Font=new Font("맑은 고딕",7.5f,FontStyle.Bold),ForeColor=Color.White,BackColor=Color.FromArgb(200,120,30),Cursor=Cursors.Hand};
                badge.Click+=(s,e)=>SelectStep(jt-1);
                card.Controls.Add(badge);
            }
            // 비활성 표시
            if(!step.Enabled)card.Controls.Add(new Label{Text="OFF",Location=new System.Drawing.Point(cardW-32,4),AutoSize=true,Font=new Font("맑은 고딕",7,FontStyle.Bold),ForeColor=Color.FromArgb(180,180,180),BackColor=Color.Transparent});
            else if(step.StartDisabled)card.Controls.Add(new Label{Text="꺼진 채 시작",Location=new System.Drawing.Point(cardW-66,4),AutoSize=true,Font=new Font("맑은 고딕",7,FontStyle.Bold),ForeColor=Color.FromArgb(150,60,190),BackColor=Color.Transparent});
            // 선택 테두리
            if(selected)card.Paint+=(s,e)=>{using var pen=new Pen(Color.FromArgb(0,120,215),2);e.Graphics.DrawRectangle(pen,1,1,card.Width-3,card.Height-3);};
            // 클릭 → 선택
            EventHandler click=(s,e)=>SelectStep(idx);
            card.Click+=click; foreach(Control c in card.Controls)c.Click+=click;
            return card;
        }

        Panel MakeBranchBar(MacroStep step,int barW)
        {
            int barH=22;
            var bar=new Panel{Size=new System.Drawing.Size(barW,barH),BackColor=Color.FromArgb(235,245,255),BorderStyle=BorderStyle.FixedSingle};
            var targets=step.WatchTargets.Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
            string txt="끝난 뒤 지켜봄: "+string.Join(" | ",targets.Select(t=>"스텝 "+t.Trim()))+" → 먼저 뜨는 쪽으로";
            var lbl=new Label{Text=txt,Location=new System.Drawing.Point(6,2),AutoSize=true,Font=new Font("맑은 고딕",7.5f),ForeColor=Color.FromArgb(0,100,160),BackColor=Color.Transparent,Cursor=Cursors.Hand};
            bar.Controls.Add(lbl);
            // 전체 바 클릭 시 첫 번째 대상으로 이동
            if(targets.Length>0&&int.TryParse(targets[0].Trim(),out int firstTarget)&&firstTarget>=1){
                EventHandler barClick=(s,e)=>SelectStep(firstTarget-1);
                bar.Click+=barClick;lbl.Click+=barClick;
            }
            return bar;
        }

        Panel MakeGroupWrapper(List<int> indices,List<MacroStep> steps,int wrapW,int sel)
        {
            int headerH=20;int cardGap=2;int pad=4;
            int innerW=wrapW-pad*2;
            int totalCardH=indices.Count*50+(indices.Count-1)*cardGap;
            int wrapH=headerH+totalCardH+pad*2;
            var wrapper=new Panel{Size=new System.Drawing.Size(wrapW,wrapH),BackColor=Color.FromArgb(255,250,235)};
            wrapper.Paint+=(s,e)=>{using var pen=new Pen(Color.FromArgb(200,160,60),1){DashStyle=System.Drawing.Drawing2D.DashStyle.Dash};e.Graphics.DrawRectangle(pen,0,0,wrapper.Width-1,wrapper.Height-1);};
            int gid=steps[indices[0]].GroupId;
            wrapper.Controls.Add(new Label{Text=$"묶음 {gid} — 먼저 뜨는 하나만",Location=new System.Drawing.Point(pad+2,2),AutoSize=true,Font=new Font("맑은 고딕",7.5f,FontStyle.Bold),ForeColor=Color.FromArgb(160,100,0),BackColor=Color.Transparent});
            int cy=headerH;
            foreach(int idx in indices){
                var card=MakeStepCard(idx,steps[idx],innerW,sel==idx);
                card.Location=new System.Drawing.Point(pad,cy);
                wrapper.Controls.Add(card);
                cy+=50+cardGap;
            }
            return wrapper;
        }

        // 입력칸 밖을 누르면 커서를 놓고, 입력 중이던 값을 바로 저장한다.
        void CommitAllInput()
        {
            CommitMacroInput();
            CommitPendingInput();
        }

        // 매크로 단위 설정값 확정
        void CommitMacroInput()
        {
            if(_current==null)return;
            if(txtName.Text.Trim().Length>0&&_current.Name!=txtName.Text){_current.Name=txtName.Text;RefreshMacroList();}
            if(int.TryParse(nudScanMs.Text,out int sc))_current.ScanInterval=Math.Clamp(sc,(int)nudScanMs.Minimum,(int)nudScanMs.Maximum);
            if(int.TryParse(nudRepeat.Text,out int rp))_current.RepeatCount=Math.Clamp(rp,(int)nudRepeat.Minimum,(int)nudRepeat.Maximum);
            if(int.TryParse(nudLoopDelay.Text,out int ld))_current.LoopDelay=Math.Clamp(ld,(int)nudLoopDelay.Minimum,(int)nudLoopDelay.Maximum);
            if(int.TryParse(nudRestartDelay.Text,out int rd))_current.RestartDelay=Math.Clamp(rd,(int)nudRestartDelay.Minimum,(int)nudRestartDelay.Maximum);
        }

        // 모든 입력칸에 "포커스 잃으면 저장", 빈 곳에는 "누르면 커서 해제"를 걸어준다.
        void HookInputCommit(Control root)
        {
            foreach(Control c in root.Controls){
                if(c is NumericUpDown||c is TextBox||c is ComboBox||c is TrackBar){
                    // 값만 확정한다. 여기서 목록을 다시 그리면 지금 누르고 있는 카드가
                    // 사라져서 클릭이 씹힌다 (카드 글자는 각 입력칸의 ValueChanged 에서 갱신됨).
                    c.Leave+=(s,e)=>CommitAllInput();
                    continue;   // 내부 자식(에디트/버튼)에는 걸지 않는다
                }
                if(c is Panel||c is Label||c is PictureBox||c is GroupBox)
                    c.MouseDown+=(s,e)=>DropFocus();
                HookInputCommit(c);
            }
        }

        void DropFocus()
        {
            if(ActiveControl==null)return;
            CommitAllInput();
            ActiveControl=null;      // 커서(캐럿)가 사라진다
        }

        void CommitPendingInput()
        {
            if(_selStep==null)return;
            if(int.TryParse(nudClicks.Text,out int c))_selStep.ClickCount=Math.Clamp(c,(int)nudClicks.Minimum,(int)nudClicks.Maximum);
            if(int.TryParse(nudCDelay.Text,out int d))_selStep.ClickDelay=Math.Clamp(d,(int)nudCDelay.Minimum,(int)nudCDelay.Maximum);
            if(int.TryParse(nudWaitAfter.Text,out int w))_selStep.WaitAfter=Math.Clamp(w,(int)nudWaitAfter.Minimum,(int)nudWaitAfter.Maximum);
            if(int.TryParse(nudTimeout.Text,out int t))_selStep.Timeout=Math.Clamp(t,(int)nudTimeout.Minimum,(int)nudTimeout.Maximum);
            if(int.TryParse(nudOffsetX.Text,out int ox))_selStep.ClickOffsetX=Math.Clamp(ox,(int)nudOffsetX.Minimum,(int)nudOffsetX.Maximum);
            if(int.TryParse(nudOffsetY.Text,out int oy))_selStep.ClickOffsetY=Math.Clamp(oy,(int)nudOffsetY.Minimum,(int)nudOffsetY.Maximum);
            if(int.TryParse(nudMoveX.Text,out int mx))_selStep.MoveX=Math.Clamp(mx,(int)nudMoveX.Minimum,(int)nudMoveX.Maximum);
            if(int.TryParse(nudMoveY.Text,out int my))_selStep.MoveY=Math.Clamp(my,(int)nudMoveY.Minimum,(int)nudMoveY.Maximum);
            if(int.TryParse(nudDelayMs.Text,out int dm)){_selStep.DelayMs=Math.Clamp(dm,(int)nudDelayMs.Minimum,(int)nudDelayMs.Maximum);UpdateDelayHint();}
            if(int.TryParse(nudJump.Text,out int j))_selStep.JumpOnSuccess=Math.Clamp(j,(int)nudJump.Minimum,(int)nudJump.Maximum);
            if(int.TryParse(nudGroupId.Text,out int g))_selStep.GroupId=Math.Clamp(g,(int)nudGroupId.Minimum,(int)nudGroupId.Maximum);
        }

        // "= 1분 30초" 처럼 실제 대기 시간을 알려준다
        void UpdateDelayHint(MacroStep? st=null)
        {
            var t=st??_selStep;
            if(t==null){lblDelayHint.Text="";return;}
            int ms=t.DelayEffectiveMs;
            lblDelayHint.Text=t.DelayUnit==0?$"= {ms/1000.0:0.###}초":$"= {ms:N0}밀리초";
        }

        void SelectStep(int idx)
        {
            if(_suppressStepEvt||_current==null||idx<0||idx>=_current.Steps.Count)return;
            CommitPendingInput();
            _selectedCardIdx=idx;
            var step=_current.Steps[idx];
            _selStep=null; // LoadStepUI 중 이벤트 핸들러에서 RefreshStepList 호출 방지
            LoadStepUI(step);
            _selStep=step;
            pnlRightScroll.Enabled=true;
            RefreshStepList(idx);
        }

        // ══════════════════════════════════════════════════════
        //  스텝 타입 변경
        // ══════════════════════════════════════════════════════
        void OnStepTypeChanged(object? s,EventArgs e)
        {
            if(_selStep==null)return;
            StepType t=cmbStepType.SelectedIndex switch{0=>StepType.Sequential,1=>StepType.Simultaneous,2=>StepType.KeyInput,3=>StepType.MouseMove,4=>StepType.Delay,5=>StepType.Notification,6=>StepType.ToggleSteps,_=>StepType.Sequential};
            _selStep.Type=t; ShowPanelForType(t); RefreshStepList();
        }

        // ══════════════════════════════════════════════════════
        //  이미지 인식 테스트
        // ══════════════════════════════════════════════════════
        void OnTestImage(object? s,EventArgs e)
        {
            if(_selStep==null||string.IsNullOrEmpty(_selStep.ImagePath)){SetStatus("먼저 이미지 파일을 선택하세요.");return;}
            lblTestResult.Text="테스트 중..."; lblTestResult.ForeColor=Color.Gray; Application.DoEvents();
            try{
                using var tmpl=new Bitmap(_selStep.ImagePath);
                using var ss=CaptureScreen(out var capOrg); using Mat src=BitmapToMat(ss); using Mat g=new Mat();
                Cv2.CvtColor(src,g,ColorConversionCodes.BGRA2GRAY);
                var pos=MatchTplWithScore(g,tmpl,capOrg,out double score,out Rectangle matchRect);
                if(pos.HasValue){
                    int cx=pos.Value.X+_selStep.ClickOffsetX, cy=pos.Value.Y+_selStep.ClickOffsetY;
                    lblTestResult.Text=$"[성공] 발견! 위치: ({pos.Value.X},{pos.Value.Y}) [{MonitorNameOf(pos.Value)}]  일치도: {score:P1}  클릭 위치: ({cx},{cy})";
                    lblTestResult.ForeColor=Color.DarkGreen; SetStatus($"찾기 성공! 일치도 {score:P1}");
                    ShowMatchOverlay(matchRect,new System.Drawing.Point(cx,cy),score);
                }else{
                    lblTestResult.Text=$"[실패] 못 찾음 (최고 일치도: {score:P1}, 기준: {_selStep.Confidence}%)";
                    lblTestResult.ForeColor=Color.Red; SetStatus($"찾기 실패. 일치도 기준을 낮춰보세요.");
                    ShowMatchOverlay(matchRect,System.Drawing.Point.Empty,score,false);
                }
            }catch(Exception ex){lblTestResult.Text=$"오류: {ex.Message}";lblTestResult.ForeColor=Color.Red;}
        }

        void ShowMatchOverlay(Rectangle matchRect,System.Drawing.Point clickPt,double score,bool found=true)
        {
            var screen=VirtualBounds;
            // 매칭 영역 주변에 여유 추가
            int pad=40;
            var viewRect=Rectangle.Inflate(matchRect,pad,pad);
            viewRect.Intersect(screen);

            var frm=new Form{
                FormBorderStyle=System.Windows.Forms.FormBorderStyle.None,
                StartPosition=FormStartPosition.Manual,
                Location=viewRect.Location,
                Size=viewRect.Size,
                TopMost=true,ShowInTaskbar=false,
                BackColor=Color.Magenta,TransparencyKey=Color.Magenta,
                Opacity=1.0
            };

            frm.Paint+=(s,pe)=>{
                var g=pe.Graphics;
                g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                // 매칭 영역을 폼 로컬 좌표로 변환
                int rx=matchRect.X-viewRect.X,ry=matchRect.Y-viewRect.Y;
                int rw=matchRect.Width,rh=matchRect.Height;

                // 반투명 배경 (매칭 영역 강조)
                using var dimBrush=new SolidBrush(Color.FromArgb(60,0,0,0));
                g.FillRectangle(dimBrush,0,0,frm.Width,frm.Height);
                // 매칭 영역 안쪽은 투명하게 (구멍 뚫기)
                g.FillRectangle(new SolidBrush(Color.Magenta),rx,ry,rw,rh);

                // 테두리
                Color borderColor=found?Color.FromArgb(0,200,0):Color.FromArgb(255,80,80);
                using var pen=new Pen(borderColor,3);
                g.DrawRectangle(pen,rx,ry,rw,rh);

                // 모서리 강조
                int corner=Math.Min(12,Math.Min(rw,rh)/3);
                using var cpen=new Pen(borderColor,4);
                g.DrawLine(cpen,rx,ry,rx+corner,ry);g.DrawLine(cpen,rx,ry,rx,ry+corner);
                g.DrawLine(cpen,rx+rw,ry,rx+rw-corner,ry);g.DrawLine(cpen,rx+rw,ry,rx+rw,ry+corner);
                g.DrawLine(cpen,rx,ry+rh,rx+corner,ry+rh);g.DrawLine(cpen,rx,ry+rh,rx,ry+rh-corner);
                g.DrawLine(cpen,rx+rw,ry+rh,rx+rw-corner,ry+rh);g.DrawLine(cpen,rx+rw,ry+rh,rx+rw,ry+rh-corner);

                // 클릭 예정 위치 십자선
                if(found&&clickPt!=System.Drawing.Point.Empty){
                    int cpx=clickPt.X-viewRect.X,cpy=clickPt.Y-viewRect.Y;
                    using var crossPen=new Pen(Color.Red,2);
                    g.DrawLine(crossPen,cpx-10,cpy,cpx+10,cpy);
                    g.DrawLine(crossPen,cpx,cpy-10,cpx,cpy+10);
                    g.DrawEllipse(crossPen,cpx-6,cpy-6,12,12);
                }

                // 유사도 라벨
                string label=found?$"일치도: {score:P1}":$"최고: {score:P1}";
                using var font=new Font("맑은 고딕",9,FontStyle.Bold);
                var labelSize=g.MeasureString(label,font);
                int lx=rx,ly=ry-(int)labelSize.Height-4;
                if(ly<0)ly=ry+rh+4;
                using var labelBg=new SolidBrush(Color.FromArgb(200,found?Color.FromArgb(0,80,0):Color.FromArgb(120,0,0)));
                g.FillRectangle(labelBg,lx,ly,labelSize.Width+8,labelSize.Height+2);
                g.DrawString(label,font,Brushes.White,lx+4,ly+1);
            };

            frm.Show();
            var timer=new System.Windows.Forms.Timer{Interval=3000};
            timer.Tick+=(ts,te)=>{timer.Stop();frm.Close();frm.Dispose();timer.Dispose();};
            timer.Start();
        }

        // ══════════════════════════════════════════════════════
        //  마우스 위치 가져오기
        // ══════════════════════════════════════════════════════
        void OnPickMousePos(object? s,EventArgs e)
        {
            if(_pickingPos)return; _pickingPos=true;
            lblPickPos.Text="3초 후 마우스 위치를 가져옵니다... 원하는 위치로 이동하세요!";
            lblPickPos.ForeColor=Color.DarkOrange;
            var t=new System.Windows.Forms.Timer{Interval=3000};
            t.Tick+=(ts,te)=>{t.Stop();_pickingPos=false;GetCursorPos(out POINT pt);if(_selStep!=null){_selStep.MoveX=pt.X;_selStep.MoveY=pt.Y;nudMoveX.Value=Math.Clamp(pt.X,-32000,32000);nudMoveY.Value=Math.Clamp(pt.Y,-32000,32000);}lblPickPos.Text=$"캡처 완료: ({pt.X}, {pt.Y})  [{MonitorNameOf(new System.Drawing.Point(pt.X,pt.Y))}]";lblPickPos.ForeColor=Color.DarkGreen;SetStatus($"마우스 위치 설정: ({pt.X},{pt.Y})");};
            t.Start();
        }

        // ══════════════════════════════════════════════════════
        //  스텝 단축키 캡처
        // ══════════════════════════════════════════════════════
        void OnStepHkKeyDown(KeyEventArgs e)
        {
            if(e.KeyCode==Keys.ControlKey||e.KeyCode==Keys.ShiftKey||e.KeyCode==Keys.Menu||e.KeyCode==Keys.None)return;
            string ks=(e.Control?"Ctrl+":"")+(e.Alt?"Alt+":"")+(e.Shift?"Shift+":"")+e.KeyCode;
            if(_selStep!=null)_selStep.HotKey=ks;
            txtHotKey.Text=ks; txtHotKey.BackColor=Color.White;
            _capturingStepHk=false; SetStatus($"단축키 설정: {ks}");
            e.Handled=true; e.SuppressKeyPress=true;
        }

        // ══════════════════════════════════════════════════════
        //  미리보기
        // ══════════════════════════════════════════════════════
        void PicPreview_Click(object? s,MouseEventArgs e){if(_selStep==null||picPreview.Image==null)return;var ir=GetImageRect(picPreview);if(!ir.Contains(e.Location))return;float sx=(float)picPreview.Image.Width/ir.Width,sy=(float)picPreview.Image.Height/ir.Height;int imgX=(int)((e.X-ir.X)*sx),imgY=(int)((e.Y-ir.Y)*sy);_selStep.ClickOffsetX=imgX-picPreview.Image.Width/2;_selStep.ClickOffsetY=imgY-picPreview.Image.Height/2;_selStep.UseCustomOffset=true;nudOffsetX.Value=Math.Clamp(_selStep.ClickOffsetX,-2000,2000);nudOffsetY.Value=Math.Clamp(_selStep.ClickOffsetY,-2000,2000);_previewClickPt=e.Location;UpdateClickPanel();picPreview.Invalidate();}
        void PicPreview_Paint(object? s,PaintEventArgs e){if(_previewClickPt==null||picPreview.Image==null)return;var pt=_previewClickPt.Value;using var pen=new Pen(Color.Red,2);e.Graphics.DrawLine(pen,pt.X-8,pt.Y,pt.X+8,pt.Y);e.Graphics.DrawLine(pen,pt.X,pt.Y-8,pt.X,pt.Y+8);e.Graphics.DrawEllipse(pen,pt.X-5,pt.Y-5,10,10);}
        void BtnResetClick_Click(object? s,EventArgs e){if(_selStep==null)return;_selStep.ClickOffsetX=0;_selStep.ClickOffsetY=0;_selStep.UseCustomOffset=false;nudOffsetX.Value=0;nudOffsetY.Value=0;_previewClickPt=null;UpdateClickPanel();picPreview.Invalidate();}
        void UpdateClickPanel(){if(_selStep==null)return;if(_selStep.ClickOffsetX==0&&_selStep.ClickOffsetY==0){lblClickInfo.Text="(이미지 중앙)";lblClickInfo.ForeColor=Color.DarkGreen;pnlClickPos.BackColor=Color.FromArgb(245,255,245);}else{string sx=_selStep.ClickOffsetX>=0?"+":"";string sy=_selStep.ClickOffsetY>=0?"+":"";lblClickInfo.Text=$"중앙 X{sx}{_selStep.ClickOffsetX} Y{sy}{_selStep.ClickOffsetY}";lblClickInfo.ForeColor=Color.DarkRed;pnlClickPos.BackColor=Color.FromArgb(255,245,235);}}
        static Rectangle GetImageRect(PictureBox pb){if(pb.Image==null)return pb.ClientRectangle;float ia=(float)pb.Image.Width/pb.Image.Height,ba=(float)pb.Width/pb.Height;int w,h,x,y;if(ia>ba){w=pb.Width;h=(int)(pb.Width/ia);x=0;y=(pb.Height-h)/2;}else{h=pb.Height;w=(int)(pb.Height*ia);y=0;x=(pb.Width-w)/2;}return new Rectangle(x,y,w,h);}
        void RecalcPreviewClickPt(MacroStep st){if(picPreview.Image==null){_previewClickPt=null;return;}var ir=GetImageRect(picPreview);float sx=(float)ir.Width/picPreview.Image.Width,sy=(float)ir.Height/picPreview.Image.Height;int cx=picPreview.Image.Width/2,cy=picPreview.Image.Height/2;_previewClickPt=new System.Drawing.Point((int)((cx+st.ClickOffsetX)*sx)+ir.X,(int)((cy+st.ClickOffsetY)*sy)+ir.Y);}

        // ══════════════════════════════════════════════════════
        //  이벤트 핸들러
        // ══════════════════════════════════════════════════════
        void OnTimeoutActionChanged(object? s,EventArgs e){if(_current==null)return;_current.OnTimeout=rdTORestart.Checked?TimeoutAction.Restart:TimeoutAction.Stop;nudRestartDelay.Enabled=rdTORestart.Checked;lblRestartDelay.ForeColor=rdTORestart.Checked?Color.Black:Color.Gray;}
        void OnClickTypeChanged(object? s,EventArgs e){if(_selStep!=null)_selStep.RightClick=rdClickR.Checked;}
        void OnNudClicks(object? s,EventArgs e){if(_selStep!=null)_selStep.ClickCount=(int)nudClicks.Value;}
        void OnNudCDelay(object? s,EventArgs e){if(_selStep!=null)_selStep.ClickDelay=(int)nudCDelay.Value;}
        void OnConf(object? s,EventArgs e){lblConf.Text=$"{tbConf.Value}%";if(_selStep!=null)_selStep.Confidence=tbConf.Value;}
        void OnEnabled(object? s,EventArgs e){if(_selStep!=null){_selStep.Enabled=chkEnabled.Checked;RefreshStepList();}}
        void OnNameChanged(object? s,EventArgs e){if(_current==null)return;_current.Name=txtName.Text;int idx=lstMacros.SelectedIndex;if(idx>=0&&idx<lstMacros.Items.Count)lstMacros.Items[idx]=txtName.Text;}

        // ══════════════════════════════════════════════════════
        //  매크로 목록
        // ══════════════════════════════════════════════════════
        void OnNewMacro(object? s,EventArgs e){var m=new MacroItem{Name=$"매크로 {_macros.Count+1}"};_macros.Add(m);RefreshMacroList();lstMacros.SelectedIndex=_macros.Count-1;}
        void OnDelMacro(object? s,EventArgs e){if(_current==null)return;if(MessageBox.Show($"'{_current.Name}'을(를) 삭제하시겠습니까?","삭제 확인",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;_macros.Remove(_current);_current=null;RefreshMacroList();pnlStepFlow.Controls.Clear();_selectedCardIdx=-1;pnlRightScroll.Enabled=false;SetStatus("삭제 완료.");}
        void OnMacroSelected(object? s,EventArgs e){if(lstMacros.SelectedIndex<0)return;_current=_macros[lstMacros.SelectedIndex];LoadMacroUI(_current);btnRun.Enabled=_current.Steps.Count>0;}
        void RefreshMacroList(){int sel=lstMacros.SelectedIndex;lstMacros.Items.Clear();foreach(var m in _macros)lstMacros.Items.Add(m.Name);if(sel>=0&&sel<lstMacros.Items.Count)lstMacros.SelectedIndex=sel;}

        void LoadMacroUI(MacroItem m)
        {
            txtName.TextChanged-=OnNameChanged;
            txtName.Text=m.Name; nudScanMs.Value=Math.Clamp(m.ScanInterval,100,5000); nudRepeat.Value=Math.Clamp(m.RepeatCount,0,9999);
            lblStartHk.Text=m.StartHotkey; lblStopHk.Text=m.StopHotkey;
            rdTOStop.CheckedChanged-=OnTimeoutActionChanged; rdTORestart.CheckedChanged-=OnTimeoutActionChanged;
            rdTORestart.Checked=m.OnTimeout==TimeoutAction.Restart; rdTOStop.Checked=m.OnTimeout==TimeoutAction.Stop;
            nudRestartDelay.Value=Math.Clamp(m.RestartDelay,0,30000); nudRestartDelay.Enabled=rdTORestart.Checked; lblRestartDelay.ForeColor=rdTORestart.Checked?Color.Black:Color.Gray;
            rdTOStop.CheckedChanged+=OnTimeoutActionChanged; rdTORestart.CheckedChanged+=OnTimeoutActionChanged;
            // 구버전 파일: BackgroundClick 만 있던 시절 값을 ClickMode 로 옮긴다
            if(m.ClickMode==ClickMode.Normal&&m.BackgroundClick)m.ClickMode=ClickMode.Background;
            _suppressStepEvt=true;
            FillMonitorCombo();
            cmbMonitor.SelectedIndex=Math.Clamp(m.SearchMonitor,0,cmbMonitor.Items.Count-1);
            m.SearchMonitor=cmbMonitor.SelectedIndex; _searchMonitor=m.SearchMonitor;
            nudLoopDelay.Value=Math.Clamp(m.LoopDelay,0,9999);
            cmbLoopUnit.SelectedIndex=Math.Clamp(m.LoopDelayUnit,0,2);
            cmbClickMode.SelectedIndex=(int)m.ClickMode;
            btnAdbCfg.Enabled=m.ClickMode==ClickMode.Adb;
            _suppressStepEvt=false;
            chkEventMode.Checked=m.EventMode;
            txtName.TextChanged+=OnNameChanged;
            ParseHk(m.StartHotkey,out uint sm,out uint sv); ParseHk(m.StopHotkey,out uint em,out uint ev);
            m.StartMod=sm;m.StartVk=sv;m.StopMod=em;m.StopVk=ev; ApplyHk(m); _selStep=null; pnlRightScroll.Enabled=false; RefreshStepList();
        }

        // ══════════════════════════════════════════════════════
        //  스텝 목록 (스크롤 유지)
        // ══════════════════════════════════════════════════════
        void RefreshStepList(int forceSel=-2)
        {
            int sel=forceSel==-2?_selectedCardIdx:forceSel;
            int scrollPos=pnlStepFlow.VerticalScroll.Value;
            _suppressStepEvt=true;
            // 스크롤 초기화 후 Clear → 팬텀 공백 방지
            pnlStepFlow.AutoScrollPosition=new System.Drawing.Point(0,0);
            pnlStepFlow.SuspendLayout(); pnlStepFlow.Controls.Clear();
            if(_current!=null&&_current.Steps.Count>0){
                int cw=pnlStepFlow.ClientSize.Width; if(cw<100)cw=290;
                int cardW=cw-10;
                int y=4;
                int i=0;
                while(i<_current.Steps.Count){
                    var step=_current.Steps[i];
                    // 순차 연결 화살표
                    if(i>0){
                        var arrow=new Label{Text="↓",TextAlign=ContentAlignment.MiddleCenter,Location=new System.Drawing.Point(4+cardW/2-10,y),Size=new System.Drawing.Size(20,18),Font=new Font("맑은 고딕",9),ForeColor=Color.FromArgb(160,160,175)};
                        pnlStepFlow.Controls.Add(arrow);y+=18;
                    }
                    // 동시 인식 그룹 감지
                    if(step.Type==StepType.Simultaneous){
                        int gid=step.GroupId;
                        var grpIndices=new List<int>{i};
                        int j=i+1;
                        while(j<_current.Steps.Count&&_current.Steps[j].Type==StepType.Simultaneous&&_current.Steps[j].GroupId==gid){grpIndices.Add(j);j++;}
                        if(grpIndices.Count>1){
                            // 그룹 래퍼
                            var wrapper=MakeGroupWrapper(grpIndices,_current.Steps,cardW,sel);
                            wrapper.Location=new System.Drawing.Point(4,y);
                            pnlStepFlow.Controls.Add(wrapper);
                            y+=wrapper.Height+4;
                            // 그룹 내 마지막 스텝의 WatchTargets 분기 바
                            var lastStep=_current.Steps[grpIndices[^1]];
                            if(!string.IsNullOrWhiteSpace(lastStep.WatchTargets)){
                                var branchBar=MakeBranchBar(lastStep,cardW);
                                branchBar.Location=new System.Drawing.Point(4,y);
                                pnlStepFlow.Controls.Add(branchBar);y+=branchBar.Height+2;
                            }
                            i=j;
                            continue;
                        }
                    }
                    // 단일 카드
                    var card=MakeStepCard(i,step,cardW,sel==i);
                    card.Location=new System.Drawing.Point(4,y);
                    pnlStepFlow.Controls.Add(card);
                    y+=card.Height+4;
                    // WatchTargets 분기 바
                    if(!string.IsNullOrWhiteSpace(step.WatchTargets)){
                        var branchBar=MakeBranchBar(step,cardW);
                        branchBar.Location=new System.Drawing.Point(4,y);
                        pnlStepFlow.Controls.Add(branchBar);y+=branchBar.Height+2;
                    }
                    i++;
                }
            }
            _selectedCardIdx=sel;
            pnlStepFlow.ResumeLayout(true);
            // 선택된 카드가 화면 밖이면 그쪽으로 스크롤 (추가/이동 뱃지 클릭 시)
            if(sel>=0){
                BeginInvoke(new Action(()=>{
                    try{var c=FindStepCard(sel); if(c!=null)pnlStepFlow.ScrollControlIntoView(c);}catch{}
                }));
            }
            // 레이아웃 완료 후 스크롤 위치 복원
            else if(scrollPos>0){
                BeginInvoke(new Action(()=>{
                    try{
                        int max=pnlStepFlow.VerticalScroll.Maximum;
                        if(max>0)pnlStepFlow.AutoScrollPosition=new System.Drawing.Point(0,Math.Min(scrollPos,max));
                    }catch{}
                }));
            }
            _suppressStepEvt=false;
        }

        // 스텝 번호에 해당하는 카드 컨트롤 찾기 (묶음 그룹 안쪽까지 뒤진다)
        Control? FindStepCard(int idx)
        {
            Control? Dig(Control parent){
                foreach(Control c in parent.Controls){
                    if(c is Panel pp&&pp.Tag is int t&&t==idx)return pp;
                    var r=Dig(c); if(r!=null)return r;
                }
                return null;
            }
            return Dig(pnlStepFlow);
        }

        // NumericUpDown에 값을 설정하면서 Text도 강제 동기화 (같은 Value일 때 Text가 갱신 안 되는 WinForms 버그 방지)
        static void SetNud(NumericUpDown nud, decimal val)
        {
            val=Math.Clamp(val,nud.Minimum,nud.Maximum);
            nud.Value=val;
            nud.Text=val.ToString();
        }

        void LoadStepUI(MacroStep st)
        {
            nudClicks.ValueChanged-=OnNudClicks; nudCDelay.ValueChanged-=OnNudCDelay;
            cmbStepType.SelectedIndexChanged-=OnStepTypeChanged;
            cmbStepType.SelectedIndex=st.Type switch{StepType.Sequential=>0,StepType.Simultaneous=>1,StepType.KeyInput=>2,StepType.MouseMove=>3,StepType.Delay=>4,StepType.Notification=>5,StepType.ToggleSteps=>6,_=>0};
            SetNud(nudWaitAfter,st.WaitAfter); chkEnabled.Checked=st.Enabled; chkStartOff.Checked=st.StartDisabled;
            SetNud(nudJump,st.JumpOnSuccess);
            ShowPanelForType(st.Type);
            // 이미지
            lblImgPath.Text=string.IsNullOrEmpty(st.ImagePath)?"(선택 안됨)":Path.GetFileName(st.ImagePath);
            SetNud(nudGroupId,st.GroupId);
            rdClickR.Checked=st.RightClick; rdClickL.Checked=!st.RightClick;
            SetNud(nudClicks,st.ClickCount); SetNud(nudCDelay,st.ClickDelay);
            SetNud(nudTimeout,st.Timeout); tbConf.Value=Math.Clamp(st.Confidence,50,99); lblConf.Text=$"{st.Confidence}%";
            SetNud(nudOffsetX,st.ClickOffsetX); SetNud(nudOffsetY,st.ClickOffsetY);
            lblTestResult.Text="";
            try{if(!string.IsNullOrEmpty(st.ImagePath)){picPreview.Image=System.Drawing.Image.FromFile(st.ImagePath);if(st.UseCustomOffset)RecalcPreviewClickPt(st);else _previewClickPt=null;}else{picPreview.Image=null;_previewClickPt=null;}}catch{picPreview.Image=null;_previewClickPt=null;}
            UpdateClickPanel(); picPreview.Invalidate();
            // 키
            txtKeyText.Text=st.KeyText;
            txtHotKey.Text=string.IsNullOrEmpty(st.HotKey)?"(없음)":st.HotKey;
            // 마우스
            SetNud(nudMoveX,st.MoveX); SetNud(nudMoveY,st.MoveY);
            rdMoveRel.Checked=st.MoveRelative; rdMoveAbs.Checked=!st.MoveRelative;
            if(st.MoveAction==MoveAction.LeftClick)rdMoveLeft.Checked=true;
            else if(st.MoveAction==MoveAction.RightClick)rdMoveRight.Checked=true;
            else rdMoveOnly.Checked=true;
            lblPickPos.Text="위 버튼 클릭 후 3초 안에 원하는 위치로 마우스를 이동하세요."; lblPickPos.ForeColor=Color.Gray;
            // 대기
            SetNud(nudDelayMs,st.DelayMs); cmbDelayUnit.SelectedIndex=Math.Clamp(st.DelayUnit,0,2); UpdateDelayHint(st);
            txtToggleTargets.Text=st.ToggleTargets; cmbToggleAction.SelectedIndex=(int)st.ToggleAction;
            // 알림
            txtNotifText.Text=st.NotificationText;
            // 감시 대상
            txtWatchTargets.Text=st.WatchTargets;
            nudClicks.ValueChanged+=OnNudClicks; nudCDelay.ValueChanged+=OnNudCDelay;
            cmbStepType.SelectedIndexChanged+=OnStepTypeChanged;
        }

        void OnDelStep(object? s,EventArgs e){if(_current==null||_selectedCardIdx<0||_selectedCardIdx>=_current.Steps.Count)return;int idx=_selectedCardIdx;_current.Steps.RemoveAt(idx);_selStep=null;int ns=Math.Min(idx,_current.Steps.Count-1);_selectedCardIdx=ns;RefreshStepList(ns);if(ns<0)pnlRightScroll.Enabled=false;btnRun.Enabled=_current.Steps.Count>0;}
        void OnStepUp(object? s,EventArgs e){if(_current==null||_selectedCardIdx<=0)return;int i=_selectedCardIdx;var t=_current.Steps[i];_current.Steps[i]=_current.Steps[i-1];_current.Steps[i-1]=t;RefreshStepList(i-1);}
        void OnStepDn(object? s,EventArgs e){if(_current==null||_selectedCardIdx<0||_selectedCardIdx>=_current.Steps.Count-1)return;int i=_selectedCardIdx;var t=_current.Steps[i];_current.Steps[i]=_current.Steps[i+1];_current.Steps[i+1]=t;RefreshStepList(i+1);}
        void OnPickImage(object? s,EventArgs e){if(_selStep==null)return;using var dlg=new OpenFileDialog{Title="이미지 선택",Filter="이미지 파일|*.png;*.jpg;*.jpeg;*.bmp"};if(dlg.ShowDialog()!=DialogResult.OK)return;_selStep.ImagePath=dlg.FileName;_selStep.UseCustomOffset=false;_selStep.ClickOffsetX=0;_selStep.ClickOffsetY=0;lblImgPath.Text=Path.GetFileName(dlg.FileName);nudOffsetX.Value=0;nudOffsetY.Value=0;_previewClickPt=null;lblTestResult.Text="";try{picPreview.Image=System.Drawing.Image.FromFile(dlg.FileName);}catch{}UpdateClickPanel();picPreview.Invalidate();RefreshStepList();}

        // ══════════════════════════════════════════════════════
        //  저장 / 불러오기
        // ══════════════════════════════════════════════════════
        void OnSave(object? s,EventArgs e){using var dlg=new SaveFileDialog{Title="저장",Filter="매크로 파일|*.macros.json",FileName="macros"};if(dlg.ShowDialog()!=DialogResult.OK)return;File.WriteAllText(dlg.FileName,JsonSerializer.Serialize(_macros,new JsonSerializerOptions{WriteIndented=true}));SetStatus($"저장: {Path.GetFileName(dlg.FileName)}");}
        void OnLoadFile(object? s,EventArgs e){using var dlg=new OpenFileDialog{Title="파일 불러오기",Filter="매크로 파일|*.macros.json"};if(dlg.ShowDialog()!=DialogResult.OK)return;try{var loaded=JsonSerializer.Deserialize<List<MacroItem>>(File.ReadAllText(dlg.FileName));if(loaded==null)throw new Exception();_macros=loaded;_current=null;_selStep=null;RefreshMacroList();pnlStepFlow.Controls.Clear();_selectedCardIdx=-1;pnlRightScroll.Enabled=false;SetStatus($"불러오기: {loaded.Count}개");}catch(Exception ex){MessageBox.Show("불러오기 실패: "+ex.Message);}}

        // ══════════════════════════════════════════════════════
        //  실행 / 정지
        // ══════════════════════════════════════════════════════
        void OnRun(object? s,EventArgs e)
        {
            if(_current==null||_current.Steps.Count==0||_isRunning)return;
            if(_current.EventMode){
                foreach(var st in _current.Steps)if(st.Enabled&&st.Type==StepType.Simultaneous&&string.IsNullOrEmpty(st.ImagePath)){MessageBox.Show("이미지 파일이 지정되지 않은 '묶어 찾기' 스텝이 있습니다.","실행 불가");return;}
            }else{
                foreach(var st in _current.Steps)if(st.Enabled&&(st.Type==StepType.Sequential||st.Type==StepType.Simultaneous||st.Type==StepType.ToggleSteps)&&string.IsNullOrEmpty(st.ImagePath)){MessageBox.Show("이미지 파일이 지정되지 않은 스텝이 있습니다.","실행 불가");return;}
            }
            _isRunning=true; btnRun.Enabled=false; btnStop.Enabled=true;
            _searchMonitor=_current.SearchMonitor;
            _clickMode=_current.ClickMode;
            if(_clickMode==ClickMode.Adb&&!PrepareAdb(_current)){_isRunning=false;btnRun.Enabled=true;btnStop.Enabled=false;return;}
            var snap=_current.Clone();
            int startOff=ApplyStartState(snap);
            if(!snap.EventMode&&!snap.Steps.Exists(st=>st.Enabled)){
                MessageBox.Show("모든 스텝이 꺼진 상태로 시작하게 되어 있어 아무것도 실행되지 않습니다.\n\n"+
                                "'스텝 켜고 끄기' 스텝만큼은 '시작할 때 꺼둠' 을 풀어주세요.","실행 불가");
                _isRunning=false;btnRun.Enabled=true;btnStop.Enabled=false;return;
            }
            if(snap.EventMode)_thread=new Thread(()=>{try{EventLoop(snap);}catch(Exception ex){BeginInvoke(()=>{_isRunning=false;btnRun.Enabled=true;btnStop.Enabled=false;SetStatus($"오류: {ex.Message}");});}});
            else _thread=new Thread(()=>{try{MacroLoop(snap);}catch(Exception ex){BeginInvoke(()=>{_isRunning=false;btnRun.Enabled=true;btnStop.Enabled=false;SetStatus($"오류: {ex.Message}");});}});
            _thread.IsBackground=true; _thread.Start();
            string mode=snap.EventMode?"항상 감시":$"반복:{(snap.RepeatCount==0?"무한":snap.RepeatCount+"회")}";
            SetStatus($"'{snap.Name}' 실행 중! {mode}"+(startOff>0?$"  (꺼진 채 시작: {startOff}개)":""));
        }
        void OnStopMacro(object? s,EventArgs e){_isRunning=false;btnRun.Enabled=_current?.Steps.Count>0;btnStop.Enabled=false;SetStatus("정지됨.");} // 정지됨 유지

        // ══════════════════════════════════════════════════════
        //  매크로 루프
        // ══════════════════════════════════════════════════════
        // JumpOnSuccess: 0=다음 스텝, N=N번 스텝(1-based)으로 이동
        static int JmpTo(MacroStep step,int defaultNext)=>step.JumpOnSuccess>0?step.JumpOnSuccess-1:defaultNext;

        // WatchTargets 문자열 → 0-based 인덱스 리스트 파싱
        static List<int> ParseWatchTargets(string wt,int stepCount)
        {
            var result=new List<int>();
            if(string.IsNullOrWhiteSpace(wt))return result;
            foreach(var tok in wt.Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries)){
                if(int.TryParse(tok,out int n)&&n>=1&&n<=stepCount)result.Add(n-1);
            }
            return result;
        }

        // 완료 후 감시: 지정된 스텝들의 이미지를 동시에 스캔, 먼저 인식된 스텝의 0-based 인덱스 반환
        int RunWatch(MacroItem macro,MacroStep currentStep,List<int> targets,Dictionary<string,Bitmap> bitmaps,int loop)
        {
            string ri=macro.RepeatCount==0?$"{loop+1}/무한":$"{loop+1}/{macro.RepeatCount}";
            var names=new List<string>();
            foreach(int ti in targets){var st=macro.Steps[ti];names.Add($"{ti+1}:{Path.GetFileName(st.ImagePath)}");}
            string watchLabel=string.Join(", ",names);
            while(_isRunning&&!_restartRequested){
                SetStatus($"[{macro.Name}] {ri}  지켜보는 중: [{watchLabel}]");
                using var ss=CaptureScreen(out var capOrg);using Mat src=BitmapToMat(ss);using Mat g=new Mat();
                Cv2.CvtColor(src,g,ColorConversionCodes.BGRA2GRAY);
                foreach(int ti in targets){
                    var st=macro.Steps[ti];
                    if(string.IsNullOrEmpty(st.ImagePath)||!bitmaps.TryGetValue(st.ImagePath,out var tmpl))continue;
                    var pos=MatchTpl(g,tmpl,st.Confidence/100.0,capOrg);
                    if(pos.HasValue){
                        SetStatus($"[{macro.Name}] {ri}  스텝{ti+1} 먼저 뜸 → 스텝{ti+1}로 이동");
                        return ti;
                    }
                }
                Thread.Sleep(macro.ScanInterval);
            }
            return -1;
        }


        // 스텝 완료 후 다음 이동 결정: WatchTargets가 있으면 JumpOnSuccess 대상 + WatchTargets를 동시 감시
        int NextAfterStep(MacroItem macro,MacroStep step,int defaultNext,Dictionary<string,Bitmap> bitmaps,int loop)
        {
            var wt=ParseWatchTargets(step.WatchTargets,macro.Steps.Count);
            if(wt.Count>0){
                // JumpOnSuccess 대상도 감시 목록에 자동 포함
                int jmpTarget=step.JumpOnSuccess>0?step.JumpOnSuccess-1:defaultNext;
                if(jmpTarget>=0&&jmpTarget<macro.Steps.Count&&!wt.Contains(jmpTarget))
                    wt.Insert(0,jmpTarget);
                int watchResult=RunWatch(macro,step,wt,bitmaps,loop);
                if(watchResult>=0)return watchResult;
                return defaultNext; // 정지 시 기본값
            }
            return JmpTo(step,defaultNext);
        }

        // 실행을 시작할 때 '시작할 때 꺼둠' 스텝을 꺼둔다. 몇 개를 껐는지 돌려준다.
        // 실행용 복사본에만 적용하므로 저장된 매크로는 그대로 남는다.
        static int ApplyStartState(MacroItem m)
        {
            int n=0;
            foreach(var st in m.Steps)if(st.StartDisabled&&st.Enabled){st.Enabled=false;n++;}
            return n;
        }

        void MacroLoop(MacroItem macro)
        {
            var bitmaps=LoadBitmaps(macro); int loopDone=0;
            while(_isRunning){
                _restartRequested=false;
                bool ranSomething=false;
                int i=0;
                while(i<macro.Steps.Count&&_isRunning&&!_restartRequested){
                    var step=macro.Steps[i];
                    if(!step.Enabled){i++;continue;}
                    ranSomething=true;
                    bool ok=true;
                    if(step.Type==StepType.Sequential){ok=RunSeq(macro,step,bitmaps,i,loopDone);i=ok?NextAfterStep(macro,step,i+1,bitmaps,loopDone):i+1;}
                    else if(step.Type==StepType.Simultaneous){
                        // 같은 GroupId 묶기
                        int gid=step.GroupId;
                        var grp=new List<(int idx,MacroStep st)>();
                        int j=i;
                        // 같은 묶음 번호가 이어지는 데까지가 한 묶음이다.
                        // 꺼진 멤버는 후보에서만 빼고 묶음을 끊지 않는다.
                        // (끊어버리면 뒤쪽 멤버가 통째로 빠져 묶음이 둘로 쪼개진다)
                        while(j<macro.Steps.Count&&macro.Steps[j].Type==StepType.Simultaneous&&macro.Steps[j].GroupId==gid){
                            if(macro.Steps[j].Enabled)grp.Add((j,macro.Steps[j]));
                            j++;
                        }
                        var(simOk,simMatched)=RunSim(macro,grp,bitmaps,loopDone); ok=simOk;
                        if(simMatched!=null)i=NextAfterStep(macro,simMatched,j,bitmaps,loopDone);else i=j;
                    }
                    else if(step.Type==StepType.KeyInput){RunKey(step);i=NextAfterStep(macro,step,i+1,bitmaps,loopDone);}
                    else if(step.Type==StepType.MouseMove){RunMouseMove(step,macro);i=NextAfterStep(macro,step,i+1,bitmaps,loopDone);}
                    else if(step.Type==StepType.Delay){SetStatus($"[{macro.Name}] 스텝{i+1}: {step.DelayMs}{MacroItem.UnitName(step.DelayUnit)} 대기...");int rem=step.DelayEffectiveMs;while(rem>0&&_isRunning){int ch=Math.Min(rem,100);Thread.Sleep(ch);rem-=ch;}if(step.WaitAfter>0)Thread.Sleep(step.WaitAfter);i=NextAfterStep(macro,step,i+1,bitmaps,loopDone);}
                    else if(step.Type==StepType.ToggleSteps){RunToggle(macro,step,bitmaps,i);i=NextAfterStep(macro,step,i+1,bitmaps,loopDone);}
                    else if(step.Type==StepType.Notification){if(!string.IsNullOrEmpty(step.NotificationText))ShowNotification(macro.Name,step.NotificationText);if(step.WaitAfter>0)Thread.Sleep(step.WaitAfter);i=NextAfterStep(macro,step,i+1,bitmaps,loopDone);}
                    if(!ok)break;
                }
                if(!_isRunning)break;
                if(_restartRequested)continue;
                // 전부 꺼져 있으면 한 바퀴가 순식간에 끝나 CPU 를 태운다 → 탐색 주기만큼 쉰다
                if(!ranSomething){
                    SetStatus($"[{macro.Name}] 켜져 있는 스텝이 없습니다 — 스위치가 켜주기를 기다리는 중...");
                    int rest=Math.Max(macro.ScanInterval,100);
                    while(rest>0&&_isRunning){int ch=Math.Min(rest,100);Thread.Sleep(ch);rest-=ch;}
                }
                loopDone++;
                if(macro.RepeatCount>0&&loopDone>=macro.RepeatCount){SetStatus($"[{macro.Name}] {macro.RepeatCount}회 반복 완료.");Invoke(OnStopMacro,null,EventArgs.Empty);break;}
                WaitBeforeNextLoop(macro,loopDone);
            }
            DisposeBitmaps(bitmaps);
        }

        // 한 바퀴 다 돌고 다음 반복까지 쉰다. 정지 버튼에 바로 반응하도록 잘게 끊어서 잔다.
        void WaitBeforeNextLoop(MacroItem macro,int loopDone)
        {
            int ms=macro.LoopDelayMs;
            if(ms<=0)return;
            string unit=macro.LoopDelayUnit==2?"분":macro.LoopDelayUnit==1?"초":"ms";
            var sw=System.Diagnostics.Stopwatch.StartNew();
            while(_isRunning&&!_restartRequested&&sw.ElapsedMilliseconds<ms){
                int left=(int)((ms-sw.ElapsedMilliseconds+999)/1000);
                SetStatus($"[{macro.Name}] {loopDone}회 완료 — 다음 반복까지 {macro.LoopDelay}{unit} 대기 중 ({left}초 남음)");
                Thread.Sleep(100);
            }
        }

        bool RunSeq(MacroItem macro,MacroStep step,Dictionary<string,Bitmap> bitmaps,int idx,int loop)
        {
            if(!bitmaps.TryGetValue(step.ImagePath,out var tmpl))return true;
            string ri=macro.RepeatCount==0?$"{loop+1}/무한":$"{loop+1}/{macro.RepeatCount}";
            var sw=System.Diagnostics.Stopwatch.StartNew();
            while(_isRunning&&!_restartRequested){
                SetStatus($"[{macro.Name}] {ri}  스텝{idx+1}: '{Path.GetFileName(step.ImagePath)}' 찾는 중...");
                using var ss=CaptureScreen(out var capOrg); using Mat src=BitmapToMat(ss); using Mat g=new Mat(); Cv2.CvtColor(src,g,ColorConversionCodes.BGRA2GRAY);
                var pos=MatchTpl(g,tmpl,step.Confidence/100.0,capOrg);
                if(pos.HasValue){int cx=pos.Value.X+step.ClickOffsetX,cy=pos.Value.Y+step.ClickOffsetY;SetStatus($"[{macro.Name}] {ri}  스텝{idx+1}: 발견! ({cx},{cy}) 클릭");DoClick(cx,cy,step);if(step.WaitAfter>0)Thread.Sleep(step.WaitAfter);return true;}
                if(step.Timeout>0&&sw.ElapsedMilliseconds>=step.Timeout){HandleTimeout(macro,idx);return false;}
                Thread.Sleep(macro.ScanInterval);
            }
            return false;
        }

        (bool ok,MacroStep? matched) RunSim(MacroItem macro,List<(int idx,MacroStep step)> group,Dictionary<string,Bitmap> bitmaps,int loop)
        {
            if(group.Count==0)return (true,null);
            string ri=macro.RepeatCount==0?$"{loop+1}/무한":$"{loop+1}/{macro.RepeatCount}";
            int gto=0; foreach(var(_,st)in group)if(st.Timeout>0)gto=gto==0?st.Timeout:Math.Min(gto,st.Timeout);
            var sw=System.Diagnostics.Stopwatch.StartNew();
            while(_isRunning&&!_restartRequested){
                using var ss=CaptureScreen(out var capOrg); using Mat src=BitmapToMat(ss); using Mat g=new Mat(); Cv2.CvtColor(src,g,ColorConversionCodes.BGRA2GRAY);
                var names=new List<string>(); foreach(var(_,st)in group)names.Add(Path.GetFileName(st.ImagePath));
                SetStatus($"[{macro.Name}] {ri}  묶음{group[0].step.GroupId}: {string.Join(" / ",names)} 중 먼저 뜨는 것 찾는 중...");
                foreach(var(idx,st)in group){if(!bitmaps.TryGetValue(st.ImagePath,out var tmpl))continue;var pos=MatchTpl(g,tmpl,st.Confidence/100.0,capOrg);if(pos.HasValue){int cx=pos.Value.X+st.ClickOffsetX,cy=pos.Value.Y+st.ClickOffsetY;SetStatus($"[{macro.Name}] {ri}  묶음{st.GroupId}: 스텝{idx+1} 먼저 뜸! ({cx},{cy}) 나머지 건너뜀");DoClick(cx,cy,st);if(st.WaitAfter>0)Thread.Sleep(st.WaitAfter);return (true,st);}}
                if(gto>0&&sw.ElapsedMilliseconds>=gto){HandleTimeout(macro,group[0].idx);return (false,null);}
                Thread.Sleep(macro.ScanInterval);
            }
            return (false,null);
        }

        void RunKey(MacroStep step)
        {
            SetStatus($"키보드 입력: 텍스트='{step.KeyText}'  단축키='{step.HotKey}'");
            if(!string.IsNullOrEmpty(step.KeyText)){
                string? prev=null;
                Invoke((Action)(()=>{try{prev=Clipboard.GetText();}catch{}Clipboard.SetText(step.KeyText);}));
                Thread.Sleep(80);
                keybd_event(0x11,0,0,IntPtr.Zero);keybd_event(0x56,0,0,IntPtr.Zero);
                Thread.Sleep(30);
                keybd_event(0x56,0,KEY_UP,IntPtr.Zero);keybd_event(0x11,0,KEY_UP,IntPtr.Zero);
                Thread.Sleep(150);
                if(!string.IsNullOrEmpty(prev))Invoke((Action)(()=>{try{Clipboard.SetText(prev);}catch{};}));
            }
            if(!string.IsNullOrEmpty(step.HotKey)){
                string hk=step.HotKey; bool c=hk.Contains("Ctrl"),a=hk.Contains("Alt"),sh=hk.Contains("Shift");
                string[] parts=hk.Split('+');
                if(Enum.TryParse<Keys>(parts[^1],out var k)){
                    byte vk=(byte)k;
                    if(c) keybd_event(0x11,0,0,IntPtr.Zero);
                    if(a) keybd_event(0x12,0,0,IntPtr.Zero);
                    if(sh)keybd_event(0x10,0,0,IntPtr.Zero);
                    keybd_event(vk,0,0,IntPtr.Zero); Thread.Sleep(30); keybd_event(vk,0,KEY_UP,IntPtr.Zero);
                    if(sh)keybd_event(0x10,0,KEY_UP,IntPtr.Zero);
                    if(a) keybd_event(0x12,0,KEY_UP,IntPtr.Zero);
                    if(c) keybd_event(0x11,0,KEY_UP,IntPtr.Zero);
                }
            }
            if(step.WaitAfter>0)Thread.Sleep(step.WaitAfter);
        }

        void RunMouseMove(MacroStep step,MacroItem macro)
        {
            int tx,ty;
            if(step.MoveRelative){GetCursorPos(out POINT cur);tx=cur.X+step.MoveX;ty=cur.Y+step.MoveY;}
            else{tx=step.MoveX;ty=step.MoveY;}
            if(_clickMode!=ClickMode.Adb)SetCursorPos(tx,ty);
            if(step.MoveAction==MoveAction.LeftClick){
                if(_clickMode!=ClickMode.Normal){ClickAt(tx,ty,false);}
                else{Thread.Sleep(50);mouse_event(LDOWN,tx,ty,0,IntPtr.Zero);Thread.Sleep(50);mouse_event(LUP,tx,ty,0,IntPtr.Zero);}
                SetStatus($"마우스 좌클릭: ({tx},{ty})");
            }else if(step.MoveAction==MoveAction.RightClick){
                if(_clickMode!=ClickMode.Normal){ClickAt(tx,ty,true);}
                else{Thread.Sleep(50);mouse_event(RDOWN,tx,ty,0,IntPtr.Zero);Thread.Sleep(50);mouse_event(RUP,tx,ty,0,IntPtr.Zero);}
                SetStatus($"마우스 우클릭: ({tx},{ty})");
            }else{
                SetStatus($"마우스 이동: ({tx},{ty})");
            }
            if(step.WaitAfter>0)Thread.Sleep(step.WaitAfter);
        }

        static string ToggleActionName(ToggleAction a)=>a switch{ToggleAction.On=>"켜기",ToggleAction.Flip=>"반대로",ToggleAction.OnlyThese=>"만 켜기(나머지 끄기)",_=>"끄기"};

        // 지정한 이미지가 지금 화면에 보이면 대상 스텝들을 켜거나 끈다.
        // 안 보이면 아무것도 하지 않고 그냥 지나간다 (기다리지 않는다).
        // macro 는 실행용 복사본이라 저장된 매크로는 바뀌지 않는다.
        void RunToggle(MacroItem macro,MacroStep step,Dictionary<string,Bitmap> bitmaps,int idx)
        {
            if(string.IsNullOrEmpty(step.ImagePath)||!bitmaps.TryGetValue(step.ImagePath,out var tmpl))return;
            var targets=ParseWatchTargets(step.ToggleTargets,macro.Steps.Count);
            if(targets.Count==0)return;

            using var ss=CaptureScreen(out var capOrg);
            using Mat src=BitmapToMat(ss); using Mat g=new Mat();
            Cv2.CvtColor(src,g,ColorConversionCodes.BGRA2GRAY);
            if(!MatchTpl(g,tmpl,step.Confidence/100.0,capOrg).HasValue)return;

            var changed=new List<string>();
            if(step.ToggleAction==ToggleAction.OnlyThese){
                // 적은 것만 켜고 나머지는 끈다.
                // 스위치 스텝까지 꺼버리면 다시 켤 방법이 없어지므로 건드리지 않는다.
                var want=new HashSet<int>(targets);
                for(int ti=0;ti<macro.Steps.Count;ti++){
                    if(macro.Steps[ti].Type==StepType.ToggleSteps)continue;
                    bool on=want.Contains(ti);
                    if(macro.Steps[ti].Enabled!=on){macro.Steps[ti].Enabled=on;changed.Add($"{ti+1}{(on?"켬":"끔")}");}
                }
            }else{
                foreach(int ti in targets){
                    bool on=step.ToggleAction switch{
                        ToggleAction.On=>true,
                        ToggleAction.Flip=>!macro.Steps[ti].Enabled,
                        _=>false};
                    if(macro.Steps[ti].Enabled!=on){macro.Steps[ti].Enabled=on;changed.Add($"{ti+1}");}
                }
            }
            string what=step.ToggleAction switch{ToggleAction.On=>"켬",ToggleAction.Flip=>"뒤집음",ToggleAction.OnlyThese=>"만 켬",_=>"끔"};
            string where=idx>=0?$"스텝{idx+1}":"[항상감시]";
            SetStatus(changed.Count>0
                ? $"[{macro.Name}] {where}: '{Path.GetFileName(step.ImagePath)}' 보임 → 스텝 {string.Join(",",changed)} {what}"
                : $"[{macro.Name}] {where}: '{Path.GetFileName(step.ImagePath)}' 보임 (이미 {what} 상태)");
            if(step.WaitAfter>0)Thread.Sleep(step.WaitAfter);
        }

        void HandleTimeout(MacroItem macro,int stepIdx)
        {
            if(macro.OnTimeout==TimeoutAction.Restart){SetStatus($"[{macro.Name}] 스텝{stepIdx+1}: 시간 초과 → {macro.RestartDelay}ms 후 다시 실행...");if(macro.RestartDelay>0)Thread.Sleep(macro.RestartDelay);_restartRequested=true;}
            else{SetStatus($"[{macro.Name}] 스텝{stepIdx+1}: 시간 초과 → 정지");Invoke(OnStopMacro,null,EventArgs.Empty);}
        }

        // ══════════════════════════════════════════════════════
        //  이벤트 감시 모드
        // ══════════════════════════════════════════════════════
        void EventLoop(MacroItem macro)
        {
            var bitmaps=LoadBitmaps(macro);
            var triggers=new List<(int idx,MacroStep step)>();
            for(int i=0;i<macro.Steps.Count;i++){var st=macro.Steps[i];if(!st.Enabled)continue;if(st.Type!=StepType.Simultaneous)continue;if(string.IsNullOrEmpty(st.ImagePath))continue;triggers.Add((i,st));}
            if(triggers.Count==0){Invoke(()=>{MessageBox.Show("'먼저 뜨는 것 클릭' 스텝이 없습니다.\n항상 감시 모드는 그 스텝들만 지켜봅니다.","항상 감시 모드");OnStopMacro(null,EventArgs.Empty);});DisposeBitmaps(bitmaps);return;}
            SetStatus($"[{macro.Name}] 항상 감시 시작 ({triggers.Count}개 이미지 지켜보는 중)");
            while(_isRunning){
                using var ss=CaptureScreen(out var capOrg); using Mat src=BitmapToMat(ss); using Mat g=new Mat(); Cv2.CvtColor(src,g,ColorConversionCodes.BGRA2GRAY);
                bool anyFired=false;
                foreach(var(idx,step)in triggers){
                    if(!_isRunning)break;
                    if(!bitmaps.TryGetValue(step.ImagePath,out var tmpl))continue;
                    var pos=MatchTpl(g,tmpl,step.Confidence/100.0,capOrg);
                    if(!pos.HasValue)continue;
                    int cx=pos.Value.X+step.ClickOffsetX,cy=pos.Value.Y+step.ClickOffsetY;
                    DoClick(cx,cy,step); if(step.WaitAfter>0)Thread.Sleep(step.WaitAfter);
                    int actionIdx=step.JumpOnSuccess-1;
                    if(actionIdx>=0&&actionIdx<macro.Steps.Count){
                        SetStatus($"[{macro.Name}] [항상감시] 스텝{idx+1} 발견 → 스텝{step.JumpOnSuccess} 실행");
                        ExecuteActionStep(macro.Steps[actionIdx],macro,bitmaps);
                    }else{
                        SetStatus($"[{macro.Name}] [항상감시] 스텝{idx+1} 발견 → 클릭 실행");
                    }
                    anyFired=true;
                }
                if(_isRunning&&!anyFired)Thread.Sleep(macro.ScanInterval);
            }
            DisposeBitmaps(bitmaps);
        }

        void ExecuteActionStep(MacroStep action,MacroItem macro,Dictionary<string,Bitmap> bitmaps)
        {
            switch(action.Type){
                case StepType.KeyInput:RunKey(action);break;
                case StepType.MouseMove:RunMouseMove(action,macro);break;
                case StepType.ToggleSteps:RunToggle(macro,action,bitmaps,-1);break;
                case StepType.Delay:int rem=action.DelayEffectiveMs;while(rem>0&&_isRunning){int ch=Math.Min(rem,100);Thread.Sleep(ch);rem-=ch;}break;
                case StepType.Notification:if(!string.IsNullOrEmpty(action.NotificationText))ShowNotification(macro.Name,action.NotificationText);break;
                case StepType.Sequential:case StepType.Simultaneous:
                    if(bitmaps.TryGetValue(action.ImagePath,out var tmpl)){using var ss=CaptureScreen(out var capOrg);using Mat src=BitmapToMat(ss);using Mat g2=new Mat();Cv2.CvtColor(src,g2,ColorConversionCodes.BGRA2GRAY);var pos=MatchTpl(g2,tmpl,action.Confidence/100.0,capOrg);if(pos.HasValue){int ax=pos.Value.X+action.ClickOffsetX,ay=pos.Value.Y+action.ClickOffsetY;DoClick(ax,ay,action);}}break;
            }
            if(action.WaitAfter>0)Thread.Sleep(action.WaitAfter);
        }

        // ══════════════════════════════════════════════════════
        //  플로우 전용 창
        // ══════════════════════════════════════════════════════
        // ══════════════════════════════════════════════════════
        //  실행 흐름 보기
        //  스텝을 여러 칸(세로줄)에 나눠 담아 가로로 퍼지게 하고,
        //  건너뛰는 흐름은 카드 사이 빈 통로로만 지나가게 해서
        //  선이 카드 뒤로 숨지 않도록 한다.
        //  나가는 곳은 카드 오른쪽(● + 대상 번호),
        //  들어오는 곳은 카드 왼쪽(▶ + 출발 번호)에 표시한다.
        // ══════════════════════════════════════════════════════
        void ShowFlowWindow()
        {
            if(_current==null||_current.Steps.Count==0){MessageBox.Show("스텝이 없습니다.","실행 흐름 보기");return;}
            var steps=_current.Steps; int n=steps.Count;
            const int CW=250,CH=58,VGAP=32,COLGAP=86,LM=58,GPAD=7,GHEAD=20,LANE=16,BANDH=15;
            var cOut=Color.FromArgb(205,85,0);      // 실행 후 이동
            var cWatch=Color.FromArgb(0,140,160);   // 끝난 뒤 지켜보기
            var cTog=Color.FromArgb(130,40,170);    // 스텝 켜고 끄기
            var cSeq=Color.FromArgb(155,155,170);   // 순서대로

            // ── 1) 블록 만들기 (단일 카드 또는 묶음 그룹) ──────
            var bMem=new List<List<int>>(); var bGid=new List<int>(); var bH=new List<int>();
            for(int i=0;i<n;){
                var st=steps[i];
                if(st.Type==StepType.Simultaneous){
                    int gid=st.GroupId; var mem=new List<int>{i}; int j=i+1;
                    while(j<n&&steps[j].Type==StepType.Simultaneous&&steps[j].GroupId==gid){mem.Add(j);j++;}
                    if(mem.Count>1){
                        bMem.Add(mem); bGid.Add(gid);
                        bH.Add(GHEAD+mem.Count*CH+(mem.Count-1)*4+GPAD);
                        i=j; continue;
                    }
                }
                bMem.Add(new List<int>{i}); bGid.Add(-1); bH.Add(CH); i++;
            }
            int nb=bMem.Count;
            int sumH=0; foreach(int h in bH)sumH+=h+VGAP; sumH-=VGAP;

            // ── 2) 몇 칸으로 나눌지 (가로세로가 비슷해지게) ────
            var wa=Screen.FromControl(this).WorkingArea;
            int maxCols=Math.Max(1,Math.Min(5,(wa.Width*9/10-LM-COLGAP)/(CW+COLGAP)));
            int cols=1,bestScore=int.MaxValue;
            for(int c=1;c<=Math.Min(maxCols,nb);c++){
                int w=LM+c*CW+c*COLGAP;
                int h=sumH/c+120;
                int score=Math.Abs(w-h)+(h>wa.Height*9/10?1000000:0);
                if(score<bestScore){bestScore=score;cols=c;}
            }

            // ── 3) 블록을 칸에 나눠 담기 ──────────────────────
            int target=sumH/cols;
            var bCol=new int[nb]; var bY=new int[nb];
            int curCol=0,curY=0,used=0;
            for(int k=0;k<nb;k++){
                if(curCol<cols-1&&used>0&&used+bH[k]/2>target){curCol++;curY=0;used=0;}
                bCol[k]=curCol; bY[k]=curY;
                curY+=bH[k]+VGAP; used+=bH[k]+VGAP;
            }
            int colX(int c)=>LM+c*(CW+COLGAP);
            int chX(int c,int lane)=>c<0?8+lane*LANE:colX(c)+CW+10+lane*LANE;   // c번 칸 오른쪽 통로

            var cardRc=new Dictionary<int,Rectangle>();
            var grpRc=new List<(Rectangle rc,int gid)>();
            var blockRc=new Rectangle[nb];
            for(int k=0;k<nb;k++){
                int x=colX(bCol[k]),yy=bY[k];
                if(bGid[k]>=0){
                    blockRc[k]=new Rectangle(x-GPAD,yy,CW+GPAD*2,bH[k]);
                    grpRc.Add((blockRc[k],bGid[k]));
                    int cyy=yy+GHEAD;
                    foreach(int m in bMem[k]){cardRc[m]=new Rectangle(x,cyy,CW,CH);cyy+=CH+4;}
                }else{
                    blockRc[k]=new Rectangle(x,yy,CW,CH);
                    cardRc[bMem[k][0]]=blockRc[k];
                }
            }

            // ── 4) 건너뛰는 흐름 모으기 (1=이동, 2=분기 감시) ──
            var jm=new List<(int from,int to,int kind)>();
            for(int k=0;k<n;k++){
                int jt=steps[k].JumpOnSuccess-1;
                if(steps[k].JumpOnSuccess>0&&jt>=0&&jt<n&&jt!=k)jm.Add((k,jt,1));
                foreach(int w in ParseWatchTargets(steps[k].WatchTargets,n))if(w!=k)jm.Add((k,w,2));
                if(steps[k].Type==StepType.ToggleSteps)
                    foreach(int w in ParseWatchTargets(steps[k].ToggleTargets,n))if(w!=k)jm.Add((k,w,3));
            }
            // 한 카드의 같은 면(왼쪽/오른쪽)에 붙는 점들을 모아서
            // 나가는 것과 들어오는 것을 함께 세로로 흩어놓는다 (겹치면 번호표가 가려진다)
            var srcPt=new System.Drawing.Point[jm.Count]; var dstPt=new System.Drawing.Point[jm.Count];
            var dstFromLeft=new bool[jm.Count];
            var slots=new Dictionary<(int card,bool right),List<(int edge,bool isSrc)>>();
            void Slot(int card,bool right,int edge,bool isSrc){
                var key=(card,right);
                if(!slots.TryGetValue(key,out var list)){list=new List<(int,bool)>();slots[key]=list;}
                list.Add((edge,isSrc));
            }
            for(int k=0;k<jm.Count;k++){
                var(f,t,_)=jm[k];
                int cf0=bCol[BlockOf(bMem,f)],ct0=bCol[BlockOf(bMem,t)];
                dstFromLeft[k]=cf0!=ct0;          // 같은 칸이면 오른쪽으로 들어온다
                Slot(f,true,k,true);              // 나가는 건 언제나 오른쪽
                Slot(t,!dstFromLeft[k],k,false);
            }
            foreach(var kv in slots){
                var rc=cardRc[kv.Key.card];
                var list=kv.Value;
                for(int i2=0;i2<list.Count;i2++){
                    int yy=rc.Y+CH*(i2+1)/(list.Count+1);
                    int xx=kv.Key.right?rc.Right:rc.Left;
                    if(list[i2].isSrc)srcPt[list[i2].edge]=new System.Drawing.Point(xx,yy);
                    else              dstPt[list[i2].edge]=new System.Drawing.Point(xx,yy);
                }
            }

            // ── 5) 통로 차선 배정 (겹치는 선끼리 다른 차선) ────
            var chEnd=new Dictionary<int,List<int>>();       // 칸별 세로 통로
            var bandEnd=new List<int>();                     // 위쪽 가로 통로
            int Lane(int c,int y1,int y2){
                if(!chEnd.TryGetValue(c,out var list)){list=new List<int>();chEnd[c]=list;}
                int lo=Math.Min(y1,y2)-8,hi=Math.Max(y1,y2)+8;
                for(int L=0;L<list.Count;L++)if(list[L]<lo){list[L]=hi;return L;}
                list.Add(hi); return list.Count-1;
            }
            int BandLane(int x1,int x2){
                int lo=Math.Min(x1,x2)-8,hi=Math.Max(x1,x2)+8;
                for(int L=0;L<bandEnd.Count;L++)if(bandEnd[L]<lo){bandEnd[L]=hi;return L;}
                bandEnd.Add(hi); return bandEnd.Count-1;
            }
            var route=new List<System.Drawing.Point[]>();
            for(int k=0;k<jm.Count;k++){
                var(f,t,_)=jm[k];
                int cf=bCol[BlockOf(bMem,f)], ct=bCol[BlockOf(bMem,t)];
                var sp=srcPt[k]; var dp=dstPt[k];
                if(cf==ct||ct-1==cf){                      // 같은 칸이거나 바로 오른쪽 칸 → 통로 하나로 끝
                    int lx=chX(cf,Lane(cf,sp.Y,dp.Y));
                    route.Add(new[]{sp,new System.Drawing.Point(lx,sp.Y),new System.Drawing.Point(lx,dp.Y),dp});
                }else{                                     // 그 외 → 위쪽 가로 통로를 거쳐 간다
                    int lx1=chX(cf,Lane(cf,sp.Y,-1));
                    int lx2=chX(ct-1,Lane(ct-1,-1,dp.Y));
                    int by=-20-BandLane(lx1,lx2)*BANDH;
                    route.Add(new[]{sp,new System.Drawing.Point(lx1,sp.Y),new System.Drawing.Point(lx1,by),
                                       new System.Drawing.Point(lx2,by),new System.Drawing.Point(lx2,dp.Y),dp});
                }
            }

            // ── 6) 위쪽 통로만큼 전체를 아래로 밀기 ────────────
            int top=20+Math.Max(bandEnd.Count,0)*BANDH+14;
            void Shift(){
                foreach(var key in new List<int>(cardRc.Keys)){var r=cardRc[key];r.Y+=top;cardRc[key]=r;}
                for(int k=0;k<nb;k++){var r=blockRc[k];r.Y+=top;blockRc[k]=r;}
                for(int k=0;k<grpRc.Count;k++){var(r,g)=grpRc[k];r.Y+=top;grpRc[k]=(r,g);}
                for(int k=0;k<route.Count;k++)for(int j=0;j<route[k].Length;j++)route[k][j]=new System.Drawing.Point(route[k][j].X,route[k][j].Y+top);
                for(int k=0;k<srcPt.Length;k++){srcPt[k]=new System.Drawing.Point(srcPt[k].X,srcPt[k].Y+top);dstPt[k]=new System.Drawing.Point(dstPt[k].X,dstPt[k].Y+top);}
            }
            Shift();

            int canvasW=colX(cols-1)+CW+COLGAP,canvasH=0;
            for(int k=0;k<nb;k++)canvasH=Math.Max(canvasH,blockRc[k].Bottom);
            canvasH+=24;

            // ── 7) 창 ─────────────────────────────────────────
            var frm=new Form{
                Text=$"실행 흐름 — {_current.Name}  (스텝 {n}개)",
                StartPosition=FormStartPosition.CenterParent,
                BackColor=Color.FromArgb(248,248,252),
                MinimumSize=new System.Drawing.Size(640,400)
            };
            frm.ClientSize=new System.Drawing.Size(
                Math.Max(Math.Min(canvasW+24,wa.Width*9/10),640),
                Math.Max(Math.Min(canvasH+30+24,wa.Height*9/10),420));

            var legend=new Panel{Dock=DockStyle.Top,Height=30,BackColor=Color.FromArgb(240,240,246)};
            legend.Paint+=(s2,e)=>{
                var g=e.Graphics;
                g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using(var ln=new Pen(Color.FromArgb(215,215,225)))g.DrawLine(ln,0,legend.Height-1,legend.Width,legend.Height-1);
                using(var pn=new Pen(cSeq,1.8f))g.DrawLine(pn,12,15,34,15);
                using(var pn=new Pen(cOut,2.2f))g.DrawLine(pn,150,15,166,15);
                Dot(g,cOut,150,15); Head(g,cOut,172,15);
                using(var pn=new Pen(cWatch,1.8f){DashStyle=System.Drawing.Drawing2D.DashStyle.Dash})g.DrawLine(pn,330,15,352,15);
                using(var pn=new Pen(cTog,1.8f){DashStyle=System.Drawing.Drawing2D.DashStyle.Dot})g.DrawLine(pn,470,15,492,15);
            };
            void Leg(string t,int x,Color c){legend.Controls.Add(new Label{Text=t,Location=new System.Drawing.Point(x,8),AutoSize=true,Font=new Font("맑은 고딕",8),ForeColor=c,BackColor=Color.Transparent});}
            Leg("순서대로 실행",40,Color.FromArgb(110,110,125));
            Leg("● 나감 → 들어옴 ▶  (끝나면 이동)",182,Color.FromArgb(180,70,0));
            Leg("끝난 뒤 지켜보기",358,Color.FromArgb(0,125,145));
            Leg("스텝 켜고 끄기",498,Color.FromArgb(120,35,155));

            var scroll=new Panel{Dock=DockStyle.Fill,AutoScroll=true,BackColor=Color.FromArgb(248,248,252)};
            var canvas=new Panel{Location=System.Drawing.Point.Empty,Size=new System.Drawing.Size(canvasW,canvasH),BackColor=Color.FromArgb(248,248,252)};
            scroll.Controls.Add(canvas);
            frm.Controls.Add(scroll); frm.Controls.Add(legend);

            foreach(var kv in cardRc){
                var card=MakeFlowCard(kv.Key,steps[kv.Key],CW,CH);
                card.Location=kv.Value.Location;
                canvas.Controls.Add(card);
            }

            canvas.Paint+=(s2,e)=>{
                var g=e.Graphics;
                g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                // 묶음 그룹 상자
                foreach(var(box,gid)in grpRc){
                    using var bg=new SolidBrush(Color.FromArgb(255,250,238));
                    g.FillRectangle(bg,box);
                    using var pen=new Pen(Color.FromArgb(205,165,70),1){DashStyle=System.Drawing.Drawing2D.DashStyle.Dash};
                    g.DrawRectangle(pen,box);
                    using var br=new SolidBrush(Color.FromArgb(150,95,0));
                    using var ft=new Font("맑은 고딕",8,FontStyle.Bold);
                    g.DrawString($"묶음 {gid} — 이 중 먼저 뜨는 하나만 클릭",ft,br,box.X+8,box.Y+3);
                }

                // 순서대로 실행: 같은 칸 안은 아래로, 칸이 바뀌면 다음 칸 위로
                using(var seq=new Pen(cSeq,1.8f))
                using(var sf=new Font("맑은 고딕",8,FontStyle.Bold))
                using(var sb=new SolidBrush(Color.FromArgb(120,120,138))){
                    for(int k=0;k+1<nb;k++){
                        var a2=blockRc[k]; var b2=blockRc[k+1];
                        if(bCol[k]==bCol[k+1]){
                            int x=colX(bCol[k])+CW/2;
                            g.DrawLine(seq,x,a2.Bottom,x,b2.Top-8);
                            Head(g,cSeq,x,b2.Top,1);
                        }else{
                            g.DrawString("▼ 다음 칸으로",sf,sb,a2.X+2,a2.Bottom+6);
                            g.DrawString("▲ 이어서",sf,sb,b2.X+2,b2.Top-20);
                        }
                    }
                }

                // 건너뛰는 흐름
                for(int k=0;k<jm.Count;k++){
                    var(f,t,kind)=jm[k];
                    Color c=kind==1?cOut:kind==2?cWatch:cTog;
                    using var pen=new Pen(c,kind==1?2.2f:1.8f);
                    if(kind==2)pen.DashStyle=System.Drawing.Drawing2D.DashStyle.Dash;
                    if(kind==3)pen.DashStyle=System.Drawing.Drawing2D.DashStyle.Dot;
                    var pts=route[k];
                    for(int j=0;j+1<pts.Length;j++)g.DrawLine(pen,pts[j],pts[j+1]);
                    // 나가는 곳 ●, 들어오는 곳 ▶
                    Dot(g,c,srcPt[k].X,srcPt[k].Y);
                    if(dstFromLeft[k]){
                        Head(g,c,dstPt[k].X,dstPt[k].Y,0);                       // 오른쪽을 향해 카드로
                        NumTag(g,c,dstPt[k].X-11,dstPt[k].Y,$"{f+1}→",true);
                    }else{
                        Head(g,c,dstPt[k].X,dstPt[k].Y,2);                       // 왼쪽을 향해 카드로
                        NumTag(g,c,dstPt[k].X+11,dstPt[k].Y,$"←{f+1}",false);
                    }
                    NumTag(g,c,srcPt[k].X+7,srcPt[k].Y,$"→{t+1}",false);
                }
            };

            frm.ShowDialog(this);
        }

        static int BlockOf(List<List<int>> blocks,int stepIdx)
        {
            for(int k=0;k<blocks.Count;k++)if(blocks[k].Contains(stepIdx))return k;
            return 0;
        }

        // 선이 나가는 자리 표시 (꽉 찬 동그라미)
        static void Dot(Graphics g,Color c,int x,int y)
        {
            using var br=new SolidBrush(c);
            using var wp=new Pen(Color.White,1.5f);
            g.FillEllipse(br,x-4,y-4,8,8);
            g.DrawEllipse(wp,x-4,y-4,8,8);
        }

        // 선이 들어오는 자리 표시 (삼각 화살촉). dir 0=오른쪽, 1=아래, 2=왼쪽
        static void Head(Graphics g,Color c,int x,int y,int dir=0)
        {
            using var br=new SolidBrush(c);
            if(dir==1)     g.FillPolygon(br,new[]{new System.Drawing.Point(x,y),new System.Drawing.Point(x-5,y-8),new System.Drawing.Point(x+5,y-8)});
            else if(dir==2)g.FillPolygon(br,new[]{new System.Drawing.Point(x,y),new System.Drawing.Point(x+9,y-5),new System.Drawing.Point(x+9,y+5)});
            else           g.FillPolygon(br,new[]{new System.Drawing.Point(x,y),new System.Drawing.Point(x-9,y-5),new System.Drawing.Point(x-9,y+5)});
        }

        // 선 끝에 붙는 작은 번호표. rightAlign=true 면 x 를 오른쪽 끝으로 본다.
        static void NumTag(Graphics g,Color c,int x,int y,string text,bool rightAlign)
        {
            using var ft=new Font("맑은 고딕",7.5f,FontStyle.Bold);
            var sz=g.MeasureString(text,ft);
            int w=(int)sz.Width+6,h=15;
            int bx=rightAlign?x-w:x;
            var rc=new Rectangle(bx,y-h/2,w,h);
            using var bg=new SolidBrush(c);
            using var fg=new SolidBrush(Color.White);
            g.FillRectangle(bg,rc);
            g.DrawString(text,ft,fg,rc.X+3,rc.Y+1);
        }

        Panel MakeFlowCard(int idx,MacroStep step,int w,int h)
        {
            var card=new Panel{Size=new System.Drawing.Size(w,h),BackColor=GetCardBg(step.Type),Cursor=Cursors.Hand};
            card.Controls.Add(new Panel{Location=new System.Drawing.Point(0,0),Size=new System.Drawing.Size(4,h),BackColor=GetTypeColor(step.Type)});
            string num=step.Type==StepType.Simultaneous?$"G{step.GroupId}":$"{idx+1}";
            card.Controls.Add(new Label{Text=$"{num}. {GetTypeName(step.Type)}",Location=new System.Drawing.Point(10,6),AutoSize=true,Font=new Font("맑은 고딕",8.5f,FontStyle.Bold),ForeColor=GetTypeColor(step.Type),BackColor=Color.Transparent});
            card.Controls.Add(new Label{Text=GetCardContent(step),Location=new System.Drawing.Point(10,28),Size=new System.Drawing.Size(w-62,20),Font=new Font("맑은 고딕",8),ForeColor=step.Enabled?Color.FromArgb(60,60,60):Color.Silver,BackColor=Color.Transparent});
            if(!step.Enabled)card.Controls.Add(new Label{Text="OFF",Location=new System.Drawing.Point(w-38,6),AutoSize=true,Font=new Font("맑은 고딕",7,FontStyle.Bold),ForeColor=Color.FromArgb(170,170,170),BackColor=Color.Transparent});
            card.Paint+=(s,e)=>{using var pen=new Pen(GetTypeColor(step.Type));e.Graphics.DrawRectangle(pen,0,0,card.Width-1,card.Height-1);};
            EventHandler click=(s,e)=>{SelectStep(idx);};
            card.Click+=click;foreach(Control c in card.Controls)c.Click+=click;
            return card;
        }

        static void DrawFlowArrow(Graphics g,Color color,int x,int y,bool down)
        {
            using var brush=new SolidBrush(color);
            if(down)g.FillPolygon(brush,new[]{new System.Drawing.Point(x,y),new System.Drawing.Point(x-5,y-8),new System.Drawing.Point(x+5,y-8)});
            else g.FillPolygon(brush,new[]{new System.Drawing.Point(x,y),new System.Drawing.Point(x-8,y-5),new System.Drawing.Point(x-8,y+5)});
        }

        void ShowNotification(string title,string msg)
        {
            Invoke(()=>{
                var frm=new Form{Text=title,FormBorderStyle=System.Windows.Forms.FormBorderStyle.FixedToolWindow,StartPosition=FormStartPosition.Manual,Size=new System.Drawing.Size(300,80),TopMost=true,ShowInTaskbar=false,BackColor=Color.FromArgb(40,40,48)};
                var screen=Screen.FromControl(this).WorkingArea;
                frm.Location=new System.Drawing.Point(screen.Right-310,screen.Bottom-90);
                frm.Controls.Add(new Label{Text=msg,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter,Font=new Font("맑은 고딕",10),ForeColor=Color.White});
                frm.Show();
                var timer=new System.Windows.Forms.Timer{Interval=3000};
                timer.Tick+=(s,e)=>{timer.Stop();frm.Close();frm.Dispose();timer.Dispose();};
                timer.Start();
            });
        }

        // ══════════════════════════════════════════════════════
        //  유틸
        // ══════════════════════════════════════════════════════
        Dictionary<string,Bitmap> LoadBitmaps(MacroItem m){var d=new Dictionary<string,Bitmap>();foreach(var st in m.Steps)if(!string.IsNullOrEmpty(st.ImagePath)&&!d.ContainsKey(st.ImagePath))try{d[st.ImagePath]=new Bitmap(st.ImagePath);}catch{}return d;}
        void DisposeBitmaps(Dictionary<string,Bitmap> d){foreach(var b in d.Values)b.Dispose();}
        // 모든 모니터를 합친 가상 화면 영역. 보조 모니터가 주 모니터 왼쪽/위에 있으면 X/Y가 음수가 된다.
        static Rectangle VirtualBounds=>SystemInformation.VirtualScreen;

        // 좌표가 몇 번 모니터에 속하는지 (1부터 시작, 주 모니터는 뒤에 "(주)" 표시)
        static string MonitorNameOf(System.Drawing.Point p){
            var all=Screen.AllScreens;
            for(int i=0;i<all.Length;i++)if(all[i].Bounds.Contains(p))return $"모니터{i+1}{(all[i].Primary?"(주)":"")}";
            return "화면밖";}

        // 이미지를 찾을 범위. 0=전체(모든 모니터), 1..N=N번 모니터만.
        // 세로로 돌려 쓰는 모니터는 Windows 가 이미 1080x1920 처럼 회전된 크기로 알려주므로 그대로 쓰면 된다.
        Rectangle SearchBounds(){
            var all=Screen.AllScreens; int mi=_searchMonitor;
            if(mi>=1&&mi<=all.Length)return all[mi-1].Bounds;
            return VirtualBounds;}

        // 지정된 범위를 한 장으로 캡처한다. origin = 캡처 이미지의 (0,0)에 해당하는 실제 화면 좌표.
        Bitmap CaptureScreen(out System.Drawing.Point origin){
            var b=SearchBounds(); origin=b.Location;
            var ss=new Bitmap(b.Width,b.Height);
            using(var g=Graphics.FromImage(ss))g.CopyFromScreen(b.Location,System.Drawing.Point.Empty,b.Size);
            return ss;}

        // 두 이미지 모두 가우시안 블러 적용 → DWM/ClearType 서브픽셀 노이즈 제거
        static void BlurForMatch(Mat src,Mat dst){Cv2.GaussianBlur(src,dst,new OpenCvSharp.Size(3,3),0);}

        System.Drawing.Point? MatchTpl(Mat sg,Bitmap tmpl,double thr,System.Drawing.Point org){
            using Mat t=BitmapToMat(tmpl);using Mat tg=new Mat();Cv2.CvtColor(t,tg,ColorConversionCodes.BGRA2GRAY);
            using Mat tgb=new Mat();BlurForMatch(tg,tgb);
            using Mat sgb=new Mat();BlurForMatch(sg,sgb);
            using Mat r=new Mat();Cv2.MatchTemplate(sgb,tgb,r,TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(r,out _,out double v,out _,out OpenCvSharp.Point loc);
            if(v>=thr)return new System.Drawing.Point(org.X+loc.X+t.Width/2,org.Y+loc.Y+t.Height/2);return null;}

        // 스코어 + 매칭 영역 반환 (테스트용)
        System.Drawing.Point? MatchTplWithScore(Mat sg,Bitmap tmpl,System.Drawing.Point org,out double score,out Rectangle matchRect){
            score=0;matchRect=Rectangle.Empty;
            using Mat t=BitmapToMat(tmpl);using Mat tg=new Mat();Cv2.CvtColor(t,tg,ColorConversionCodes.BGRA2GRAY);
            using Mat tgb=new Mat();BlurForMatch(tg,tgb);
            using Mat sgb=new Mat();BlurForMatch(sg,sgb);
            using Mat r=new Mat();Cv2.MatchTemplate(sgb,tgb,r,TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(r,out _,out score,out _,out OpenCvSharp.Point loc);
            matchRect=new Rectangle(org.X+loc.X,org.Y+loc.Y,t.Width,t.Height);
            double thr=_selStep!=null?_selStep.Confidence/100.0:0.8;
            if(score>=thr)return new System.Drawing.Point(org.X+loc.X+t.Width/2,org.Y+loc.Y+t.Height/2);return null;}

        void DoClick(int x,int y,MacroStep step){for(int ci=0;ci<step.ClickCount&&_isRunning;ci++){ClickAt(x,y,step.RightClick);if(ci<step.ClickCount-1)Thread.Sleep(step.ClickDelay);}}
        static unsafe Mat BitmapToMat(Bitmap bmp){var rect=new Rectangle(0,0,bmp.Width,bmp.Height);BitmapData bd=bmp.LockBits(rect,ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);try{Mat mat=new Mat(bmp.Height,bmp.Width,MatType.CV_8UC4);int stride=Math.Abs(bd.Stride);long step=mat.Step();byte* src=(byte*)bd.Scan0;byte* dst=(byte*)mat.Data;for(int y=0;y<bmp.Height;y++)Buffer.MemoryCopy(src+y*stride,dst+y*step,step,step);return mat;}finally{bmp.UnlockBits(bd);}}
        void ClickAt(int x,int y,bool right){
            if(_clickMode==ClickMode.Adb){AdbClickAt(x,y,right);return;}
            if(_clickMode==ClickMode.Background){BgClickAt(x,y,right);return;}
            SetCursorPos(x,y);Thread.Sleep(50);mouse_event(right?RDOWN:LDOWN,x,y,0,IntPtr.Zero);Thread.Sleep(50);mouse_event(right?RUP:LUP,x,y,0,IntPtr.Zero);}
        // ══════════════════════════════════════════════════════
        //  모니터 / ADB
        // ══════════════════════════════════════════════════════
        void FillMonitorCombo()
        {
            var keep=cmbMonitor.SelectedIndex;
            cmbMonitor.Items.Clear();
            var vb=VirtualBounds;
            cmbMonitor.Items.Add($"전체 화면 ({vb.Width}x{vb.Height})");
            var all=Screen.AllScreens;
            for(int i=0;i<all.Length;i++){
                var b=all[i].Bounds;
                string ori=b.Height>b.Width?"세로":"가로";
                cmbMonitor.Items.Add($"모니터{i+1}{(all[i].Primary?"(주)":"")} {b.Width}x{b.Height} {ori}");
            }
            cmbMonitor.SelectedIndex=Math.Clamp(keep<0?0:keep,0,cmbMonitor.Items.Count-1);
        }

        // 블루스택 등에 자동으로 붙는다. 실패하면 왜 안 됐는지 알려준다.
        // showUi=false 면 조용히 시도만 한다.
        bool TryAutoConnectAdb(MacroItem m,bool showUi)
        {
            void Fail(string msg){
                SetStatus("[ADB] "+msg.Replace("\n"," "));
                if(showUi)MessageBox.Show(msg,"ADB 연결 실패",MessageBoxButtons.OK,MessageBoxIcon.Warning);
            }

            // 1) adb 실행 파일 — 지정된 경로가 실제로 없으면 자동 탐색으로 되돌린다
            string adb=m.AdbPath.Trim();
            if(adb.Length>0&&!System.IO.File.Exists(adb)){
                SetStatus("[ADB] 지정된 adb 경로가 없어 자동 탐색합니다: "+adb);
                adb="";
            }
            if(adb.Length==0)adb=Adb.AutoDetectAdbPath();
            if(adb.Length==0){
                Fail("adb 실행 파일을 찾지 못했습니다.\n\n블루스택이 설치되어 있으면 보통 자동으로 찾습니다.\n[ADB 설정]에서 경로를 직접 지정해주세요.");
                return false;
            }
            m.AdbPath=adb;

            // 2) 이미 붙어 있는 기기가 지정돼 있으면 그대로 사용
            var devs=Adb.Devices(adb);
            string serial=m.AdbSerial.Trim();
            if(serial.Length>0&&devs.Contains(serial))return FinishAdbConnect(m,adb,serial,showUi);

            // 3) 에뮬레이터가 켜져 있는지
            if(!Adb.IsEmulatorRunning(out _)&&devs.Count==0){
                Fail("블루스택이 실행되고 있지 않습니다.\n\n블루스택을 먼저 켠 다음 다시 [ADB(에뮬)]를 선택해주세요.");
                return false;
            }

            // 4) 블루스택 인스턴스 포트로 연결 시도
            var insts=Adb.BlueStacksInstances();
            var tried=new List<string>();
            foreach(var(name,port)in insts){
                string hp="127.0.0.1:"+port;
                tried.Add($"{name} ({hp})");
                Adb.Connect(adb,hp);
            }
            if(serial.Length>0&&!devs.Contains(serial))Adb.Connect(adb,serial);   // 직접 적어둔 주소도 시도

            devs=Adb.Devices(adb);
            if(devs.Count==0){
                string detail=insts.Count>0?"\n\n시도한 주소:\n  "+string.Join("\n  ",tried)
                                           :"\n\n블루스택 설정 파일(bluestacks.conf)을 찾지 못해 포트를 모릅니다.";
                Fail("블루스택에 연결하지 못했습니다."+detail+
                     "\n\n블루스택 [설정] → [고급] 에서\n\"Android 디버그 브리지(ADB)\" 를 켠 뒤 다시 시도해주세요.");
                return false;
            }

            // 지정된 게 없으면 블루스택 주소를 우선 고른다
            if(serial.Length==0||!devs.Contains(serial)){
                serial=devs.Find(d=>d.StartsWith("127.0.0.1:"))??devs[0];
            }
            return FinishAdbConnect(m,adb,serial,showUi);
        }

        // 연결된 기기로 해상도와 렌더 영역까지 확인한다.
        bool FinishAdbConnect(MacroItem m,string adb,string serial,bool showUi)
        {
            m.AdbSerial=serial;
            if(!Adb.DisplaySize(adb,serial,out int dw,out int dh,out bool rot)){
                string msg="기기에는 붙었지만 안드로이드 화면 크기를 읽지 못했습니다. ("+serial+")";
                SetStatus("[ADB] "+msg);
                if(showUi)MessageBox.Show(msg,"ADB 연결",MessageBoxButtons.OK,MessageBoxIcon.Warning);
                return false;
            }
            _adbPath=adb; _adbSerial=serial; _adbW=dw; _adbH=dh; _adbRotKnown=rot;
            _adbTitle=m.AdbWindowTitle.Length>0?m.AdbWindowTitle:"BlueStacks";
            _adbManual=m.AdbManualArea; _adbLongMs=m.AdbLongPressMs;
            _adbManualRect=new Rectangle(m.AdbAreaX,m.AdbAreaY,m.AdbAreaW,m.AdbAreaH);
            _adbArea=Rectangle.Empty; _adbAreaAt=0;

            var area=ResolveAdbArea();
            if(area.Width<=0||area.Height<=0){
                string msg="기기에는 연결됐지만 에뮬레이터 창을 찾지 못했습니다.\n\n"+
                           "블루스택 창이 최소화되어 있지 않은지 확인해주세요.\n"+
                           "([ADB 설정] → [영역 확인]에서 직접 지정할 수도 있습니다.)";
                SetStatus("[ADB] 에뮬레이터 창을 찾지 못했습니다.");
                if(showUi)MessageBox.Show(msg,"ADB 연결",MessageBoxButtons.OK,MessageBoxIcon.Warning);
                return false;
            }

            // 성공하면 알림창 없이 제목표시줄에만 적는다 (실패했을 때만 알린다)
            SetStatus($"[ADB] 연결됨: {serial}  ·  안드로이드 {dw}x{dh}  ·  창 {area.Width}x{area.Height} ({MonitorNameOf(new System.Drawing.Point(area.X+area.Width/2,area.Y+area.Height/2))})");
            return true;
        }

        // 실행 시작 전에 adb 경로 / 기기 / 안드로이드 해상도 / 렌더 영역을 확정한다.
        bool PrepareAdb(MacroItem m)=>TryAutoConnectAdb(m,true);

        // 렌더 영역. 창은 움직일 수 있으니 자동 인식 모드에서는 1초마다 다시 찾는다.
        Rectangle ResolveAdbArea()
        {
            if(_adbManual)return _adbManualRect;
            long now=Environment.TickCount64;
            if(_adbArea.Width>0&&now-_adbAreaAt<1000)return _adbArea;
            _adbArea=Adb.FindEmulatorArea(_adbTitle,out _); _adbAreaAt=now;
            return _adbArea;
        }

        // 화면 좌표를 안드로이드 좌표로 바꿔 adb 로 탭을 보낸다.
        void AdbClickAt(int x,int y,bool right)
        {
            var area=ResolveAdbArea();
            if(!Adb.MapToDevice(area,_adbW,_adbH,_adbRotKnown,x,y,out int ax,out int ay)){
                SetStatus($"[ADB] ({x},{y})는 에뮬레이터 화면 밖이라 건너뜁니다.");return;}
            string r=right?Adb.LongPress(_adbPath,_adbSerial,ax,ay,_adbLongMs)
                          :Adb.Tap(_adbPath,_adbSerial,ax,ay);
            if(r.StartsWith("[오류]"))SetStatus("[ADB] "+r);
        }

        void BgClickAt(int x,int y,bool right){
            var pt=new POINT{X=x,Y=y};
            IntPtr hwnd=WindowFromPoint(pt);if(hwnd==IntPtr.Zero)return;
            ScreenToClient(hwnd,ref pt);
            IntPtr lp=(IntPtr)(((pt.Y&0xFFFF)<<16)|(pt.X&0xFFFF));
            IntPtr wp=(IntPtr)(right?2:1);
            PostMessage(hwnd,right?WM_RBUTTONDOWN:WM_LBUTTONDOWN,wp,lp);
            Thread.Sleep(50);
            PostMessage(hwnd,right?WM_RBUTTONUP:WM_LBUTTONUP,IntPtr.Zero,lp);
        }

        // ══════════════════════════════════════════════════════
        //  헬퍼
        // ══════════════════════════════════════════════════════
        void DkBtn(Panel p,Button b,string t,int y,Color c,EventHandler h){b.Text=t;b.Location=new System.Drawing.Point(8,y);b.Size=new System.Drawing.Size(174,26);b.BackColor=c;b.ForeColor=Color.White;b.FlatStyle=FlatStyle.Flat;b.Click+=h;p.Controls.Add(b);}
        void MdBtn(Panel p,Button b,string t,int x,int y,Color c,EventHandler h){b.Text=t;b.Location=new System.Drawing.Point(x,y);b.Size=new System.Drawing.Size(66,26);b.BackColor=c;b.ForeColor=Color.White;b.FlatStyle=FlatStyle.Flat;b.Click+=h;p.Controls.Add(b);}
        Label MkL(string t,int x,int y)=>new Label{Text=t,Location=new System.Drawing.Point(x,y),AutoSize=true};
        // 섹션 제목 + 가로줄. 제목 길이에 맞춰 줄이 알아서 이어진다.
        Control MkSL(string t,int x,int y,int w=505)
        {
            string title=t.Trim(' ','─');
            var box=new Panel{Location=new System.Drawing.Point(x,y),Size=new System.Drawing.Size(w,16),BackColor=Color.Transparent};
            var lbl=new Label{Text=title,Location=new System.Drawing.Point(0,0),AutoSize=true,
                              ForeColor=Color.FromArgb(90,90,110),Font=new Font("맑은 고딕",8.5f,FontStyle.Bold)};
            box.Controls.Add(lbl);
            box.Paint+=(s,e)=>{
                int lx=lbl.Width+8;
                if(lx<box.Width-2){using var pen=new Pen(Color.FromArgb(205,205,218));e.Graphics.DrawLine(pen,lx,8,box.Width-2,8);}
            };
            return box;
        }

        void BeginHkCapture(int t){_settingHkTarget=t;_settingHk=true;SetStatus($"[{(t==1?"실행":"정지")}] 단축키를 눌러주세요...");var lbl=t==1?lblStartHk:lblStopHk;lbl.Text="입력 대기...";lbl.BackColor=Color.LightYellow;}
        void Form1_KeyDown(object? s,KeyEventArgs e){if(!_settingHk)return;if(e.KeyCode==Keys.ControlKey||e.KeyCode==Keys.ShiftKey||e.KeyCode==Keys.Menu||e.KeyCode==Keys.None)return;uint mod=(e.Control?MOD_CTRL:0)|(e.Alt?MOD_ALT:0)|(e.Shift?MOD_SHIFT:0);string ks=(e.Control?"Ctrl+":"")+(e.Alt?"Alt+":"")+(e.Shift?"Shift+":"")+e.KeyCode;if(_current!=null){if(_settingHkTarget==1){_current.StartHotkey=ks;_current.StartMod=mod;_current.StartVk=(uint)e.KeyCode;lblStartHk.Text=ks;lblStartHk.BackColor=Color.White;}else{_current.StopHotkey=ks;_current.StopMod=mod;_current.StopVk=(uint)e.KeyCode;lblStopHk.Text=ks;lblStopHk.BackColor=Color.White;}ApplyHk(_current);}_settingHk=false;SetStatus($"단축키: {ks}");e.Handled=true;e.SuppressKeyPress=true;}
        void ApplyHk(MacroItem m){UnregisterHotKey(Handle,HK_START);UnregisterHotKey(Handle,HK_STOP);RegisterHotKey(Handle,HK_START,m.StartMod,m.StartVk);RegisterHotKey(Handle,HK_STOP,m.StopMod,m.StopVk);}
        protected override void WndProc(ref Message m){if(m.Msg==WM_HOTKEY){if(m.WParam.ToInt32()==HK_START&&!_isRunning&&_current?.Steps.Count>0)OnRun(null,EventArgs.Empty);else if(m.WParam.ToInt32()==HK_STOP&&_isRunning)OnStopMacro(null,EventArgs.Empty);}base.WndProc(ref m);}
        protected override void OnFormClosed(FormClosedEventArgs e){
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged-=OnDisplaySettingsChanged;
            UnregisterHotKey(Handle,HK_START);UnregisterHotKey(Handle,HK_STOP);base.OnFormClosed(e);}

        void OnDisplaySettingsChanged(object? sender,EventArgs e)
        {
            if(InvokeRequired){BeginInvoke(new Action(()=>OnDisplaySettingsChanged(sender,e)));return;}
            int wanted=_current?.SearchMonitor??_searchMonitor;
            _suppressStepEvt=true;
            FillMonitorCombo();
            // 고르고 있던 모니터가 없어졌으면 '전체 화면'으로 되돌린다
            int pick=(wanted>=1&&wanted<=Screen.AllScreens.Length)?wanted:0;
            cmbMonitor.SelectedIndex=pick;
            if(_current!=null)_current.SearchMonitor=pick;
            _searchMonitor=pick;
            _suppressStepEvt=false;
            var vb=VirtualBounds;
            SetStatus(wanted!=pick
                ? $"모니터 구성이 바뀌었습니다 — 고른 모니터가 없어져 '전체 화면'으로 되돌렸습니다. (모니터 {Screen.AllScreens.Length}개, {vb.Width}x{vb.Height})"
                : $"모니터 구성이 바뀌어 검색 범위를 다시 읽었습니다. (모니터 {Screen.AllScreens.Length}개, {vb.Width}x{vb.Height})");
        }
        static void ParseHk(string hk,out uint mod,out uint vk){mod=MOD_NONE;vk=(uint)Keys.F5;if(string.IsNullOrEmpty(hk))return;if(hk.Contains("Ctrl"))mod|=MOD_CTRL;if(hk.Contains("Alt"))mod|=MOD_ALT;if(hk.Contains("Shift"))mod|=MOD_SHIFT;var parts=hk.Split('+');if(parts.Length>0&&Enum.TryParse<Keys>(parts[^1],out var k))vk=(uint)k;}
        void SetStatus(string msg){void u(){Text=string.IsNullOrEmpty(msg)?"이미지 자동화 매크로":$"이미지 자동화 매크로 — {msg}";}if(InvokeRequired)BeginInvoke(u);else u();}
    }
}
