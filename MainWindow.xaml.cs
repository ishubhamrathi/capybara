
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Text.Json;
using System.IO;
using Windows.System;
using System.Runtime.InteropServices;
using WinRT.Interop;
using CapyBara.Controls;
using CapyBara.Models;
using CapyBara.Pages;
using CapyBara.Services;

namespace CapyBara;

public sealed partial class MainWindow : Window
{
    private readonly List<BrowserTab> _tabs = [];
    private readonly Dictionary<BrowserTab, CancellationTokenSource> _suggestionCancellations = [];
    private CancellationTokenSource? _addressSuggestionCancellation;
    private BrowserTab? _activeTab;

    private const string HomeUrl = "https://www.google.com";
    private readonly SearchSuggestionService _suggestionService = new(new HttpSearchSuggestionProvider());
    private readonly SearchNavigationService _searchNavigationService = new();

    private const int GWL_STYLE = -16;

    private const int WS_CAPTION = 0x00C00000;
    private const int WS_THICKFRAME = 0x00040000;
    private const int WS_MINIMIZEBOX = 0x00020000;
    private const int WS_MAXIMIZEBOX = 0x00010000;
    private const int WS_SYSMENU = 0x00080000;

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const int SW_MINIMIZE = 6;

    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int WM_NCCALCSIZE = 0x0083;
    private const int WM_NCHITTEST = 0x0084;
    private const int WM_NCPAINT = 0x0085;
    private const int WM_ACTIVATE = 0x0006;
    private const int WM_SETFOCUS = 0x0007;
    private const int WM_NCACTIVATE = 0x0086;
    private const int WM_SETTINGCHANGE = 0x001A;
    private const int WM_THEMECHANGED = 0x031A;
    private const int SM_CXSIZEFRAME = 32;
    private const int SM_CYSIZEFRAME = 33;
    private const int SM_CXPADDEDBORDER = 92;
    private const int MinimumResizeHitTargetDip = 8;
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private static readonly UIntPtr WindowSubclassId = new(1);
    // DWMWA_BORDER_COLOR takes a COLORREF (0x00BBGGRR). Use the browser's
    // dark surface color so DWM does not substitute the active accent color.
    private const int DarkWindowBorderColor = 0x00242120;
    private static readonly string ChromeDiagnosticPath = Path.Combine(
        Path.GetTempPath(), "CapyBara-window-chrome.log");
    private static readonly Dictionary<int, DateTime> LastHitTestLogTimes = [];
    private static bool _loggedFirstHitTest;

    private const int HTLEFT = 10;
    private const int HTRIGHT = 11;
    private const int HTTOP = 12;
    private const int HTTOPLEFT = 13;
    private const int HTTOPRIGHT = 14;
    private const int HTBOTTOM = 15;
    private const int HTBOTTOMLEFT = 16;
    private const int HTBOTTOMRIGHT = 17;
    private const int HTCAPTION = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr WindowSubclassProc(
        IntPtr hWnd,
        uint message,
        UIntPtr wParam,
        IntPtr lParam,
        UIntPtr subclassId,
        IntPtr referenceData);

    private WindowSubclassProc? _windowSubclassProc;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        ref int pvAttribute,
        int cbAttribute);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        out int pvAttribute,
        int cbAttribute);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        out NativeRect pvAttribute,
        int cbAttribute);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(
        IntPtr hWnd,
        WindowSubclassProc callback,
        UIntPtr subclassId,
        IntPtr referenceData);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(
        IntPtr hWnd,
        WindowSubclassProc callback,
        UIntPtr subclassId);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(
        IntPtr hWnd,
        uint message,
        UIntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    private void ApplyWindowBorderColor()
    {
        // Set a stable explicit dark color; COLOR_NONE lets Windows choose a
        // theme/accent-dependent border that can become white on activation.
        SetDwmBorderColor("startup");

        int queriedBorderColor = 0;
        int borderColorQueryResult = DwmGetWindowAttribute(
            _windowHandle,
            DWMWA_BORDER_COLOR,
            out queriedBorderColor,
            sizeof(int));
        LogChromeDiagnostic($"DWM_BORDER_COLOR_QUERY hwnd=0x{_windowHandle.ToInt64():X} HRESULT=0x{borderColorQueryResult:X8} value=0x{queriedBorderColor:X8}");

        int darkFrame = 1;
        int darkModeResult = DwmSetWindowAttribute(
            _windowHandle,
            DWMWA_USE_IMMERSIVE_DARK_MODE,
            ref darkFrame,
            sizeof(int));
        LogChromeDiagnostic($"DWM_USE_IMMERSIVE_DARK_MODE hwnd=0x{_windowHandle.ToInt64():X} value={darkFrame} HRESULT=0x{darkModeResult:X8}");
        if (darkModeResult < 0)
            Debug.WriteLine($"DwmSetWindowAttribute(DWMWA_USE_IMMERSIVE_DARK_MODE) failed: 0x{darkModeResult:X8}");

        int visibleFrameThickness = 0;
        int frameThicknessQueryResult = DwmGetWindowAttribute(_windowHandle, 37, out visibleFrameThickness, sizeof(int));
        int boundsQueryResult = DwmGetWindowAttribute(_windowHandle, DWMWA_EXTENDED_FRAME_BOUNDS, out NativeRect visualBounds, Marshal.SizeOf<NativeRect>());
        LogChromeDiagnostic($"DWM_STATE visibleBorderResult=0x{frameThicknessQueryResult:X8} visibleBorderPx={visibleFrameThickness} visualBoundsResult=0x{boundsQueryResult:X8} visualBounds={FormatRect(visualBounds)}");
    }

    private void SetDwmBorderColor(string reason)
    {
        int color = DarkWindowBorderColor;
        int result = DwmSetWindowAttribute(
            _windowHandle,
            DWMWA_BORDER_COLOR,
            ref color,
            sizeof(int));

        LogChromeDiagnostic($"DWM_BORDER_COLOR hwnd=0x{_windowHandle.ToInt64():X} reason={reason} color=0x{color:X8} HRESULT=0x{result:X8}");
        if (result < 0)
            Debug.WriteLine($"DwmSetWindowAttribute(DWMWA_BORDER_COLOR) failed: 0x{result:X8}");
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(
        IntPtr hWnd,
        int nIndex,
        IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(
        IntPtr hWnd,
        int nIndex,
        int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int X,
        int Y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(
        IntPtr hWnd,
        int nCmdShow);

    private IntPtr _windowHandle;

    private void ConfigureCustomCaptionBar()
    {
        _windowHandle = WindowNative.GetWindowHandle(this);

        int style = ReadWindowStyle(_windowHandle);
        int originalStyle = style;

        // Keep the native resize frame and system capabilities. The caption
        // style alone is removed because this window supplies its own buttons.
        style &= ~WS_CAPTION;

        // Keep normal resizing and maximize/minimize support.
        style |= WS_THICKFRAME
               | WS_MINIMIZEBOX
               | WS_MAXIMIZEBOX
               | WS_SYSMENU;

        WriteWindowStyle(_windowHandle, style);
        int appliedStyle = ReadWindowStyle(_windowHandle);
        LogChromeDiagnostic($"WINDOW_STYLE hwnd=0x{_windowHandle.ToInt64():X} process={Environment.ProcessPath} original=0x{originalStyle:X8} requested=0x{style:X8} applied=0x{appliedStyle:X8} thickFrame={(appliedStyle & WS_THICKFRAME) != 0}");

        if (!SetWindowPos(
            _windowHandle,
            IntPtr.Zero,
            0, 0, 0, 0,
            SWP_NOMOVE
            | SWP_NOSIZE
            | SWP_NOZORDER
            | SWP_FRAMECHANGED))
        {
            int error = Marshal.GetLastPInvokeError();
            LogChromeDiagnostic($"FRAME_CHANGED hwnd=0x{_windowHandle.ToInt64():X} result=failure win32={error}");
            throw new System.ComponentModel.Win32Exception(error, "Unable to refresh the window frame.");
        }

        LogChromeDiagnostic($"FRAME_CHANGED hwnd=0x{_windowHandle.ToInt64():X} result=success");
    }

    private static int ReadWindowStyle(IntPtr hWnd)
    {
        Marshal.SetLastPInvokeError(0);
        IntPtr value = IntPtr.Size == 8
            ? GetWindowLongPtr64(hWnd, GWL_STYLE)
            : new IntPtr(GetWindowLong32(hWnd, GWL_STYLE));
        int error = Marshal.GetLastPInvokeError();
        if (value == IntPtr.Zero && error != 0)
            throw new System.ComponentModel.Win32Exception(error, "Unable to read the window style.");

        return unchecked((int)value.ToInt64());
    }

    private static void WriteWindowStyle(IntPtr hWnd, int style)
    {
        Marshal.SetLastPInvokeError(0);
        IntPtr previous = IntPtr.Size == 8
            ? SetWindowLongPtr64(hWnd, GWL_STYLE, new IntPtr(style))
            : new IntPtr(SetWindowLong32(hWnd, GWL_STYLE, style));
        int error = Marshal.GetLastPInvokeError();
        if (previous == IntPtr.Zero && error != 0)
            throw new System.ComponentModel.Win32Exception(error, "Unable to configure the custom caption style.");
    }

    private void InstallCustomFrameHandling()
    {
        _windowSubclassProc = WindowSubclassProcedure;
        if (!SetWindowSubclass(_windowHandle, _windowSubclassProc, WindowSubclassId, IntPtr.Zero))
        {
            int error = Marshal.GetLastPInvokeError();
            LogChromeDiagnostic($"SUBCLASS_INSTALL_FAILED hwnd=0x{_windowHandle.ToInt64():X} win32={error}");
            throw new System.ComponentModel.Win32Exception(
                error,
                "Unable to install custom frame handling.");
        }
        LogChromeDiagnostic($"SUBCLASS_INSTALLED hwnd=0x{_windowHandle.ToInt64():X} id={WindowSubclassId} callback={_windowSubclassProc.Method.Name}");

        // Recalculate the frame after installing the subclass so Windows
        // immediately lays out the client area over the old top non-client band.
        if (!SetWindowPos(
            _windowHandle,
            IntPtr.Zero,
            0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED))
        {
            int error = Marshal.GetLastPInvokeError();
            LogChromeDiagnostic($"FRAME_RECALC_FAILED hwnd=0x{_windowHandle.ToInt64():X} win32={error}");
            RemoveWindowSubclass(_windowHandle, _windowSubclassProc, WindowSubclassId);
            _windowSubclassProc = null;
            throw new System.ComponentModel.Win32Exception(error, "Unable to apply the custom frame layout.");
        }
    }

    private IntPtr WindowSubclassProcedure(
        IntPtr hWnd,
        uint message,
        UIntPtr wParam,
        IntPtr lParam,
        UIntPtr subclassId,
        IntPtr referenceData)
    {
        try
        {
            return ProcessWindowMessage(hWnd, message, wParam, lParam);
        }
        catch (Exception ex)
        {
            // Never allow a managed exception to escape through the native
            // subclass callback. Preserve WinUI behavior for the failed case.
            LogChromeDiagnostic($"WINDOW_SUBCLASS_EXCEPTION hwnd=0x{hWnd.ToInt64():X} message=0x{message:X4} error={ex}");
            return DefSubclassProc(hWnd, message, wParam, lParam);
        }
    }

    private IntPtr ProcessWindowMessage(
        IntPtr hWnd,
        uint message,
        UIntPtr wParam,
        IntPtr lParam)
    {
        if (message == WM_NCHITTEST && !_loggedFirstHitTest)
        {
            _loggedFirstHitTest = true;
            LogChromeDiagnostic($"WM_NCHITTEST_RECEIVED hwnd=0x{hWnd.ToInt64():X}");
        }

        if (message == WM_NCACTIVATE || message == WM_ACTIVATE || message == WM_SETFOCUS)
        {
            LogChromeDiagnostic(
                $"ACTIVATION_MESSAGE hwnd=0x{hWnd.ToInt64():X} message=0x{message:X4} wParam=0x{wParam.ToUInt64():X} lParam=0x{lParam.ToInt64():X}");
        }

        if (message == WM_NCPAINT)
            LogHitTestAtMostTwicePerSecond(1001, $"WM_NCPAINT_FORWARDED hwnd=0x{hWnd.ToInt64():X} region=0x{wParam.ToUInt64():X}");

        if (message == WM_THEMECHANGED || message == WM_SETTINGCHANGE)
        {
            IntPtr result = DefSubclassProc(hWnd, message, wParam, lParam);
            SetDwmBorderColor(message == WM_THEMECHANGED ? "theme-change" : "setting-change");
            return result;
        }

        // WM_NCCALCSIZE has a legacy RECT form when wParam is FALSE. Leave
        // that form to the chained WinUI/default procedure; returning zero
        // only removes the standard frame for the NCCALCSIZE_PARAMS form.
        if (message == WM_NCCALCSIZE && wParam != UIntPtr.Zero && lParam != IntPtr.Zero)
        {
            if (IsZoomed(hWnd))
            {
                IntPtr monitor = MonitorFromWindow(hWnd, MONITOR_DEFAULTTONEAREST);
                var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
                if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
                {
                    // NCCALCSIZE_PARAMS begins with the proposed client RECT.
                    Marshal.StructureToPtr(info.Work, lParam, false);
                }
            }

            // Use the full window bounds as the client area. The resize edges
            // are restored in WM_NCHITTEST below, avoiding an exposed top frame.
            return IntPtr.Zero;
        }

        if (message == WM_NCHITTEST)
        {
            if (IsZoomed(hWnd) || !GetWindowRect(hWnd, out NativeRect bounds))
                return DefSubclassProc(hWnd, message, wParam, lParam);

            long packedPoint = lParam.ToInt64();
            int x = unchecked((short)(packedPoint & 0xFFFF));
            int y = unchecked((short)((packedPoint >> 16) & 0xFFFF));
            uint dpi = GetDpiForWindow(hWnd);
            if (dpi == 0)
                dpi = 96;

            // Keep the system resize thickness, with an 8-DIP minimum target
            // for the custom frame. These remain hit-test-only regions.
            int minimumHitTarget = (MinimumResizeHitTargetDip * (int)dpi + 95) / 96;
            int resizeX = Math.Max(minimumHitTarget,
                GetSystemMetricsForDpi(SM_CXSIZEFRAME, dpi) + GetSystemMetricsForDpi(SM_CXPADDEDBORDER, dpi));
            int resizeY = Math.Max(minimumHitTarget,
                GetSystemMetricsForDpi(SM_CYSIZEFRAME, dpi) + GetSystemMetricsForDpi(SM_CXPADDEDBORDER, dpi));

            NativeRect visualBounds = bounds;
            int visualBoundsResult = DwmGetWindowAttribute(
                hWnd,
                DWMWA_EXTENDED_FRAME_BOUNDS,
                out NativeRect dwmVisualBounds,
                Marshal.SizeOf<NativeRect>());
            if (visualBoundsResult == 0 &&
                dwmVisualBounds.Right > dwmVisualBounds.Left &&
                dwmVisualBounds.Bottom > dwmVisualBounds.Top)
            {
                visualBounds = dwmVisualBounds;
            }

            // GetWindowRect includes the invisible resize frame while DWM's
            // extended bounds track the visible edge. Anchor the 8-DIP target
            // to that visible edge, but constrain it to the actual window so
            // points elsewhere on the desktop cannot become resize hits.
            bool insideWindow = x >= bounds.Left && x < bounds.Right &&
                                y >= bounds.Top && y < bounds.Bottom;
            bool left = insideWindow && x < visualBounds.Left + resizeX;
            bool right = insideWindow && x >= visualBounds.Right - resizeX;
            bool top = insideWindow && y < visualBounds.Top + resizeY;
            bool bottom = insideWindow && y >= visualBounds.Bottom - resizeY;

            int hitTest = top && left ? HTTOPLEFT
                : top && right ? HTTOPRIGHT
                : bottom && left ? HTBOTTOMLEFT
                : bottom && right ? HTBOTTOMRIGHT
                : left ? HTLEFT
                : right ? HTRIGHT
                : top ? HTTOP
                : bottom ? HTBOTTOM
                : 0;

            if (hitTest != 0)
            {
                LogHitTestAtMostTwicePerSecond(
                    hitTest,
                    $"WM_NCHITTEST_EDGE hwnd=0x{hWnd.ToInt64():X} point=({x},{y}) bounds={FormatRect(bounds)} visualBounds={FormatRect(visualBounds)} visualBoundsResult=0x{visualBoundsResult:X8} dpi={dpi} target=({resizeX},{resizeY}) hit={hitTest}");
                return new IntPtr(hitTest);
            }

            // Resize zones are handled above. Let WinUI's SetTitleBar hit
            // testing identify the designated empty drag element and return
            // HTCAPTION; this preserves its pointer and DPI handling.
            IntPtr forwardedResult = DefSubclassProc(hWnd, message, wParam, lParam);
            if (forwardedResult.ToInt32() == HTCAPTION)
            {
                LogHitTestAtMostTwicePerSecond(
                    HTCAPTION,
                    $"WM_NCHITTEST_TITLEBAR hwnd=0x{hWnd.ToInt64():X} point=({x},{y}) dpi={dpi} result={HTCAPTION}");
                return forwardedResult;
            }

            int nearestEdge = Math.Min(
                Math.Min(Math.Abs(x - bounds.Left), Math.Abs(bounds.Right - 1 - x)),
                Math.Min(Math.Abs(y - bounds.Top), Math.Abs(bounds.Bottom - 1 - y)));
            if (nearestEdge <= Math.Max(resizeX, resizeY) * 2)
                LogHitTestAtMostTwicePerSecond(
                    100 + forwardedResult.ToInt32(),
                    $"WM_NCHITTEST_FORWARDED hwnd=0x{hWnd.ToInt64():X} point=({x},{y}) bounds={FormatRect(bounds)} dpi={dpi} nearestEdge={nearestEdge} result={forwardedResult.ToInt64()}");
            return forwardedResult;
        }

        // Preserve WinUI's title-bar drag hit testing and normal client input.
        return DefSubclassProc(hWnd, message, wParam, lParam);
    }

    private void RemoveCustomFrameHandling()
    {
        if (_windowSubclassProc is null)
            return;

        if (!RemoveWindowSubclass(_windowHandle, _windowSubclassProc, WindowSubclassId))
            Debug.WriteLine($"RemoveWindowSubclass failed: {Marshal.GetLastPInvokeError()}");
        else
            LogChromeDiagnostic($"SUBCLASS_REMOVED hwnd=0x{_windowHandle.ToInt64():X}");

        _windowSubclassProc = null;
    }

    private static string FormatRect(NativeRect rect) =>
        $"[{rect.Left},{rect.Top},{rect.Right},{rect.Bottom}]";

    private static void LogHitTestAtMostTwicePerSecond(int key, string message)
    {
        DateTime now = DateTime.UtcNow;
        if (LastHitTestLogTimes.TryGetValue(key, out DateTime last) &&
            now - last < TimeSpan.FromMilliseconds(500))
            return;

        LastHitTestLogTimes[key] = now;
        LogChromeDiagnostic(message);
    }

    private static void LogChromeDiagnostic(string message)
    {
#if DEBUG
        string line = $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}";
        Debug.Write(line);
        try
        {
            File.AppendAllText(ChromeDiagnosticPath, line);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Chrome diagnostic log write failed: {ex.Message}");
        }
#endif
    }

    private void MinimizeWindowButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        ShowWindow(_windowHandle, SW_MINIMIZE);
    }

    private void MaximizeWindowButton_Click(
    object sender,
    RoutedEventArgs e)
    {
        if (AppWindow.Presenter is not OverlappedPresenter presenter)
            return;

        if (presenter.State == OverlappedPresenterState.Maximized)
        {
            presenter.Restore();
        }
        else
        {
            presenter.Maximize();
        }
        UpdateMaximizeButton();
    }

    private void UpdateMaximizeButton()
    {
        bool maximized = AppWindow.Presenter is OverlappedPresenter presenter &&
                         presenter.State == OverlappedPresenterState.Maximized;
        BrowserToolbar.SetMaximized(maximized);
    }

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidPresenterChange)
        {
            UpdateMaximizeButton();
            SetDwmBorderColor("presenter-change");
        }
    }

    private void CloseWindowButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        Close();
    }
    public MainWindow()
    {
        InitializeComponent();

        BrowserToolbar.HomeRequested += HomeButton_Click;
        BrowserToolbar.BackRequested += BackButton_Click;
        BrowserToolbar.ForwardRequested += ForwardButton_Click;
        BrowserToolbar.ReloadRequested += ReloadButton_Click;
        BrowserToolbar.NewTabRequested += NewTabButton_Click;
        BrowserToolbar.MinimizeRequested += MinimizeWindowButton_Click;
        BrowserToolbar.MaximizeRequested += MaximizeWindowButton_Click;
        BrowserToolbar.CloseRequested += CloseWindowButton_Click;
        BrowserToolbar.AddressSuggestionRequested += AddressBar_SuggestionRequested;
        BrowserToolbar.AddressQuerySubmitted += AddressBar_QuerySubmitted;
        BrowserToolbar.AddressSearchEngineChanged += AddressSearchEngine_Changed;
        BrowserToolbar.AddressGotFocus += AddressBar_GotFocus;
        BrowserToolbar.AddressLostFocus += AddressBar_LostFocus;
        BrowserToolbar.SetSearchEngine(SearchEngineSettings.Current);

        ExtendsContentIntoTitleBar = true;
        // The toolbar exposes only its empty spacer as a WinUI title-bar area.
        // This owns dragging; the native subclass below handles resize edges.
        SetTitleBar(BrowserToolbar.DragArea);
        ConfigureCustomCaptionBar();
        InstallCustomFrameHandling();
        ApplyWindowBorderColor();
        AppWindow.Changed += AppWindow_Changed;
        Closed += (_, _) =>
        {
            AppWindow.Changed -= AppWindow_Changed;
            RemoveCustomFrameHandling();
        };
        UpdateMaximizeButton();
        try
        {
            AppWindow.SetIcon("Assets/CapyBaraAppIcon.ico");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Window icon setup failed: {ex.Message}");
        }

        _ = CreateTabAsync();
    }

    private async Task CreateTabAsync(
        string? url = null,
        bool focusAddressBar = false)
    {
        var tab = new BrowserTab();

        tab.SelectButton.Click += (_, _) => ActivateTab(tab);
        tab.CloseButton.Click += (_, _) => CloseTab(tab);
        BrowserToolbar.AddTab(tab);
        BrowserHost.Children.Add(tab.View);
        _tabs.Add(tab);

        ActivateTab(tab);

        tab.View.NavigationCompleted += (_, args) =>
        {
            LogChromeDiagnostic(
                $"BROWSER_NAVIGATION_COMPLETED source={tab.View.Source} success={args.IsSuccess} webError={args.WebErrorStatus}");
            if (NewTabPage.IsSource(tab.View.CoreWebView2?.Source))
                tab.IsNewTabPage = true;

            if (tab.IsNewTabPage)
            {
                try
                {
                    tab.View.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(new
                    {
                        type = "settings",
                        engine = SearchEngineSettings.Current.Id,
                        engines = SearchEngineSettings.AvailableEngines.Select(engine => new
                        {
                            id = engine.Id,
                            searchUrlTemplate = engine.SearchUrlTemplate
                        })
                    }));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"New tab settings initialization failed: {ex.Message}");
                }
            }
            if (_activeTab == tab &&
                !BrowserToolbar.IsAddressFocused)
            {
                string? source = tab.View.Source?.ToString();
                BrowserToolbar.AddressText = string.Equals(
                    source,
                    "about:blank",
                    StringComparison.OrdinalIgnoreCase)
                    ? string.Empty
                    : GetCompactUrl(source);
            }

            UpdateNavigationButtons();
            UpdateTabTooltip(tab);
            UpdateFavicon(tab);
        };

        try
        {
            await tab.View.EnsureCoreWebView2Async();

            CoreWebView2 core = tab.View.CoreWebView2;
            NewTabPage.Configure(core);

            tab.View.NavigationStarting += (_, args) =>
            {
                tab.IsNewTabPage = NewTabPage.IsSource(args.Uri);
            };

            core.WebMessageReceived += async (_, args) =>
            {
                // Keep page messaging scoped to the built-in virtual host.
                if (tab.IsNewTabPage || NewTabPage.IsSource(args.Source))
                {
                    Debug.WriteLine($"New Tab WebMessageReceived from {args.Source}: {args.WebMessageAsJson}");
                    await HandleNewTabMessageAsync(tab, core, args.WebMessageAsJson);
                }
            };

            core.DocumentTitleChanged += (_, _) =>
            {
                tab.Title = string.IsNullOrWhiteSpace(core.DocumentTitle)
                    ? "New Tab"
                    : core.DocumentTitle;

                tab.TitleText.Text = tab.Title;
                UpdateTabTooltip(tab);
            };

            core.FaviconChanged += (_, _) => UpdateFavicon(tab);

            UpdateFavicon(tab);
            UpdateTabTooltip(tab);

            if (!string.IsNullOrWhiteSpace(url) &&
                Uri.TryCreate(url, UriKind.Absolute, out var target))
            {
                tab.View.Source = target;
            }
            else
            {
                tab.IsNewTabPage = true;
                core.Navigate(NewTabPage.Source);
            }

            if (focusAddressBar)
            {
                BrowserToolbar.AddressText = string.Empty;
                BrowserToolbar.FocusAddressBar();
            }
        }
        catch (Exception ex)
        {
            tab.Title = "Browser initialization failed";
            tab.TitleText.Text = tab.Title;
            UpdateTabTooltip(tab);

            LogChromeDiagnostic($"BROWSER_INITIALIZATION_FAILED source={tab.View.Source} error={ex}");
            Debug.WriteLine($"Browser initialization failed: {ex}");
        }
    }

    private async Task HandleNewTabMessageAsync(
        BrowserTab tab,
        CoreWebView2 core,
        string messageJson)
    {
        try
        {
            using JsonDocument message = JsonDocument.Parse(messageJson);
            JsonElement root = message.RootElement;
            if (!root.TryGetProperty("type", out JsonElement type))
            {
                return;
            }

            string messageType = type.GetString() ?? string.Empty;
            if (messageType == "engine" && root.TryGetProperty("engine", out JsonElement selectedEngine))
            {
                CancelSuggestionRequest(tab);
                BrowserToolbar.SetSearchEngine(SearchEngineSettings.Select(selectedEngine.GetString()));
                return;
            }

            if (messageType == "search" && root.TryGetProperty("query", out JsonElement searchQuery))
            {
                CancelSuggestionRequest(tab);
                string queryText = searchQuery.GetString() ?? string.Empty;
                string engineId = root.TryGetProperty("engine", out JsonElement searchEngine)
                    ? searchEngine.GetString() ?? SearchEngineSettings.Current.Id
                    : SearchEngineSettings.Current.Id;
                BrowserToolbar.SetSearchEngine(SearchEngineSettings.Select(engineId));
                if (!string.IsNullOrWhiteSpace(queryText))
                    tab.View.Source = new Uri(_searchNavigationService.CreateSearchUrl(queryText, engineId));
                return;
            }

            if (messageType != "suggest" ||
                !root.TryGetProperty("id", out JsonElement id) ||
                !root.TryGetProperty("engine", out JsonElement engine) ||
                !root.TryGetProperty("query", out JsonElement query))
                return;

            CancelSuggestionRequest(tab);
            var cancellation = new CancellationTokenSource();
            _suggestionCancellations[tab] = cancellation;
            IReadOnlyList<SearchSuggestion> results;
            try
            {
                results = await _suggestionService.GetSuggestionsAsync(
                    engine.GetString(), query.GetString() ?? string.Empty, cancellation.Token);
            }
            finally
            {
                if (_suggestionCancellations.TryGetValue(tab, out CancellationTokenSource? active) &&
                    ReferenceEquals(active, cancellation))
                {
                    _suggestionCancellations.Remove(tab);
                }
                cancellation.Dispose();
            }

            if ((!tab.IsNewTabPage && !NewTabPage.IsSource(core.Source)) || tab.View.CoreWebView2 != core)
                return;

            string responseJson = JsonSerializer.Serialize(new
            {
                type = "suggestions",
                id = id.GetInt32(),
                items = results.Select(item => item.Text)
            });
            core.PostWebMessageAsJson(responseJson);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            Debug.WriteLine($"Search suggestion handling failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Search suggestion request failed: {ex.Message}");
        }
    }

    private void CancelSuggestionRequest(BrowserTab tab)
    {
        if (_suggestionCancellations.Remove(tab, out CancellationTokenSource? cancellation))
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }
    }

    private void UpdateFavicon(BrowserTab tab)
    {
        try
        {
            string? faviconUri = tab.View.CoreWebView2?.FaviconUri;

            if (Uri.TryCreate(faviconUri, UriKind.Absolute, out var uri))
            {
                tab.FaviconImage.Source = new BitmapImage(uri);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Favicon update failed: {ex.Message}");
        }
    }

    private void ActivateTab(BrowserTab tab)
    {
        if (!_tabs.Contains(tab))
            return;

        _activeTab = tab;

        foreach (var item in _tabs)
        {
            bool active = item == tab;

            item.View.Visibility = active
                ? Visibility.Visible
                : Visibility.Collapsed;

            BrowserToolbar.SetTabActive(item, active);
        }

        BrowserToolbar.AddressText = GetCompactUrl(tab.View.Source?.ToString());
        UpdateNavigationButtons();
        UpdateTabTooltip(tab);
    }

    private void CloseTab(BrowserTab tab)
    {
        int index = _tabs.IndexOf(tab);

        if (index < 0)
            return;

        bool wasActive = tab == _activeTab;

        _tabs.Remove(tab);
        CancelSuggestionRequest(tab);
        BrowserToolbar.RemoveTab(tab);
        BrowserHost.Children.Remove(tab.View);

        tab.View.Close();

        if (_tabs.Count == 0)
        {
            _activeTab = null;
            _ = CreateTabAsync();
        }
        else if (wasActive)
        {
            ActivateTab(_tabs[Math.Min(index, _tabs.Count - 1)]);
        }

        UpdateNavigationButtons();
    }

    private void UpdateTabTooltip(BrowserTab tab)
    {
        string url = tab.View.Source?.ToString() ?? "New Tab";

        ToolTipService.SetToolTip(
            tab.SelectButton,
            $"{tab.Title}\n{url}");

        ToolTipService.SetToolTip(
            tab.CloseButton,
            $"Close {tab.Title}");
    }

    private static string GetCompactUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return "";

        if (NewTabPage.IsSource(url))
            return string.Empty;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return url;

        if (uri.Scheme == "about" && uri.AbsolutePath == "blank")
            return string.Empty;

        if (uri.Scheme != Uri.UriSchemeHttp &&
            uri.Scheme != Uri.UriSchemeHttps)
        {
            return url;
        }

        string host = uri.Host;

        if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            host = host[4..];

        string result = host;

        if (!uri.IsDefaultPort)
            result += $":{uri.Port}";

        if (uri.AbsolutePath != "/")
            result += uri.AbsolutePath;

        result += uri.Query;
        result += uri.Fragment;

        return result;
    }

    private async Task NavigateAsync(string? submittedText = null)
    {
        if (_activeTab is null)
            return;

        string input = (submittedText ?? BrowserToolbar.AddressText).Trim();

        if (string.IsNullOrWhiteSpace(input))
            return;

        string url;

        if (Uri.TryCreate(input, UriKind.Absolute, out var parsed) &&
            (parsed.Scheme == Uri.UriSchemeHttp ||
             parsed.Scheme == Uri.UriSchemeHttps))
        {
            url = input;
        }
        else if (!input.Contains(' ') && input.Contains('.'))
        {
            url = "https://" + input;
        }
        else
        {
            url = _searchNavigationService.CreateSearchUrl(input);
        }

        try
        {
            await _activeTab.View.EnsureCoreWebView2Async();
            _activeTab.View.Source = new Uri(url);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Navigation failed: {ex}");
        }
    }

    private void UpdateNavigationButtons()
    {
        BrowserToolbar.SetNavigationState(
            _activeTab?.View.CanGoBack == true,
            _activeTab?.View.CanGoForward == true,
            _activeTab is not null);
    }

    private async void HomeButton_Click(
        object sender, RoutedEventArgs e)
    {
        if (_activeTab is null)
            return;

        await _activeTab.View.EnsureCoreWebView2Async();
        _activeTab.View.Source = new Uri(HomeUrl);
    }

    private async void NewTabButton_Click(
        object sender, RoutedEventArgs e)
    {
        await CreateTabAsync();
    }

    private async void AddressBar_QuerySubmitted(string query)
    {
        CancelAddressSuggestionRequest();
        await NavigateAsync(query);
        BrowserHost.Focus(FocusState.Programmatic);
    }

    private void AddressSearchEngine_Changed(string engineId)
    {
        CancelAddressSuggestionRequest();
        BrowserToolbar.ClearAddressSuggestions();
        BrowserToolbar.SetSearchEngine(SearchEngineSettings.Select(engineId));
        if (BrowserToolbar.IsAddressFocused)
            AddressBar_SuggestionRequested(BrowserToolbar.AddressText);
    }

    private async void AddressBar_SuggestionRequested(string input)
    {
        CancelAddressSuggestionRequest();
        string query = input.Trim();
        if (query.Length < 2)
        {
            BrowserToolbar.ClearAddressSuggestions();
            return;
        }

        var cancellation = new CancellationTokenSource();
        _addressSuggestionCancellation = cancellation;
        try
        {
            await Task.Delay(180, cancellation.Token);
            IReadOnlyList<SearchSuggestion> suggestions = await _suggestionService.GetSuggestionsAsync(
                SearchEngineSettings.Current.Id,
                query,
                cancellation.Token);

            if (!cancellation.IsCancellationRequested &&
                ReferenceEquals(_addressSuggestionCancellation, cancellation) &&
                BrowserToolbar.IsAddressFocused &&
                string.Equals(BrowserToolbar.AddressText, input, StringComparison.Ordinal))
            {
                BrowserToolbar.SetAddressSuggestions(suggestions);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Address suggestions failed: {ex.Message}");
            if (ReferenceEquals(_addressSuggestionCancellation, cancellation))
                BrowserToolbar.ClearAddressSuggestions();
        }
        finally
        {
            if (ReferenceEquals(_addressSuggestionCancellation, cancellation))
                _addressSuggestionCancellation = null;
            cancellation.Dispose();
        }
    }

    private void CancelAddressSuggestionRequest()
    {
        CancellationTokenSource? cancellation = _addressSuggestionCancellation;
        _addressSuggestionCancellation = null;
        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    private void AddressBar_GotFocus(
        object sender, RoutedEventArgs e)
    {
        string? source = _activeTab?.View.Source?.ToString();
        if (!string.IsNullOrWhiteSpace(source) &&
            !string.Equals(source, "about:blank", StringComparison.OrdinalIgnoreCase))
        {
            BrowserToolbar.AddressText = source;
            BrowserToolbar.FocusAddressBar();
        }
    }

    private void AddressBar_LostFocus(
        object sender, RoutedEventArgs e)
    {
        CancelAddressSuggestionRequest();
        Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()?.TryEnqueue(() =>
        {
            // Let AutoSuggestBox finish a mouse selection before collapsing the
            // suggestions or restoring the compact URL display.
            if (BrowserToolbar.IsAddressFocused || BrowserToolbar.IsAddressSuggestionListOpen)
                return;

            BrowserToolbar.ClearAddressSuggestions();
            if (_activeTab?.View.Source is not null)
                BrowserToolbar.AddressText = GetCompactUrl(_activeTab.View.Source.ToString());
        });
    }

    private void BackButton_Click(
        object sender, RoutedEventArgs e)
    {
        if (_activeTab?.View.CanGoBack == true)
            _activeTab.View.GoBack();
    }

    private void ForwardButton_Click(
        object sender, RoutedEventArgs e)
    {
        if (_activeTab?.View.CanGoForward == true)
            _activeTab.View.GoForward();
    }

    private void ReloadButton_Click(
        object sender, RoutedEventArgs e)
    {
        _activeTab?.View.Reload();
    }

    private async void NewTabKeyboardAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await CreateTabAsync(focusAddressBar: true);
    }

}
