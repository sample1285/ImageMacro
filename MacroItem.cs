using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Windows.Forms;

namespace ImageMacro
{
    // 스텝이 하는 일.
    // (저장 파일에는 숫자로 들어가므로 순서를 바꾸면 안 된다)
    public enum StepType
    {
        // 이미지 찾아 클릭 — 그 이미지가 화면에 뜰 때까지 기다렸다가 찾으면 클릭한다.
        Sequential,

        // 먼저 뜨는 것 클릭 — 같은 묶음 번호를 가진 스텝들을 한꺼번에 감시하다가,
        // 그중 가장 먼저 보이는 하나만 클릭하고 나머지는 건너뛴다.
        Simultaneous,

        KeyInput,      // 키보드 입력 (텍스트 타이핑 / 단축키)
        MouseMove,     // 마우스 이동·클릭 (좌표 지정)
        Delay,         // 시간 대기
        Notification,  // 알림 띄우기

        // 스텝 켜고 끄기 — 지정한 이미지가 화면에 보이면 다른 스텝들을 켜거나 끈다.
        // 기다리지 않고 그 자리에서 한 번만 확인하고 지나간다.
        // 켜고 끈 상태는 매크로가 도는 동안 유지되고, 저장된 매크로는 건드리지 않는다.
        ToggleSteps
    }

    // 스텝 켜고 끄기에서 무엇을 할지
    public enum ToggleAction
    {
        Off,     // 끄기
        On,      // 켜기
        Flip     // 반대로 (켜져 있으면 끄고, 꺼져 있으면 켜기)
    }

    public enum TimeoutAction
    {
        Stop,
        Restart
    }

    public enum MoveAction
    {
        MoveOnly,
        LeftClick,
        RightClick
    }

    // 클릭을 어떤 방식으로 보낼지
    public enum ClickMode
    {
        Normal,      // 실제 마우스 커서를 옮겨서 클릭
        Background,  // 창에 메시지만 보냄 (커서 안 움직임)
        Adb          // adb 로 안드로이드 에뮬레이터에 탭 전송 (블루스택 등)
    }

    public class MacroStep
    {
        public string   ImagePath        { get; set; } = "";
        public StepType Type             { get; set; } = StepType.Sequential;

        // 같이 볼 묶음 번호 (Simultaneous 일 때만 사용. 같은 번호끼리 한꺼번에 감시한다)
        public int      GroupId          { get; set; } = 1;

        public bool     RightClick       { get; set; } = false;
        public int      ClickCount       { get; set; } = 1;
        public int      ClickDelay       { get; set; } = 100;
        public int      WaitAfter        { get; set; } = 500;
        public int      Confidence       { get; set; } = 80;
        public bool     Enabled          { get; set; } = true;
        public int      Timeout          { get; set; } = 0;
        public int      ClickOffsetX     { get; set; } = 0;
        public int      ClickOffsetY     { get; set; } = 0;
        public bool     UseCustomOffset  { get; set; } = false;

        // 키보드 입력 스텝용
        public string   KeyText          { get; set; } = "";   // 텍스트 입력
        public string   HotKey           { get; set; } = "";   // 단축키 (예: "Ctrl+C")

        // 마우스 이동 스텝용
        public int        MoveX          { get; set; } = 0;
        public int        MoveY          { get; set; } = 0;
        public bool       MoveRelative   { get; set; } = false;
        public MoveAction MoveAction     { get; set; } = MoveAction.MoveOnly;

        // 대기 스텝용
        // 입력한 숫자와 단위를 따로 둔다. DelayUnit=0(밀리초)이면 예전 파일과 값이 그대로 맞는다.
        public int        DelayMs        { get; set; } = 1000;   // 입력한 숫자
        public int        DelayUnit      { get; set; } = 0;      // 0=밀리초, 1=초, 2=분

        // 알림 스텝용
        public string     NotificationText { get; set; } = "";

        // 이 스텝이 끝나면 갈 스텝 번호 (1부터, 0 = 바로 아래 스텝)
        public int        JumpOnSuccess  { get; set; } = 0;

        // 끝난 뒤 지켜볼 스텝 번호들 (쉼표 구분, 1부터. 예: "2,3,4")
        // 이 스텝이 끝나면 해당 스텝들의 이미지를 함께 지켜보다가 먼저 뜨는 쪽으로 이동한다.
        public string     WatchTargets   { get; set; } = "";

        // 스텝 켜고 끄기 스텝용 — 대상 스텝 번호들 (쉼표 구분, 1부터. 예: "7,8,9")
        public string       ToggleTargets { get; set; } = "";
        public ToggleAction ToggleAction  { get; set; } = ToggleAction.Off;

        // 대기 스텝이 실제로 쉬는 시간(밀리초)
        [JsonIgnore]
        public int DelayEffectiveMs => DelayMs * MacroItem.UnitFactor(DelayUnit);
    }

    public class MacroItem
    {
        public string          Name          { get; set; } = "새 매크로";
        public List<MacroStep> Steps         { get; set; } = new();
        public int             ScanInterval  { get; set; } = 300;
        public int             RepeatCount   { get; set; } = 0;
        public string          StartHotkey     { get; set; } = "F5";
        public string          StopHotkey      { get; set; } = "F6";
        public TimeoutAction   OnTimeout       { get; set; } = TimeoutAction.Stop;
        public int             RestartDelay    { get; set; } = 1000;
        public bool            BackgroundClick { get; set; } = false;  // 구버전 파일 호환용 (Load 시 ClickMode 로 변환)
        // 항상 감시 모드: 순서대로 실행하지 않고 '먼저 뜨는 것 클릭' 스텝들만 계속 지켜본다.
        public bool            EventMode       { get; set; } = false;

        // 이미지를 찾을 화면 범위. 0 = 전체(모든 모니터), 1..N = N번 모니터만
        public int             SearchMonitor   { get; set; } = 0;

        // 한 번 다 돌고 다음 반복까지 쉬는 시간
        public int             LoopDelay       { get; set; } = 0;   // 입력한 숫자
        public int             LoopDelayUnit   { get; set; } = 1;   // 0=밀리초, 1=초, 2=분

        [JsonIgnore]
        public int LoopDelayMs => LoopDelay * UnitFactor(LoopDelayUnit);

        // 단위 번호 → 밀리초 배수
        public static int UnitFactor(int unit) => unit == 2 ? 60000 : unit == 1 ? 1000 : 1;
        public static string UnitName(int unit) => unit == 2 ? "분" : unit == 1 ? "초" : "밀리초";

        // 클릭 방식
        public ClickMode       ClickMode       { get; set; } = ClickMode.Normal;

        // ADB(안드로이드 에뮬레이터) 설정
        public string AdbPath        { get; set; } = "";            // 비우면 자동 탐색
        public string AdbSerial      { get; set; } = "";            // 예: 127.0.0.1:5555 (비우면 첫 번째 기기)
        public string AdbWindowTitle { get; set; } = "BlueStacks";  // 렌더 영역을 찾을 창 제목 (일부 일치)
        public bool   AdbManualArea  { get; set; } = false;         // true 면 아래 좌표를 그대로 사용
        public int    AdbAreaX       { get; set; } = 0;
        public int    AdbAreaY       { get; set; } = 0;
        public int    AdbAreaW       { get; set; } = 0;
        public int    AdbAreaH       { get; set; } = 0;
        public int    AdbLongPressMs { get; set; } = 600;           // 우클릭 → 길게 누르기 시간

        [JsonIgnore] public uint StartMod { get; set; } = 0;
        [JsonIgnore] public uint StartVk  { get; set; } = (uint)Keys.F5;
        [JsonIgnore] public uint StopMod  { get; set; } = 0;
        [JsonIgnore] public uint StopVk   { get; set; } = (uint)Keys.F6;

        public MacroItem Clone()
        {
            var c = (MacroItem)MemberwiseClone();
            c.Steps = new List<MacroStep>();
            foreach (var s in Steps)
                c.Steps.Add(CloneStep(s));
            return c;
        }

        public static MacroStep CloneStep(MacroStep s) => new MacroStep {
            ImagePath       = s.ImagePath,      Type            = s.Type,
            GroupId         = s.GroupId,        RightClick      = s.RightClick,
            ClickCount      = s.ClickCount,     ClickDelay      = s.ClickDelay,
            WaitAfter       = s.WaitAfter,      Confidence      = s.Confidence,
            Enabled         = s.Enabled,        Timeout         = s.Timeout,
            ClickOffsetX    = s.ClickOffsetX,   ClickOffsetY    = s.ClickOffsetY,
            UseCustomOffset = s.UseCustomOffset, KeyText        = s.KeyText,
            HotKey          = s.HotKey,         MoveX           = s.MoveX,
            MoveY           = s.MoveY,          MoveRelative    = s.MoveRelative,
            MoveAction      = s.MoveAction,     JumpOnSuccess   = s.JumpOnSuccess,
            DelayMs         = s.DelayMs,        DelayUnit       = s.DelayUnit,
            ToggleTargets   = s.ToggleTargets,  ToggleAction    = s.ToggleAction,
            NotificationText = s.NotificationText,
            WatchTargets    = s.WatchTargets
        };
    }
}
