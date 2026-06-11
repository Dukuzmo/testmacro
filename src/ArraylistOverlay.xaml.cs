using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Interop;

namespace Macronic;

public partial class ArraylistOverlay : Window
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    private const int  GWL_EXSTYLE       = -20;
    private const int  WS_EX_LAYERED     = 0x00080000;
    private const int  WS_EX_TRANSPARENT = 0x00000020;
    private const int  WS_EX_TOOLWINDOW  = 0x00000080;
    private const int  WS_EX_APPWINDOW   = 0x00040000;
    private const uint WDA_NONE              = 0x00000000;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    private static readonly FontFamily BebasNeue  = new("pack://application:,,,/Fonts/#Bebas Neue");
    private static readonly FontFamily IBMPlexMono = new("pack://application:,,,/Fonts/#IBM Plex Mono");

    public ArraylistOverlay()
    {
        InitializeComponent();

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowDisplayAffinity(hwnd, WDA_NONE);
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            ex |=  WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW;
            ex &= ~WS_EX_APPWINDOW;
            SetWindowLong(hwnd, GWL_EXSTYLE, ex);
        };
    }

    public void SetProof(bool active)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        SetWindowDisplayAffinity(hwnd, active ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE);
    }

    public void UpdateItems(List<(string Name, string Detail)> items, string position, bool enabled)
    {
        if (!enabled || items.Count == 0)
        {
            if (IsVisible) Hide();
            return;
        }

        ItemStack.Children.Clear();

        bool rightSide  = position.Contains("Right");
        bool bottomSide = position.Contains("Bottom");

        var ordered = bottomSide ? items : items.AsEnumerable().Reverse().ToList();

        ItemStack.HorizontalAlignment = rightSide
            ? HorizontalAlignment.Right
            : HorizontalAlignment.Left;

        HorizontalAlignment contentAlign = rightSide
            ? HorizontalAlignment.Right
            : HorizontalAlignment.Left;

        foreach (var (name, detail) in ordered)
        {
            var nameBlock = new TextBlock
            {
                Text                  = name,
                FontFamily            = BebasNeue,
                FontSize              = 12,
                LineHeight            = 12,
                LineStackingStrategy  = LineStackingStrategy.BlockLineHeight,
                Foreground            = new SolidColorBrush(Color.FromRgb(0xf5, 0xf5, 0xf5)),
                VerticalAlignment     = VerticalAlignment.Center
            };

            var detailBlock = new TextBlock
            {
                Text                  = detail,
                FontFamily            = IBMPlexMono,
                FontSize              = 8,
                LineHeight            = 8,
                LineStackingStrategy  = LineStackingStrategy.BlockLineHeight,
                Foreground            = new SolidColorBrush(Color.FromRgb(0x6a, 0x6a, 0x6a)),
                VerticalAlignment     = VerticalAlignment.Center,
                Margin                = new Thickness(rightSide ? 0 : 4, 0, rightSide ? 4 : 0, 0)
            };

            var row = new StackPanel
            {
                Orientation         = Orientation.Horizontal,
                HorizontalAlignment = contentAlign
            };

            if (rightSide)
            {
                row.Children.Add(detailBlock);
                row.Children.Add(nameBlock);
            }
            else
            {
                row.Children.Add(nameBlock);
                row.Children.Add(detailBlock);
            }

            var item = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xee, 0x0a, 0x0a, 0x0a)),
                Padding    = new Thickness(10, 4, 10, 4),
                Margin     = new Thickness(0),
                Child      = row
            };

            ItemStack.Children.Add(item);
        }

        UpdateLayout();
        RepositionWindow(position);

        if (!IsVisible) Show();
    }

    private void RepositionWindow(string position)
    {
        double sw     = SystemParameters.PrimaryScreenWidth;
        double sh     = SystemParameters.PrimaryScreenHeight;
        double margin = 20;

        Left = position.Contains("Right")
            ? sw - ActualWidth - margin
            : margin;

        Top = position.Contains("Bottom")
            ? sh - ActualHeight - margin
            : margin;
    }
}
