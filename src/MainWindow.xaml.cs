using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Macronic;

public partial class MainWindow : Window
{
    private readonly AppState        _s = new();
    private GlobalKeyboardHook?      _hook;

    private bool            _bindingMode;
    private Action<string>? _bindingCallback;
    private Button?         _bindingBtn;

    private volatile bool   _forwardKeyDown = false;
    private bool _isManualEditPulloutRunning = false;

    private double           _scrollTarget;
    private DispatcherTimer? _scrollTimer;

    private Button? _activeNavBtn;

    private CrosshairOverlay?  _crOverlay;
    private ArraylistOverlay?  _alOverlay;

    private static readonly (string id, string name)[] CrTemplates =
    {
        ("dot",              "Dot"),
        ("ring",             "Ring"),
        ("sq_dot",           "Square"),
        ("thin_cross",       "Thin +"),
        ("thick_cross",      "Thick +"),
        ("cross_dot_c",      "Cross·"),
        ("t_shape",          "T-Shape"),
        ("cross_circle",     "Circle+"),
        ("small_plus",       "S.Plus"),
        ("large_plus",       "L.Plus"),
        ("sniper",           "Sniper"),
        ("x_cross",          "X Cross"),
        ("x_dot",            "X·"),
        ("inward_arrows",    "Arrows"),
        ("outward_chevrons", "Chevrons"),
        ("triangle",         "Triangle"),
        ("diamond",          "Diamond")
    };

    private static readonly string[] CrColors =
    {
        "#ffffff", "#ff3333", "#33ff66", "#00ffff",
        "#ffff00", "#ff00ff", "#ff8800", "#000000"
    };

    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern int  SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
    private const int WM_NCLBUTTONDOWN = 0xA1;
    private const int HT_CAPTION       = 0x2;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);
    private const uint WDA_NONE               = 0x00000000;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private const int GWL_EXSTYLE      = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_APPWINDOW  = 0x00040000;

    [DllImport("winmm.dll")]
    private static extern uint timeBeginPeriod(uint uPeriod);

    [DllImport("winmm.dll")]
    private static extern uint timeEndPeriod(uint uPeriod);

    public MainWindow()
    {
        timeBeginPeriod(1);

        RenderOptions.ProcessRenderMode = RenderMode.Default;

        InitializeComponent();

        SourceInitialized += (_, _) => ApplyCaptureAffinity();

        Loaded += (_, _) =>
        {
            _hook = new GlobalKeyboardHook();
            _hook.KeyDown += OnGlobalKeyDown;
            _hook.KeyUp   += OnGlobalKeyUp;

            if (OuterBorder != null)
            {
                var clip = new RectangleGeometry
                {
                    RadiusX = 0,
                    RadiusY = 0,
                    Rect    = new Rect(0, 0, ActualWidth, ActualHeight)
                };
                OuterBorder.Clip = clip;

                SizeChanged += (_, e) =>
                {
                    clip.Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height);
                };
            }
        };

        Closed += (_, _) =>
        {
            _hook?.Dispose();
            _crOverlay?.Close();
            _alOverlay?.Close();
            timeEndPeriod(1);
        };

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            var windowFade = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(350)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            windowFade.Completed += (_, _) =>
            {
                var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
                t.Tick += (_, _) => { t.Stop(); StartTyping(); };
                t.Start();
            };
            BeginAnimation(OpacityProperty, windowFade);
        });
    }

    private void ApplyCaptureAffinity()
    {
        bool   active  = _s.CaptureHidden;
        uint   aff     = active ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE;
        var    hwnd    = new WindowInteropHelper(this).Handle;

        SetWindowDisplayAffinity(hwnd, aff);
        _crOverlay?.SetProof(active);
        _alOverlay?.SetProof(active);

        int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        if (active)
        {
            ex |=  WS_EX_TOOLWINDOW;
            ex &= ~WS_EX_APPWINDOW;
        }
        else
        {
            ex &= ~WS_EX_TOOLWINDOW;
            ex |=  WS_EX_APPWINDOW;
        }
        SetWindowLong(hwnd, GWL_EXSTYLE, ex);
    }

    private void ToggleCaptureHide()
    {
        _s.CaptureHidden = !_s.CaptureHidden;
        ApplyCaptureAffinity();
    }

    private void StartTyping()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var fadeIn = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(600))) { EasingFunction = ease };
        AsciiText.BeginAnimation(OpacityProperty, fadeIn);

        var scaleX = new DoubleAnimation(0.92, 1, new Duration(TimeSpan.FromMilliseconds(600))) { EasingFunction = ease };
        var scaleY = new DoubleAnimation(0.92, 1, new Duration(TimeSpan.FromMilliseconds(600))) { EasingFunction = ease };
        var st = (ScaleTransform)AsciiText.RenderTransform;
        st.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
        st.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);

        var lineExpand = new DoubleAnimation(0, 120, new Duration(TimeSpan.FromMilliseconds(500)))
        {
            BeginTime      = TimeSpan.FromMilliseconds(400),
            EasingFunction = ease
        };
        AccentLine.BeginAnimation(FrameworkElement.WidthProperty, lineExpand);

        var t2 = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1600) };
        t2.Tick += (_, _) =>
        {
            t2.Stop();
            StatusText.Text = "starting Macronic Premium...";
            var fade = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(400)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            StatusText.BeginAnimation(OpacityProperty, fade);
        };
        t2.Start();

        var t3 = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(3000) };
        t3.Tick += (_, _) => { t3.Stop(); TransitionToMain(); };
        t3.Start();
    }

    private void TransitionToMain()
    {
        var fade = new DoubleAnimation(1, 0, new Duration(TimeSpan.FromMilliseconds(350)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        fade.Completed += (_, _) =>
        {
            StartupGrid.Visibility = Visibility.Collapsed;
            MainGrid.Visibility    = Visibility.Visible;
            MainGrid.Opacity       = 0;
            var fadeIn = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(300)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            MainGrid.BeginAnimation(OpacityProperty, fadeIn);
            InitButtons();
            InitKeybindButtons();
            RefreshDelayLabels();
            RefreshStatusDots();
            SetupSmoothScroll(MainScrollViewer);
            AnimateNavSelect(BtnMacros);
            _crOverlay = new CrosshairOverlay();
            InitCrosshairsPanel();
            RefreshCrosshairOverlay();
            _alOverlay = new ArraylistOverlay();
            InitArraylistPanel();
        };
        StartupGrid.BeginAnimation(OpacityProperty, fade);
    }

    private void SetupSmoothScroll(ScrollViewer sv)
    {
        _scrollTarget = 0;
        sv.PreviewMouseWheel += (s, e) =>
        {
            e.Handled = true;
            _scrollTarget = Math.Clamp(
                _scrollTarget - e.Delta * 0.45,
                0,
                sv.ScrollableHeight);
            if (_scrollTimer == null || !_scrollTimer.IsEnabled)
            {
                _scrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(7) };
                _scrollTimer.Tick += (_, _) =>
                {
                    double current = sv.VerticalOffset;
                    double diff    = _scrollTarget - current;
                    if (Math.Abs(diff) < 0.3)
                    {
                        sv.ScrollToVerticalOffset(_scrollTarget);
                        _scrollTimer.Stop();
                    }
                    else
                    {
                        sv.ScrollToVerticalOffset(current + diff * 0.18);
                    }
                };
                _scrollTimer.Start();
            }
        };
    }

    private void FadeInPanel(StackPanel panel)
    {
        panel.Opacity = 0;
        var anim = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(160)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        panel.BeginAnimation(OpacityProperty, anim);
    }

    private void InitButtons()
    {
        if (FindName("DeBindBtn") is Button deB) deB.Content = DisplayKey(_s.DeBind);
        if (FindName("SeBindBtn") is Button seB) seB.Content = DisplayKey(_s.SeBind);
        if (FindName("DbBindBtn") is Button dbB) dbB.Content = DisplayKey(_s.DbBind);

        if (FindName("DeToggle") is ToggleButton deT) deT.IsChecked = _s.DeEnabled;
        if (FindName("SeToggle") is ToggleButton seT) seT.IsChecked = _s.SeEnabled;
        if (FindName("DbToggle") is ToggleButton dbT) dbT.IsChecked = _s.DbEnabled;
        if (FindName("IbToggle") is ToggleButton ibT) ibT.IsChecked = _s.IbEnabled;
        
        if (FindName("PulloutPickaxeBox") is CheckBox pB) pB.IsChecked = _s.PulloutPickaxe;
        if (FindName("PulloutShotgunBox") is CheckBox sB) sB.IsChecked = _s.PulloutShotgun;
        if (FindName("PulloutSlot1Box") is CheckBox s1B) s1B.IsChecked = _s.PulloutSlot1;
        if (FindName("PulloutSlot2Box") is CheckBox s2B) s2B.IsChecked = _s.PulloutSlot2;
        if (FindName("PulloutSlot3Box") is CheckBox s3B) s3B.IsChecked = _s.PulloutSlot3;
        if (FindName("PulloutSlot4Box") is CheckBox s4B) s4B.IsChecked = _s.PulloutSlot4;
        if (FindName("PulloutSlot5Box") is CheckBox s5B) s5B.IsChecked = _s.PulloutSlot5;

        if (FindName("DeDelaySlider") is Slider deS) deS.Value = _s.DeDelayMs;
        if (FindName("SeDelaySlider") is Slider seS) seS.Value = _s.SeDelayMs;
        if (FindName("DbDelaySlider") is Slider dbS) dbS.Value = _s.DbDelayMs;
        if (FindName("IbDelaySlider") is Slider ibS) ibS.Value = _s.IbDelayMs;
    }

    private void InitKeybindButtons()
    {
        if (FindName("KbBuildingEditBtn") is Button b1) b1.Content = DisplayKey(_s.KbBuildingEdit);
        if (FindName("KbSelectBuildingEditBtn") is Button b2) b2.Content = DisplayKey(_s.KbSelectBuildingEdit);
        if (FindName("KbWallBtn") is Button b3) b3.Content = DisplayKey(_s.KbWall);
        if (FindName("KbFloorBtn") is Button b4) b4.Content = DisplayKey(_s.KbFloor);
        if (FindName("KbStairsBtn") is Button b5) b5.Content = DisplayKey(_s.KbStairs);
        if (FindName("KbConeBtn") is Button b6) b6.Content = DisplayKey(_s.KbCone);
        if (FindName("KbSecondaryPlaceBuildingBtn") is Button b7) b7.Content = DisplayKey(_s.KbSecondaryPlaceBuilding);
        if (FindName("KbPickaxeBtn") is Button b8) b8.Content = DisplayKey(_s.KbPickaxe);
        if (FindName("KbShotgunBtn") is Button b9) b9.Content = DisplayKey(_s.KbShotgun);
        if (FindName("KbSprintBtn") is Button b10) b10.Content = DisplayKey(_s.KbSprint);
        if (FindName("KbWalkForwardBtn") is Button b11) b11.Content = DisplayKey(_s.KbWalkForward);
        if (FindName("KbInteractBtn") is Button b12) b12.Content = DisplayKey(_s.KbInteract);
        if (FindName("KbSecondaryShootBtn") is Button b13) b13.Content = DisplayKey(_s.KbSecondaryShoot);
        if (FindName("KbSecondaryWallBtn") is Button b14) b14.Content = DisplayKey(_s.KbSecondaryWall);
        
        if (FindName("KbSlot1Btn") is Button s1) s1.Content = DisplayKey(_s.KbSlot1);
        if (FindName("KbSlot2Btn") is Button s2) s2.Content = DisplayKey(_s.KbSlot2);
        if (FindName("KbSlot3Btn") is Button s3) s3.Content = DisplayKey(_s.KbSlot3);
        if (FindName("KbSlot4Btn") is Button s4) s4.Content = DisplayKey(_s.KbSlot4);
        if (FindName("KbSlot5Btn") is Button s5) s5.Content = DisplayKey(_s.KbSlot5);
    }

    private void RefreshDelayLabels()
    {
        if (FindName("DeDelayText") is TextBlock deT) deT.Text = $"{_s.DeDelayMs} ms";
        if (FindName("SeDelayText") is TextBlock seT) seT.Text = $"{_s.SeDelayMs} ms";
        if (FindName("DbDelayText") is TextBlock dbT) dbT.Text = $"{_s.DbDelayMs} ms";
        if (FindName("IbDelayText") is TextBlock ibT) ibT.Text = $"{_s.IbDelayMs} ms";
    }

    private void RefreshStatusDots()
    {
        var on  = new SolidColorBrush(Color.FromRgb(0x22, 0xcc, 0x55));
        var off = new SolidColorBrush(Color.FromRgb(0x1a, 0x2a, 0x1a));
        if (FindName("DeStatusDot") is System.Windows.Shapes.Ellipse de) de.Fill = _s.DeEnabled ? on : off;
        if (FindName("SeStatusDot") is System.Windows.Shapes.Ellipse p1) p1.Fill = _s.SeEnabled ? on : off;
        if (FindName("DbStatusDot") is System.Windows.Shapes.Ellipse p2) p2.Fill = _s.DbEnabled ? on : off;
        if (FindName("IbStatusDot") is System.Windows.Shapes.Ellipse p3) p3.Fill = _s.IbEnabled ? on : off;
        if (FindName("SpStatusDot") is System.Windows.Shapes.Ellipse p4) p4.Fill = _s.SpEnabled ? on : off;
    }

    private static string DisplayKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return "—";
        var d = key.Replace("Key.", "").ToUpper();
        return d.Length > 8 ? d[..8] : d;
    }

    private void InitCrosshairsPanel()
    {
        if (FindName("CrTemplatePanel") is StackPanel tp)
        {
            tp.Children.Clear();
            foreach (var (id, name) in CrTemplates)
                tp.Children.Add(BuildTemplateTile(id, name));
        }

        if (FindName("CrColorPanel") is StackPanel cp)
        {
            cp.Children.Clear();
            foreach (var hex in CrColors)
                cp.Children.Add(BuildColorSwatch(hex));
        }

        UpdateTemplateTileSelection();
        UpdateColorSwatchSelection();

        if (FindName("CrOutlineToggle") is ToggleButton ot) ot.IsChecked = _s.CrOutline;
        if (FindName("CrOutlineSizeSlider") is Slider os) os.Value = _s.CrOutlineSize;
        if (FindName("CrSizeSlider") is Slider cs) cs.Value = _s.CrSize;
        if (FindName("CrOutlineSizeLabel") is TextBlock ol) ol.Text = _s.CrOutlineSize.ToString();
        if (FindName("CrSizeLabel") is TextBlock cl) cl.Text = $"{_s.CrSize}%";
    }

    private UIElement BuildTemplateTile(string id, string name)
    {
        var canvas = new Canvas
        {
            Width      = 64,
            Height     = 64,
            Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14))
        };
        CrDraw.Draw(canvas, 32, 32, 0.5, _s.CrColor, _s.CrOutline, _s.CrOutlineSize, id);

        var label = new TextBlock
        {
            Text                = name,
            FontFamily          = (FontFamily)FindResource("IBMPlexMono"),
            FontSize            = 8,
            Foreground          = new SolidColorBrush(Color.FromRgb(0x5a, 0x5a, 0x5a)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin              = new Thickness(0, 4, 0, 0)
        };

        var inner = new StackPanel();
        inner.Children.Add(canvas);
        inner.Children.Add(label);

        var border = new Border
        {
            Width           = 74,
            Height          = 88,
            Padding         = new Thickness(4),
            BorderThickness = new Thickness(1),
            BorderBrush     = new SolidColorBrush(Color.FromRgb(0x1e, 0x1e, 0x1e)),
            Margin          = new Thickness(0, 0, 6, 6),
            Cursor          = Cursors.Hand,
            Tag             = id,
            Child           = inner
        };

        border.MouseLeftButtonDown += (_, _) =>
        {
            if (_s.CrTemplate == id)
                _s.CrTemplate = "";
            else
                _s.CrTemplate = id;
            UpdateTemplateTileSelection();
            RefreshCrosshairOverlay();
        };

        return border;
    }

    private UIElement BuildColorSwatch(string hex)
    {
        Color col;
        try   { col = (Color)ColorConverter.ConvertFromString(hex); }
        catch { col = Colors.White; }

        var border = new Border
        {
            Width           = 24,
            Height          = 24,
            Margin          = new Thickness(0, 0, 6, 0),
            BorderThickness = new Thickness(2),
            BorderBrush     = new SolidColorBrush(Colors.Transparent),
            Background      = new SolidColorBrush(col),
            Cursor          = Cursors.Hand,
            Tag             = hex
        };

        border.MouseLeftButtonDown += (_, _) =>
        {
            _s.CrColor = hex;
            UpdateColorSwatchSelection();
            RebuildTemplatePreviews();
            RefreshCrosshairOverlay();
        };

        return border;
    }

    private void UpdateTemplateTileSelection()
    {
        if (FindName("CrTemplatePanel") is StackPanel tp)
        {
            foreach (UIElement el in tp.Children)
            {
                if (el is Border b)
                {
                    b.BorderBrush = b.Tag as string == _s.CrTemplate
                        ? new SolidColorBrush(Color.FromRgb(0xf5, 0xf5, 0xf5))
                        : new SolidColorBrush(Color.FromRgb(0x1e, 0x1e, 0x1e));
                }
            }
        }
    }

    private void UpdateColorSwatchSelection()
    {
        if (FindName("CrColorPanel") is StackPanel cp)
        {
            foreach (UIElement el in cp.Children)
            {
                if (el is Border b)
                {
                    b.BorderBrush = b.Tag as string == _s.CrColor
                        ? new SolidColorBrush(Color.FromRgb(0xf5, 0xf5, 0xf5))
                        : new SolidColorBrush(Colors.Transparent);
                }
            }
        }
    }

    private void RebuildTemplatePreviews()
    {
        if (FindName("CrTemplatePanel") is StackPanel tp)
        {
            foreach (UIElement el in tp.Children)
            {
                if (el is Border b && b.Child is StackPanel sp && sp.Children.Count > 0 && sp.Children[0] is Canvas canvas)
                {
                    string id = b.Tag as string ?? "";
                    CrDraw.Draw(canvas, 32, 32, 0.5, _s.CrColor, _s.CrOutline, _s.CrOutlineSize, id);
                }
            }
        }
    }

    private void RefreshCrosshairOverlay()
    {
        _crOverlay?.UpdateCrosshair(_s.CrTemplate, _s.CrColor, _s.CrOutline, _s.CrOutlineSize, _s.CrSize);
    }

    private void CrToggle_Changed(object s, RoutedEventArgs e)
    {
        if (FindName("CrOutlineToggle") is ToggleButton ot) _s.CrOutline = ot.IsChecked == true;
        RebuildTemplatePreviews();
        RefreshCrosshairOverlay();
    }

    private void CrOutlineSizeSlider_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        _s.CrOutlineSize = (int)e.NewValue;
        if (FindName("CrOutlineSizeLabel") is TextBlock ol) ol.Text = _s.CrOutlineSize.ToString();
        RebuildTemplatePreviews();
        RefreshCrosshairOverlay();
    }

    private void CrSizeSlider_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        _s.CrSize = (int)e.NewValue;
        if (FindName("CrSizeLabel") is TextBlock cl) cl.Text = $"{_s.CrSize}%";
        RefreshCrosshairOverlay();
    }

    private void InitArraylistPanel()
    {
        if (FindName("AlEnabledToggle") is ToggleButton et) et.IsChecked = _s.AlEnabled;
        RefreshAlPositionButtons();
        RefreshArraylistOverlay();
    }

    private void RefreshAlPositionButtons()
    {
        var active = new SolidColorBrush(Color.FromRgb(0xf5, 0xf5, 0xf5));
        var dim    = new SolidColorBrush(Color.FromRgb(0x5a, 0x5a, 0x5a));
        if (FindName("AlPosTopLeft") is Button b1) b1.Foreground = _s.AlPosition == "Top Left" ? active : dim;
        if (FindName("AlPosTopRight") is Button b2) b2.Foreground = _s.AlPosition == "Top Right" ? active : dim;
        if (FindName("AlPosBotLeft") is Button b3) b3.Foreground = _s.AlPosition == "Bottom Left" ? active : dim;
        if (FindName("AlPosBotRight") is Button b4) b4.Foreground = _s.AlPosition == "Bottom Right" ? active : dim;
    }

    private void SetAlPosition(string pos)
    {
        _s.AlPosition = pos;
        RefreshAlPositionButtons();
        RefreshArraylistOverlay();
    }

    private void AlToggle_Changed(object s, RoutedEventArgs e)
    {
        if (FindName("AlEnabledToggle") is ToggleButton et) _s.AlEnabled = et.IsChecked == true;
        RefreshArraylistOverlay();
    }

    private void AlPosTopLeft_Click (object s, RoutedEventArgs e) => SetAlPosition("Top Left");
    private void AlPosTopRight_Click(object s, RoutedEventArgs e) => SetAlPosition("Top Right");
    private void AlPosBotLeft_Click (object s, RoutedEventArgs e) => SetAlPosition("Bottom Left");
    private void AlPosBotRight_Click(object s, RoutedEventArgs e) => SetAlPosition("Bottom Right");

    private void RefreshArraylistOverlay()
    {
        if (_alOverlay == null) return;
        var items = new System.Collections.Generic.List<(string Name, string Detail)>();
        if (_s.DeEnabled) items.Add(("DRAG EDIT",     $"{_s.DeDelayMs}ms"));
        if (_s.SeEnabled) items.Add(("SINGLE EDIT",   $"{_s.SeDelayMs}ms"));
        if (_s.DbEnabled) items.Add(("DOUBLE EDIT",   $"{_s.DbDelayMs}ms"));
        if (_s.IbEnabled) items.Add(("INSTANT BUILD", $"{_s.IbDelayMs}ms"));
        if (_s.SpEnabled && _s.SpKeyHeld) items.Add(("SPRINT", _s.SpMode));
        _alOverlay.UpdateItems(items, _s.AlPosition, _s.AlEnabled);
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            ReleaseCapture();
            SendMessage(new WindowInteropHelper(this).Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
        }
    }

    private void TitleBar_MouseMove(object sender, MouseEventArgs e) { }
    private void Minimize_Click(object s, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Close_Click(object s, RoutedEventArgs e)
    {
        if (FindName("GoodbyeOverlay") is Grid go)
        {
            go.Visibility = Visibility.Visible;
            var fadeIn = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(300)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            go.BeginAnimation(OpacityProperty, fadeIn);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                var fadeOut = new DoubleAnimation(1, 0, new Duration(TimeSpan.FromMilliseconds(350)))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                };
                fadeOut.Completed += (_, _) => Application.Current.Shutdown();
                BeginAnimation(OpacityProperty, fadeOut);
            };
            timer.Start();
        }
        else
        {
            Application.Current.Shutdown();
        }
    }

    private void AnimateNavSelect(Button activate)
    {
        if (_activeNavBtn != null) _activeNavBtn.Tag = "";
        activate.Tag = "Active";
        _activeNavBtn = activate;
    }

    private void HideAllPanels()
    {
        if (FindName("MacrosPanel") is FrameworkElement p1) p1.Visibility = Visibility.Collapsed;
        if (FindName("SettingsPanel") is FrameworkElement p2) p2.Visibility = Visibility.Collapsed;
        if (FindName("KeybindsPanel") is FrameworkElement p3) p3.Visibility = Visibility.Collapsed;
        if (FindName("CrosshairsPanel") is FrameworkElement p4) p4.Visibility = Visibility.Collapsed;
        if (FindName("ArraylistPanel") is FrameworkElement p5) p5.Visibility = Visibility.Collapsed;
    }

    private void BtnMacros_Click(object s, RoutedEventArgs e)
    {
        HideAllPanels();
        if (FindName("MacrosPanel") is StackPanel p)
        {
            p.Visibility = Visibility.Visible;
            FadeInPanel(p);
        }
        if (s is Button b) AnimateNavSelect(b);
        _scrollTarget = 0;
        MainScrollViewer.ScrollToTop();
    }

    private void BtnSettings_Click(object s, RoutedEventArgs e)
    {
        HideAllPanels();
        if (FindName("SettingsPanel") is StackPanel p)
        {
            p.Visibility = Visibility.Visible;
            FadeInPanel(p);
        }
        if (s is Button b) AnimateNavSelect(b);
        _scrollTarget = 0;
        MainScrollViewer.ScrollToTop();
    }

    private void BtnKeybinds_Click(object s, RoutedEventArgs e)
    {
        HideAllPanels();
        if (FindName("KeybindsPanel") is StackPanel p)
        {
            p.Visibility = Visibility.Visible;
            FadeInPanel(p);
        }
        if (s is Button b) AnimateNavSelect(b);
        _scrollTarget = 0;
        MainScrollViewer.ScrollToTop();
    }

    private void BtnCrosshairs_Click(object s, RoutedEventArgs e)
    {
        HideAllPanels();
        if (FindName("CrosshairsPanel") is StackPanel p)
        {
            p.Visibility = Visibility.Visible;
            FadeInPanel(p);
        }
        if (s is Button b) AnimateNavSelect(b);
        _scrollTarget = 0;
        MainScrollViewer.ScrollToTop();
    }

    private void BtnArraylist_Click(object s, RoutedEventArgs e)
    {
        HideAllPanels();
        if (FindName("ArraylistPanel") is StackPanel p)
        {
            p.Visibility = Visibility.Visible;
            FadeInPanel(p);
        }
        if (s is Button b) AnimateNavSelect(b);
        _scrollTarget = 0;
        MainScrollViewer.ScrollToTop();
    }

    private void StartBinding(Button btn, Action<string> callback)
    {
        if (_bindingMode) return;
        _bindingMode     = true;
        _bindingCallback = callback;
        _bindingBtn      = btn;
        btn.Content      = "...";
        btn.Background   = (Brush)FindResource("Red");
    }

    private void FinishBinding(string key)
    {
        if (!_bindingMode) return;
        _bindingCallback?.Invoke(key);
        if (_bindingBtn is { } b)
        {
            b.Content    = DisplayKey(key);
            b.Background = (Brush)FindResource("BgBtn");
        }
        _bindingMode     = false;
        _bindingCallback = null;
        _bindingBtn      = null;
    }

    private void DeBindBtn_Click(object s, RoutedEventArgs e) { if (s is Button b) StartBinding(b, k => _s.DeBind = k); }
    private void SeBindBtn_Click(object s, RoutedEventArgs e) { if (s is Button b) StartBinding(b, k => _s.SeBind = k); }
    private void DbBindBtn_Click(object s, RoutedEventArgs e) { if (s is Button b) StartBinding(b, k => _s.DbBind = k); }
    private void SpBindBtn_Click(object s, RoutedEventArgs e) { if (s is Button b) StartBinding(b, k => _s.SpBind = k); }

    private bool _spDropdownOpen = false;

    private void SpModeBtn_Click(object s, RoutedEventArgs e)
    {
        if (FindName("SpModeDropdown") is FrameworkElement dd)
        {
            _spDropdownOpen = !_spDropdownOpen;
            double targetHeight = _spDropdownOpen ? 51 : 0;
            var anim = new DoubleAnimation(targetHeight, new Duration(TimeSpan.FromMilliseconds(160)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            dd.BeginAnimation(FrameworkElement.HeightProperty, anim);
        }
    }

    private void SetSpMode(string mode)
    {
        _s.SpMode = mode;
        if (FindName("SpModeBtn") is Button smb) smb.Content = mode;
        
        var active = new SolidColorBrush(Color.FromRgb(0xf5, 0xf5, 0xf5));
        var dim    = new SolidColorBrush(Color.FromRgb(0x5a, 0x5a, 0x5a));

        if (FindName("SpModeToggleBtn") is Button stb)
        {
            stb.FontSize = mode == "Toggle" ? 10 : 9;
            stb.Foreground = mode == "Toggle" ? active : dim;
        }
        if (FindName("SpModeHoldBtn") is Button shb)
        {
            shb.FontSize = mode == "Hold" ? 10 : 9;
            shb.Foreground = mode == "Hold" ? active : dim;
        }

        _spDropdownOpen = false;
        if (FindName("SpModeDropdown") is FrameworkElement dd)
        {
            var anim = new DoubleAnimation(0, new Duration(TimeSpan.FromMilliseconds(140)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            dd.BeginAnimation(FrameworkElement.HeightProperty, anim);
        }

        if (!string.IsNullOrEmpty(_s.KbSprint))
            InputSim.KeyUp(_s.KbSprint);
        _s.SpKeyHeld = false;
    }

    private void SpModeToggleBtn_Click(object s, RoutedEventArgs e) => SetSpMode("Toggle");
    private void SpModeHoldBtn_Click  (object s, RoutedEventArgs e) => SetSpMode("Hold");

    private void ProofKeyBtn_Click(object s, RoutedEventArgs e) { if (s is Button b) StartBinding(b, k => _s.ProofKey = k); }

    private void KbBuildingEditBtn_Click          (object s, RoutedEventArgs e) { if (s is Button b) StartBinding(b, k => _s.KbBuildingEdit           = k); }
    private void KbSelectBuildingEditBtn_Click    (object s, RoutedEventArgs e) { if (s is Button b) StartBinding(b, k => _s.KbSelectBuildingEdit     = k); }
    private void KbWallBtn_Click                  (object s, RoutedEventArgs e) { if (s is Button b) StartBinding(b, k => _s.KbWall                   = k); }
    private void KbFloorBtn_Click                 (object s, RoutedEventArgs e) { if (s is Button b) StartBinding(b, k => _s.KbFloor                  = k); }
    private void KbStairsBtn_Click                (object s, RoutedEventArgs e) { if (s is Button b) StartBinding(b, k => _s.KbStairs                 = k); }
    private void KbConeBtn_Click                  (object s, RoutedEventArgs e) { if (s is Button b) StartBinding(b, k => _s.KbCone                   = k); }
    private void KbSecondaryPlaceBuildingBtn_Click(object s, RoutedEventArgs e) { if (s is Button b) StartBinding(b, k => _s.KbSecondaryPlaceBuilding = k); }
    private void KbPickaxeBtn_Click               (object s, RoutedEventArgs e) { if (s is Button b) StartBinding(b, k => _s.KbPickaxe                = k); }
    private void KbShotgunBtn_Click               (object s, RoutedEventArgs e) { if (s is Button b) StartBinding(b, k => _s.KbShotgun                = k); }
    private void KbSlot1Btn_Click                 (object s, RoutedEventArgs e) { if (s is Button b) StartBinding(b, k => _s.KbSlot1                  = k); }
    private void KbSlot2Btn_Click                 (object s, RoutedEventArgs e) { if (s is Button b) StartBinding(b, k => _s.KbSlot2                  = k); }
    private void KbSlot3Btn_Click                 (object s, RoutedEventArgs e) { if (s is Button b) StartBinding(b, k => _s.KbSlot3                  = k); }
    private void KbSlot4Btn_Click                 (object s, RoutedEventArgs e) { if (s is Button b) StartBinding(b, k => _s.KbSlot4                  = k); }
    private void KbSlot5Btn_Click                 (object s, RoutedEventArgs e) { if (s is Button b) StartBinding(b, k => _s.KbSlot5                  = k); }
    private void KbSprintBtn_Click                (object s, RoutedEventArgs e) { if (s is Button b) StartBinding(b, k => _s.KbSprint                 = k); }
    private void KbWalkForwardBtn_Click           (object s, RoutedEventArgs e) { if (s is Button b) StartBinding(b, k => _s.KbWalkForward            = k); }
    private void KbInteractBtn_Click              (object s, RoutedEventArgs e) { if (s is Button b) StartBinding(b, k => _s.KbInteract               = k); }
    private void KbSecondaryShootBtn_Click        (object s, RoutedEventArgs e) { if (s is Button b) StartBinding(b, k => _s.KbSecondaryShoot         = k); }
    private void KbSecondaryWallBtn_Click         (object s, RoutedEventArgs e) { if (s is Button b) StartBinding(b, k => _s.KbSecondaryWall          = k); }

    private void Toggles_Changed(object s, RoutedEventArgs e)
    {
        if (FindName("DeToggle") is ToggleButton deT) _s.DeEnabled = deT.IsChecked == true;
        if (FindName("SeToggle") is ToggleButton seT) _s.SeEnabled = seT.IsChecked == true;
        if (FindName("DbToggle") is ToggleButton dbT) _s.DbEnabled = dbT.IsChecked == true;
        if (FindName("IbToggle") is ToggleButton ibT) _s.IbEnabled = ibT.IsChecked == true;
        
        if (FindName("PulloutPickaxeBox") is CheckBox pB) _s.PulloutPickaxe = pB.IsChecked == true;
        if (FindName("PulloutShotgunBox") is CheckBox sB) _s.PulloutShotgun = sB.IsChecked == true;
        if (FindName("PulloutSlot1Box") is CheckBox s1B) _s.PulloutSlot1 = s1B.IsChecked == true;
        if (FindName("PulloutSlot2Box") is CheckBox s2B) _s.PulloutSlot2 = s2B.IsChecked == true;
        if (FindName("PulloutSlot3Box") is CheckBox s3B) _s.PulloutSlot3 = s3B.IsChecked == true;
        if (FindName("PulloutSlot4Box") is CheckBox s4B) _s.PulloutSlot4 = s4B.IsChecked == true;
        if (FindName("PulloutSlot5Box") is CheckBox s5B) _s.PulloutSlot5 = s5B.IsChecked == true;

        RefreshStatusDots();
        RefreshArraylistOverlay();
    }

    private void Sliders_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        if (FindName("DeDelaySlider") is Slider deS && s == deS) _s.DeDelayMs = (int)e.NewValue;
        if (FindName("SeDelaySlider") is Slider seS && s == seS) _s.SeDelayMs = (int)e.NewValue;
        if (FindName("DbDelaySlider") is Slider dbS && s == dbS) _s.DbDelayMs = (int)e.NewValue;
        if (FindName("IbDelaySlider") is Slider ibS && s == ibS) _s.IbDelayMs = (int)e.NewValue;
        RefreshDelayLabels();
    }



    private void Export_Click(object s, RoutedEventArgs e)
    {
        var cfg = new
        {
            de_bind     = _s.DeBind,    de_delay_ms = _s.DeDelayMs,
            se_bind     = _s.SeBind,    se_delay_ms = _s.SeDelayMs,
            db_bind     = _s.DbBind,    db_delay_ms = _s.DbDelayMs,
            ib_delay_ms = _s.IbDelayMs,
            sp_bind     = _s.SpBind,    sp_delay_ms = _s.SpDelayMs,
            sp_mode     = _s.SpMode,
            proof_key   = _s.ProofKey,
            kb_building_edit            = _s.KbBuildingEdit,
            kb_select_building_edit     = _s.KbSelectBuildingEdit,
            kb_wall                     = _s.KbWall,
            kb_floor                    = _s.KbFloor,
            kb_stairs                   = _s.KbStairs,
            kb_cone                     = _s.KbCone,
            kb_secondary_place_building = _s.KbSecondaryPlaceBuilding,
            kb_pickaxe                  = _s.KbPickaxe,
            kb_shotgun                  = _s.KbShotgun,
            kb_sprint                   = _s.KbSprint,
            kb_walk_forward             = _s.KbWalkForward,
            kb_interact                 = _s.KbInteract,
            kb_secondary_shoot          = _s.KbSecondaryShoot,
            kb_secondary_wall           = _s.KbSecondaryWall,
            cr_template                 = _s.CrTemplate,
            cr_color                    = _s.CrColor,
            cr_outline                  = _s.CrOutline,
            cr_outline_size             = _s.CrOutlineSize,
            cr_size                     = _s.CrSize,
            al_enabled                  = _s.AlEnabled,
            al_position                 = _s.AlPosition
        };
        Clipboard.SetText(JsonSerializer.Serialize(cfg));
    }

    private void Import_Click(object s, RoutedEventArgs e)
    {
        try
        {
            var json = Clipboard.GetText();
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;

            _s.DeBind   = r.TryGetProp("de_bind",   out var v) ? v : _s.DeBind;
            _s.SeBind   = r.TryGetProp("se_bind",   out v)     ? v : _s.SeBind;
            _s.DbBind   = r.TryGetProp("db_bind",   out v)     ? v : _s.DbBind;
            _s.SpBind   = r.TryGetProp("sp_bind",   out v)     ? v : _s.SpBind;
            if (r.TryGetProp("sp_mode", out v) && (v == "Toggle" || v == "Hold")) _s.SpMode = v;
            _s.ProofKey = r.TryGetProp("proof_key", out v)     ? v : _s.ProofKey;
            _s.KbBuildingEdit           = r.TryGetProp("kb_building_edit",            out v) ? v : _s.KbBuildingEdit;
            _s.KbSelectBuildingEdit     = r.TryGetProp("kb_select_building_edit",     out v) ? v : _s.KbSelectBuildingEdit;
            _s.KbWall                   = r.TryGetProp("kb_wall",                     out v) ? v : _s.KbWall;
            _s.KbFloor                  = r.TryGetProp("kb_floor",                    out v) ? v : _s.KbFloor;
            _s.KbStairs                 = r.TryGetProp("kb_stairs",                   out v) ? v : _s.KbStairs;
            _s.KbCone                   = r.TryGetProp("kb_cone",                     out v) ? v : _s.KbCone;
            _s.KbSecondaryPlaceBuilding = r.TryGetProp("kb_secondary_place_building", out v) ? v : _s.KbSecondaryPlaceBuilding;
            _s.KbPickaxe                = r.TryGetProp("kb_pickaxe",                  out v) ? v : _s.KbPickaxe;
            _s.KbShotgun                = r.TryGetProp("kb_shotgun",                  out v) ? v : _s.KbShotgun;
            _s.KbSprint                 = r.TryGetProp("kb_sprint",                   out v) ? v : _s.KbSprint;
            _s.KbWalkForward            = r.TryGetProp("kb_walk_forward",             out v) ? v : _s.KbWalkForward;
            _s.KbInteract               = r.TryGetProp("kb_interact",                 out v) ? v : _s.KbInteract;
            _s.KbSecondaryShoot         = r.TryGetProp("kb_secondary_shoot",          out v) ? v : _s.KbSecondaryShoot;
            _s.KbSecondaryWall          = r.TryGetProp("kb_secondary_wall",           out v) ? v : _s.KbSecondaryWall;
            _s.CrTemplate               = r.TryGetProp("cr_template",                 out v) ? v : _s.CrTemplate;
            _s.CrColor                  = r.TryGetProp("cr_color",                    out v) ? v : _s.CrColor;

            if (r.TryGetProperty("de_delay_ms",     out var jv) && jv.TryGetInt32(out var i)) _s.DeDelayMs     = i;
            if (r.TryGetProperty("se_delay_ms",     out jv)     && jv.TryGetInt32(out i))     _s.SeDelayMs     = i;
            if (r.TryGetProperty("db_delay_ms",     out jv)     && jv.TryGetInt32(out i))     _s.DbDelayMs     = i;
            if (r.TryGetProperty("ib_delay_ms",     out jv)     && jv.TryGetInt32(out i))     _s.IbDelayMs     = i;
            if (r.TryGetProperty("sp_delay_ms",     out jv)     && jv.TryGetInt32(out i))     _s.SpDelayMs     = i;

            if (r.TryGetProperty("pullout_pickaxe", out jv)) _s.PulloutPickaxe = jv.GetBoolean();
            if (r.TryGetProperty("pullout_shotgun", out jv)) _s.PulloutShotgun = jv.GetBoolean();
            if (r.TryGetProperty("pullout_slot1",   out jv)) _s.PulloutSlot1   = jv.GetBoolean();
            if (r.TryGetProperty("pullout_slot2",   out jv)) _s.PulloutSlot2   = jv.GetBoolean();
            if (r.TryGetProperty("pullout_slot3",   out jv)) _s.PulloutSlot3   = jv.GetBoolean();
            if (r.TryGetProperty("pullout_slot4",   out jv)) _s.PulloutSlot4   = jv.GetBoolean();
            if (r.TryGetProperty("pullout_slot5",   out jv)) _s.PulloutSlot5   = jv.GetBoolean();

            if (r.TryGetProperty("cr_outline_size", out jv)     && jv.TryGetInt32(out i))     _s.CrOutlineSize = i;
            if (r.TryGetProperty("cr_size",         out jv)     && jv.TryGetInt32(out i))     _s.CrSize        = i;
            if (r.TryGetProperty("cr_outline",      out jv) && jv.ValueKind == JsonValueKind.True  ) _s.CrOutline = true;
            if (r.TryGetProperty("cr_outline",      out jv) && jv.ValueKind == JsonValueKind.False ) _s.CrOutline = false;
            if (r.TryGetProp("al_position", out v) && new[]{"Top Left","Top Right","Bottom Left","Bottom Right"}.Contains(v)) _s.AlPosition = v;
            if (r.TryGetProperty("al_enabled", out jv) && jv.ValueKind == JsonValueKind.True  ) _s.AlEnabled = true;
            if (r.TryGetProperty("al_enabled", out jv) && jv.ValueKind == JsonValueKind.False ) _s.AlEnabled = false;

            InitButtons();
            InitKeybindButtons();
            RefreshDelayLabels();
            if (FindName("CrTemplatePanel") != null) InitCrosshairsPanel();
            RefreshCrosshairOverlay();
            if (FindName("AlEnabledToggle") != null) InitArraylistPanel();
        }
        catch { }
    }

    private void OnGlobalKeyDown(string key)
    {
        if (_bindingMode)
        {
            Dispatcher.Invoke(() => FinishBinding(key));
            return;
        }

        if (key.Length == 1 && key[0] >= '1' && key[0] <= '9')
            _s.CurrentSlot = key;

        if (key == _s.ProofKey)
            Dispatcher.Invoke(ToggleCaptureHide);

        if (WindowGuard.IsGameActive())
        {
            // Pullout trigger for manual edit
            if ((_s.PulloutPickaxe || _s.PulloutShotgun || _s.PulloutSlot1 || _s.PulloutSlot2 || _s.PulloutSlot3 || _s.PulloutSlot4 || _s.PulloutSlot5) && key == _s.KbBuildingEdit)
            {
                // We use a flag to prevent multiple triggers during one edit
                if (!_isManualEditPulloutRunning)
                {
                    _isManualEditPulloutRunning = true;
                    new System.Threading.Thread(() => {
                        // Wait for user to finish edit (click/confirm)
                        PreciseSleep(200); 
                        HandlePullout();
                        PreciseSleep(300); // Cooldown
                        _isManualEditPulloutRunning = false;
                    }) { IsBackground = true }.Start();
                }
            }

            if (_s.DeEnabled && key == _s.DeBind && !_s.IsDeKeyHeld)
            {
                _s.IsDeKeyHeld = true;
                new System.Threading.Thread(RunDeSequence) { IsBackground = true }.Start();
            }

            if (_s.SeEnabled && key == _s.SeBind && !_s.IsSeRunning)
            {
                _s.IsSeRunning = true;
                new System.Threading.Thread(RunSeSequence) { IsBackground = true }.Start();
            }

            if (_s.DbEnabled && key == _s.DbBind && !_s.DbKeyHeld)
            {
                _s.DbKeyHeld = true;
                new System.Threading.Thread(RunDbSequence) { IsBackground = true }.Start();
            }

            if (_s.IbEnabled)
            {
                string spb = _s.KbSecondaryPlaceBuilding;
                if (!string.IsNullOrEmpty(spb) &&
                    (key == _s.KbWall || key == _s.KbFloor ||
                     key == _s.KbStairs || key == _s.KbCone))
                {
                    InputSim.KeyDown(spb);
                }
            }

            if (_s.SpEnabled && key == _s.SpBind)
            {
                if (_s.SpMode == "Toggle")
                {
                    _s.SpKeyHeld = !_s.SpKeyHeld;
                    if (!_s.SpKeyHeld && !string.IsNullOrEmpty(_s.KbSprint))
                        InputSim.KeyUp(_s.KbSprint);
                    else if (_s.SpKeyHeld && _forwardKeyDown && !string.IsNullOrEmpty(_s.KbSprint))
                        InputSim.KeyDown(_s.KbSprint);
                }
                else
                {
                    _s.SpKeyHeld = true;
                    if (_forwardKeyDown && !string.IsNullOrEmpty(_s.KbSprint))
                        InputSim.KeyDown(_s.KbSprint);
                }
                Dispatcher.BeginInvoke(() => RefreshArraylistOverlay());
            }

            if (_s.SpEnabled &&
                !string.IsNullOrEmpty(_s.KbWalkForward) && key == _s.KbWalkForward)
            {
                _forwardKeyDown = true;
                if (_s.SpKeyHeld && !string.IsNullOrEmpty(_s.KbSprint))
                    InputSim.KeyDown(_s.KbSprint);
            }
        }
    }

    private void OnGlobalKeyUp(string key)
    {
        if (_bindingMode) return;
        if (key == _s.DeBind) _s.IsDeKeyHeld = false;
        if (key == _s.SeBind) _s.IsSeRunning  = false;
        if (key == _s.DbBind) _s.DbKeyHeld    = false;
        if (_s.IbEnabled)
        {
            string spb = _s.KbSecondaryPlaceBuilding;
            if (!string.IsNullOrEmpty(spb) &&
                (key == _s.KbWall || key == _s.KbFloor ||
                 key == _s.KbStairs || key == _s.KbCone))
            {
                InputSim.KeyUp(spb);
            }
        }
        if (_s.SpEnabled && !string.IsNullOrEmpty(_s.KbWalkForward) &&
            key == _s.KbWalkForward)
        {
            _forwardKeyDown = false;
            if (!string.IsNullOrEmpty(_s.KbSprint))
                InputSim.KeyUp(_s.KbSprint);
        }
        if (_s.SpEnabled && _s.SpMode == "Hold" && key == _s.SpBind)
        {
            _s.SpKeyHeld = false;
            if (!string.IsNullOrEmpty(_s.KbSprint))
                InputSim.KeyUp(_s.KbSprint);
            Dispatcher.BeginInvoke(() => RefreshArraylistOverlay());
        }
    }

    private static readonly double _swFreq = (double)Stopwatch.Frequency;

    private const int SlotSwitchMinMs = 50;
    private const int KeyHoldMs       = 15;

    private static void PreciseSleep(int ms)
    {
        long target = Stopwatch.GetTimestamp() + (long)(ms * _swFreq / 1000.0);
        int sleepMs = ms - 2;
        if (sleepMs > 0)
            System.Threading.Thread.Sleep(sleepMs);
        while (Stopwatch.GetTimestamp() < target)
            System.Threading.Thread.SpinWait(8);
    }

    private static void SlotSwitch(string key, int delay)
    {
        if (string.IsNullOrEmpty(key)) return;
        int totalWait = Math.Max(delay, SlotSwitchMinMs);
        InputSim.KeyDown(key);
        PreciseSleep(KeyHoldMs);
        InputSim.KeyUp(key);
        PreciseSleep(totalWait - KeyHoldMs);
    }

    private static int SafeDelay(int ms) => Math.Max(10, ms);

    private void HandlePullout()
    {
        if (_s.PulloutPickaxe && !string.IsNullOrEmpty(_s.KbPickaxe))
        {
            InputSim.KeyDown(_s.KbPickaxe);
            PreciseSleep(KeyHoldMs + 15); 
            InputSim.KeyUp(_s.KbPickaxe);
        }
        else if (_s.PulloutShotgun && !string.IsNullOrEmpty(_s.KbShotgun))
        {
            InputSim.KeyDown(_s.KbShotgun);
            PreciseSleep(KeyHoldMs + 15); 
            InputSim.KeyUp(_s.KbShotgun);
        }
        else if (_s.PulloutSlot1 && !string.IsNullOrEmpty(_s.KbSlot1))
        {
            InputSim.KeyDown(_s.KbSlot1);
            PreciseSleep(KeyHoldMs + 15); 
            InputSim.KeyUp(_s.KbSlot1);
        }
        else if (_s.PulloutSlot2 && !string.IsNullOrEmpty(_s.KbSlot2))
        {
            InputSim.KeyDown(_s.KbSlot2);
            PreciseSleep(KeyHoldMs + 15); 
            InputSim.KeyUp(_s.KbSlot2);
        }
        else if (_s.PulloutSlot3 && !string.IsNullOrEmpty(_s.KbSlot3))
        {
            InputSim.KeyDown(_s.KbSlot3);
            PreciseSleep(KeyHoldMs + 15); 
            InputSim.KeyUp(_s.KbSlot3);
        }
        else if (_s.PulloutSlot4 && !string.IsNullOrEmpty(_s.KbSlot4))
        {
            InputSim.KeyDown(_s.KbSlot4);
            PreciseSleep(KeyHoldMs + 15); 
            InputSim.KeyUp(_s.KbSlot4);
        }
        else if (_s.PulloutSlot5 && !string.IsNullOrEmpty(_s.KbSlot5))
        {
            InputSim.KeyDown(_s.KbSlot5);
            PreciseSleep(KeyHoldMs + 15); 
            InputSim.KeyUp(_s.KbSlot5);
        }
    }

    private void RunDeSequence()
    {
        string editKey   = _s.KbBuildingEdit;
        string selectKey = _s.KbSelectBuildingEdit;

        if (!string.IsNullOrEmpty(editKey))
        {
            InputSim.KeyDown(editKey);
            PreciseSleep(KeyHoldMs);
            InputSim.KeyUp(editKey);
            PreciseSleep(SafeDelay(_s.DeDelayMs));
        }

        if (!string.IsNullOrEmpty(selectKey))
        {
            InputSim.KeyDown(selectKey);
            while (_s.IsDeKeyHeld)
                PreciseSleep(5);
            InputSim.KeyUp(selectKey);
        }
        else
        {
            InputSim.LeftDown();
            while (_s.IsDeKeyHeld)
                PreciseSleep(5);
            InputSim.LeftUp();
        }

        HandlePullout();
    }

    private void RunSeSequence()
    {
        int delay = SafeDelay(_s.SeDelayMs);

        if (!string.IsNullOrEmpty(_s.KbBuildingEdit))
        {
            InputSim.KeyDown(_s.KbBuildingEdit);
            PreciseSleep(KeyHoldMs);
            InputSim.KeyUp(_s.KbBuildingEdit);
            PreciseSleep(delay);
        }

        if (!string.IsNullOrEmpty(_s.KbSelectBuildingEdit))
        {
            InputSim.KeyDown(_s.KbSelectBuildingEdit);
            PreciseSleep(KeyHoldMs);
            InputSim.KeyUp(_s.KbSelectBuildingEdit);
        }

        HandlePullout();
        _s.IsSeRunning = false;
    }

    private void RunDbSequence()
    {
        int delay    = SafeDelay(_s.DbDelayMs);
        int edits    = 0;
        int minEdits = 2;

        while (_s.DbKeyHeld || edits < minEdits)
        {
            if (!string.IsNullOrEmpty(_s.KbBuildingEdit))
            {
                InputSim.KeyDown(_s.KbBuildingEdit);
                PreciseSleep(KeyHoldMs);
                InputSim.KeyUp(_s.KbBuildingEdit);
                PreciseSleep(delay);
            }

            if (!string.IsNullOrEmpty(_s.KbSelectBuildingEdit))
            {
                InputSim.KeyDown(_s.KbSelectBuildingEdit);
                PreciseSleep(KeyHoldMs);
                InputSim.KeyUp(_s.KbSelectBuildingEdit);
                PreciseSleep(delay);
            }

            edits++;
        }

        HandlePullout();
    }

}

internal static class JsonExt
{
    public static bool TryGetProp(this JsonElement el, string name, out string value)
    {
        if (el.TryGetProperty(name, out var prop) && prop.GetString() is { } s)
        { value = s; return true; }
        value = "";
        return false;
    }
}
