namespace Macronic;

public class AppState
{
    public int SeDelayMs { get; set; } = 85;
    public int DbDelayMs { get; set; } = 85;
    public int IbDelayMs { get; set; } = 85;
    public int SpDelayMs { get; set; } = 85;
    public int DeDelayMs { get; set; } = 85;

    public string ProofKey { get; set; } = "h";

    public bool SeEnabled { get; set; } = false;
    public bool DbEnabled { get; set; } = false;
    public bool IbEnabled { get; set; } = false;
    public bool SpEnabled { get; set; } = false;
    public bool DeEnabled { get; set; } = false;

    public string SeBind { get; set; } = "";
    public string DbBind { get; set; } = "";
    public string SpBind { get; set; } = "";
    public string DeBind { get; set; } = "";

    public string KbBuildingEdit           { get; set; } = "";
    public string KbSelectBuildingEdit     { get; set; } = "";
    public string KbWall                   { get; set; } = "";
    public string KbFloor                  { get; set; } = "";
    public string KbStairs                 { get; set; } = "";
    public string KbCone                   { get; set; } = "";
    public string KbSecondaryPlaceBuilding { get; set; } = "";
    public string KbPickaxe                { get; set; } = "";
    public string KbShotgun                { get; set; } = "";
    public string KbSprint                 { get; set; } = "";
    public string KbWalkForward            { get; set; } = "";
    public string KbInteract               { get; set; } = "";
    public string KbSecondaryShoot         { get; set; } = "";
    public string KbSecondaryWall          { get; set; } = "";

    public volatile bool IsSeRunning = false;
    public volatile bool DbKeyHeld   = false;
    public volatile bool SpKeyHeld   = false;
    public volatile bool IsDeKeyHeld = false;

    public string SpMode { get; set; } = "Toggle";

    public string CurrentSlot   { get; set; } = "6";
    public bool   CaptureHidden { get; set; } = false;

    public string CrTemplate    { get; set; } = "";
    public string CrColor       { get; set; } = "#ffffff";
    public bool   CrOutline     { get; set; } = false;
    public int    CrOutlineSize { get; set; } = 1;
    public int    CrSize        { get; set; } = 100;

    public bool   AlEnabled  { get; set; } = false;
    public string AlPosition { get; set; } = "Top Right";
}
