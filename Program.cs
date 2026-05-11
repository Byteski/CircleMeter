using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using LibreHardwareMonitor.Hardware;
using System.Collections.Generic;

namespace CircleMeter;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayAppContent());
    }
}

public enum MeterMode
{
    Cpu, Gpu, Memory
}

public enum MeterValueType
{
    Usage, Temperature
}

public enum TemperatureUnit
{
    Celsius, Fahrenheit
}

public sealed class TrayAppContent : ApplicationContext
{
    private readonly NotifyIcon trayIcon;
    private readonly System.Windows.Forms.Timer animationTimer;
    private readonly System.Windows.Forms.Timer sensorTimer;
    private readonly System.Windows.Forms.Timer hoverTimer;

    private readonly CpuUsageReader cpuUsageReader = new();
    private readonly HardwareReader hardwareReader = new();

    private const int MenuWidth = 150;

    private string currentHoverText = "CircleMeter";

    private Point lastTrayMousePosition = Point.Empty;

    private bool isHoveringTrayIcon = false;

    private readonly HoverInfoForm hoverForm = new();

    private MeterMode mode = MeterMode.Cpu;
    private MeterValueType valueType = MeterValueType.Usage;
    private TemperatureUnit tempUnit = TemperatureUnit.Celsius;

    private float angle = 0f;
    private int currentValue = 0;

    private Icon? lastIcon;

    private RightCheckMenuHost? cpuItem;
    private RightCheckMenuHost? gpuItem;
    private RightCheckMenuHost? memoryItem;
    private RightCheckMenuHost? usageItem;
    private RightCheckMenuHost? tempItem;
    private RightCheckMenuHost? celsiusItem;
    private RightCheckMenuHost? fahrenheitItem;

    public TrayAppContent()
    {
        trayIcon = new NotifyIcon
        {
            Visible = true,
            Text = string.Empty
        };

        BuildMenu();

        trayIcon.MouseMove += (_, _) =>
        {
            isHoveringTrayIcon = true;
            lastTrayMousePosition = Cursor.Position;
            ShowHoverInfo();
        };

        animationTimer = new System.Windows.Forms.Timer
        {
            Interval = 50
        };

        sensorTimer = new System.Windows.Forms.Timer
        {
            Interval = 1000
        };

        hoverTimer = new System.Windows.Forms.Timer
        {
            Interval = 1200
        };

        animationTimer.Tick += (_, _) => AnimateIcon();
        sensorTimer.Tick += (_, _) => UpdateSensorValue();
        hoverTimer.Tick += (_, _) => UpdateHoverState();
        hoverTimer.Start();

        UpdateMenuChecks();
        UpdateSensorValue();
        AnimateIcon();

        animationTimer.Start();
        sensorTimer.Start();
    }

    private static ToolStripLabel CreateMenuHeader(string text)
    {
        using Font baseFont = SystemFonts.MenuFont ?? new Font("Segoe UI", 9f);

        return new ToolStripLabel(text)
        {
            AutoSize = false,
            Width = 150,
            Height = 26,
            Font = new Font(baseFont.FontFamily, baseFont.Size, FontStyle.Bold),
            ForeColor = Color.FromArgb(150, 150, 150),
            BackColor = Color.FromArgb(24, 24, 24),
            Padding = new Padding(8, 6, 8, 2),
            TextAlign = ContentAlignment.MiddleLeft
        };
    }

    private void ShowHoverInfo()
    {
        hoverForm.SetText(currentHoverText);

        Point mouse = Cursor.Position;

        hoverForm.Location = new Point(
            mouse.X - hoverForm.Width / 2,
            mouse.Y - hoverForm.Height - 18
        );

        if (!hoverForm.Visible) hoverForm.Show();
    }

    private void BuildMenu()
    {
        var menu = new ContextMenuStrip
        {
            BackColor = Color.FromArgb(24, 24, 24),
            ForeColor = Color.White,
            Renderer = new DarkMenuRenderer(),
            ShowImageMargin = false,
            ShowCheckMargin = false,
            Padding = new Padding(0),
        };

        cpuItem = new RightCheckMenuHost("CPU", () =>
        {
            mode = MeterMode.Cpu;
            UpdateMenuChecks();
            UpdateSensorValue();
            AnimateIcon();
        });

        gpuItem = new RightCheckMenuHost("GPU", () =>
        {
            mode = MeterMode.Gpu;
            UpdateMenuChecks();
            UpdateSensorValue();
            AnimateIcon();
        });

        memoryItem = new RightCheckMenuHost("Memory", () =>
        {
            mode = MeterMode.Memory;

            // Memory only supports usage
            valueType = MeterValueType.Usage;

            UpdateMenuChecks();
            UpdateSensorValue();
            AnimateIcon();
        });

        usageItem = new RightCheckMenuHost("Usage", () =>
        {
            valueType = MeterValueType.Usage;
            UpdateMenuChecks();
            UpdateSensorValue();
            AnimateIcon();
        });

        tempItem = new RightCheckMenuHost("Temperature", () =>
        {
            if (mode == MeterMode.Memory)
                return;

            valueType = MeterValueType.Temperature;
            UpdateMenuChecks();
            UpdateSensorValue();
            AnimateIcon();
        });

        celsiusItem = new RightCheckMenuHost("Celsius", () =>
        {
            tempUnit = TemperatureUnit.Celsius;
            UpdateMenuChecks();
            UpdateSensorValue();
            AnimateIcon();
        });

        fahrenheitItem = new RightCheckMenuHost("Fahrenheit", () =>
        {
            tempUnit = TemperatureUnit.Fahrenheit;
            UpdateMenuChecks();
            UpdateSensorValue();
            AnimateIcon();
        });

        var exitItem = new RightCheckMenuHost("Exit", () =>
        {
            ExitApp();
        })
        {
            AutoSize = false,
            Width = 150,
            Height = 30,
            ForeColor = Color.White,
            BackColor = Color.FromArgb(24, 24, 24),
            Padding = new Padding(8, 4, 8, 4)
        };

        menu.Items.Add(CreateMenuHeader("Mode"));
        menu.Items.Add(cpuItem);
        menu.Items.Add(gpuItem);
        menu.Items.Add(memoryItem);

        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add(CreateMenuHeader("Value"));
        menu.Items.Add(usageItem);
        menu.Items.Add(tempItem);

        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add(CreateMenuHeader("Temp Unit"));
        menu.Items.Add(celsiusItem);
        menu.Items.Add(fahrenheitItem);

        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add(exitItem);

        trayIcon.ContextMenuStrip = menu;
    }

    private static void SetRightCheck(RightCheckMenuHost? item, bool isChecked)
    {
        item?.SetChecked(isChecked);
    }

    public sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkMenuColors())
        {
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            Rectangle rect = new(Point.Empty, e.Item.Size);

            using Brush backgroundBrush = new SolidBrush(
                e.Item.Selected && e.Item.Enabled
                    ? Color.FromArgb(55, 55, 55)
                    : Color.FromArgb(24, 24, 24)
            );

            e.Graphics.FillRectangle(backgroundBrush, rect);

            // Draw right-side checkmark manually.
            if (e.Item is ToolStripMenuItem menuItem && menuItem.Checked)
            {
                DrawRightCheckmark(e.Graphics, rect);
            }
        }

        private static void DrawRightCheckmark(Graphics graphics, Rectangle itemRect)
        {
            int size = 16;
            int x = itemRect.Right - size - 8;
            int y = itemRect.Top + (itemRect.Height - size) / 2;

            Rectangle checkRect = new(x, y, size, size);

            using Brush checkBackground = new SolidBrush(Color.FromArgb(70, 70, 70));
            graphics.FillEllipse(checkBackground, checkRect);

            using Pen checkPen = new Pen(Color.White, 2);
            graphics.DrawLines(checkPen, new[]
            {
                new Point(checkRect.Left + 4, checkRect.Top + 8),
                new Point(checkRect.Left + 7, checkRect.Top + 11),
                new Point(checkRect.Left + 12, checkRect.Top + 5)
            });
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            Rectangle rect = new(0, 0, e.Item.Width, e.Item.Height);

            using Brush backgroundBrush = new SolidBrush(Color.FromArgb(24, 24, 24));
            e.Graphics.FillRectangle(backgroundBrush, rect);

            using Pen linePen = new Pen(Color.FromArgb(70, 70, 70));

            int y = e.Item.Height / 2;
            e.Graphics.DrawLine(linePen, 8, y, e.Item.Width - 8, y);
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {
            using Brush brush = new SolidBrush(Color.FromArgb(24, 24, 24));
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }
    }

    public sealed class DarkMenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Color.FromArgb(24, 24, 24);
        public override Color ImageMarginGradientBegin => Color.FromArgb(24, 24, 24);
        public override Color ImageMarginGradientMiddle => Color.FromArgb(24, 24, 24);
        public override Color ImageMarginGradientEnd => Color.FromArgb(24, 24, 24);

        public override Color MenuItemSelected => Color.FromArgb(55, 55, 55);
        public override Color MenuItemBorder => Color.FromArgb(80, 80, 80);
        public override Color MenuBorder => Color.FromArgb(70, 70, 70);

        public override Color SeparatorDark => Color.FromArgb(70, 70, 70);
        public override Color SeparatorLight => Color.FromArgb(70, 70, 70);
    }

    private void UpdateHoverState()
    {
        if (!isHoveringTrayIcon)
            return;

        Point currentMouse = Cursor.Position;

        int dx = Math.Abs(currentMouse.X - lastTrayMousePosition.X);
        int dy = Math.Abs(currentMouse.Y - lastTrayMousePosition.Y);

        //If mouse moved away from the tray, hide tooltip
        if (dx > 1 || dy > 1)
        {
            isHoveringTrayIcon = false;
            hoverForm.Hide();
            return;
        }

        //Keep tooltip updated while hovering
        if (hoverForm.Visible)
        {
            hoverForm.SetText(currentHoverText);
        }
    }

    private void UpdateMenuChecks()
    {
        cpuItem?.SetChecked(mode == MeterMode.Cpu);
        gpuItem?.SetChecked(mode == MeterMode.Gpu);
        memoryItem?.SetChecked(mode == MeterMode.Memory);

        usageItem?.SetChecked(valueType == MeterValueType.Usage);
        tempItem?.SetChecked(valueType == MeterValueType.Temperature);

        tempItem?.SetEnabled(mode != MeterMode.Memory);

        celsiusItem?.SetChecked(tempUnit == TemperatureUnit.Celsius);
        fahrenheitItem?.SetChecked(tempUnit == TemperatureUnit.Fahrenheit);

        celsiusItem?.SetEnabled(valueType == MeterValueType.Temperature);
        fahrenheitItem?.SetEnabled(valueType == MeterValueType.Temperature);
    }

    private void UpdateSensorValue()
    {
        currentValue = ReadCurrentValue();

        String modeLabel = mode switch
        {
            MeterMode.Cpu => "CPU",
            MeterMode.Gpu => "GPU",
            MeterMode.Memory => "Memory",
            _ => "???"
        };
        string suffix = GetSuffix();

        string valueLabel = valueType == MeterValueType.Usage ? "Usage" : "Temperature";

        currentHoverText = currentValue < 0
            ? $"{modeLabel} {valueLabel}: N/A"
            : $"{modeLabel} {valueLabel}: {currentValue}{suffix}";

        trayIcon.Text = string.Empty;

        if (hoverForm.Visible) hoverForm.SetText(currentHoverText);
    }

    public sealed class HoverInfoForm : Form
    {
        private readonly Label label;

        public HoverInfoForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = Color.FromArgb(30, 30, 30);
            Padding = new Padding(10, 6, 10, 6);
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            label = new Label
            {
                AutoSize = true,
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Text = "CircleMeter"
            };
            Controls.Add(label);
        }
        public void SetText(string text)
        {
            label.Text = text;
        }
        protected override bool ShowWithoutActivation => true;
    }

    private void AnimateIcon()
    {
        Color circleColor = mode switch
        {
            MeterMode.Cpu => Color.Orange,
            MeterMode.Gpu => Color.LimeGreen,
            MeterMode.Memory => Color.FromArgb(0, 90, 255),
            _ => Color.White
        };

        //This is tweakable (speed value)
        float normalized = Math.Clamp(currentValue, 0, 100) / 100f;
        float speed = 25f - (normalized * 20f);
        angle = (angle + speed) % 360f;

        Icon newIcon = IconRenderer.CreateMeterIcon(
            value: currentValue,
            circleColor: circleColor,
            angle: angle
        );

        Icon? oldIcon = lastIcon;
        lastIcon = newIcon;
        trayIcon.Icon = newIcon;
        oldIcon?.Dispose();
    }

    private int ReadCurrentValue()
    {
        if (mode == MeterMode.Memory)
        {
            return MemoryReader.GetMemoryUsagePercent();
        }

        if (mode == MeterMode.Cpu && valueType == MeterValueType.Usage)
        {
            return cpuUsageReader.GetCpuUsagePercent();
        }

        if (mode == MeterMode.Cpu && valueType == MeterValueType.Temperature)
        {
            float? temp = hardwareReader.GetCpuTemperatureCelsius();
            return ConvertTempUnit(temp);
        }

        if (mode == MeterMode.Gpu && valueType == MeterValueType.Usage)
        {
            float? usage = hardwareReader.GetGpuUsagePercent();
            return usage.HasValue ? Math.Clamp((int)Math.Round(usage.Value), 0, 100) : 0;
        }

        if (mode == MeterMode.Gpu && valueType == MeterValueType.Temperature)
        {
            float? temp = hardwareReader.GetGpuTemperatureCelsius();
            return ConvertTempUnit(temp);
        }
        return 0;
    }

    private int ConvertTempUnit(float? celsius)
    {
        if (!celsius.HasValue || celsius.Value <= 0)
            return -1;

        float value = celsius.Value;

        if (tempUnit == TemperatureUnit.Fahrenheit)
        {
            value = value * 9f / 5f + 32f;
        }
        return Math.Clamp((int)Math.Round(value), 0, 999);
    }

    private string GetSuffix()
    {
        if (valueType == MeterValueType.Usage)
            return "%";

        return tempUnit == TemperatureUnit.Celsius ? "°C" : "°F";
    }

    private void ExitApp()
    {
        animationTimer.Stop();
        sensorTimer.Stop();
        hoverTimer.Stop();

        hoverForm.Hide();
        hoverForm.Dispose();

        trayIcon.Visible = false;
        trayIcon.Icon = null;
        trayIcon.Dispose();
        lastIcon?.Dispose();
        hardwareReader.Dispose();

        Application.Exit();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            animationTimer.Dispose();
            sensorTimer.Dispose();
            hoverTimer.Dispose();

            trayIcon.Dispose();
            lastIcon?.Dispose();
            hardwareReader.Dispose();
            hoverForm.Dispose();
        }
        base.Dispose(disposing);
    }
}

public sealed class RightCheckMenuHost : ToolStripControlHost
{
    private readonly RightCheckMenuRow row;

    public RightCheckMenuHost(string text, Action onClick)
        : base(new RightCheckMenuRow(text))
    {
        row = (RightCheckMenuRow)Control;

        AutoSize = false;
        Width = 150;
        Height = 30;
        Margin = Padding.Empty;
        Padding = Padding.Empty;

        row.Width = 150;
        row.Height = 30;

        row.Click += (_, _) =>
        {
            onClick();

            if (Owner is ContextMenuStrip menu)
            {
                menu.Close();
            }
        };

        row.MouseEnter += (_, _) => row.SetHovered(true);
        row.MouseLeave += (_, _) => row.SetHovered(false);
    }

    public void SetChecked(bool isChecked)
    {
        row.SetChecked(isChecked);
    }

    public void SetEnabled(bool isEnabled)
    {
        Enabled = isEnabled;
        row.SetEnabledVisual(isEnabled);
    }
}

public sealed class RightCheckMenuRow : Control
{
    private readonly string text;
    private bool isChecked;
    private bool isHovered;
    private bool isEnabledVisual = true;

    public RightCheckMenuRow(string text)
    {
        this.text = text;

        Width = 150;
        Height = 30;

        Cursor = Cursors.Hand;

        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer,
            true
        );
    }

    public void SetChecked(bool value)
    {
        isChecked = value;
        Invalidate();
    }

    public void SetHovered(bool value)
    {
        isHovered = value;
        Invalidate();
    }

    public void SetEnabledVisual(bool value)
    {
        isEnabledVisual = value;
        Cursor = value ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        Color backgroundColor = isHovered && isEnabledVisual
            ? Color.FromArgb(55, 55, 55)
            : Color.FromArgb(24, 24, 24);

        using Brush backgroundBrush = new SolidBrush(backgroundColor);
        e.Graphics.FillRectangle(backgroundBrush, ClientRectangle);

        Color textColor = isEnabledVisual
            ? Color.White
            : Color.FromArgb(100, 100, 100);

        Rectangle textRect = new Rectangle(8, 0, Width - 45, Height);

        TextRenderer.DrawText(
            e.Graphics,
            text,
            new Font("Segoe UI", 9f, FontStyle.Regular),
            textRect,
            textColor,
            TextFormatFlags.Left |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPadding
        );

        if (!isChecked)
            return;

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        int size = 16;
        int x = Width - size - 8;
        int y = (Height - size) / 2;

        Rectangle checkRect = new Rectangle(x, y, size, size);

        using Brush circleBrush = new SolidBrush(Color.FromArgb(75, 75, 75));
        e.Graphics.FillEllipse(circleBrush, checkRect);

        using Pen checkPen = new Pen(Color.White, 2);
        e.Graphics.DrawLines(checkPen, new[]
        {
            new Point(checkRect.Left + 4, checkRect.Top + 8),
            new Point(checkRect.Left + 7, checkRect.Top + 11),
            new Point(checkRect.Left + 12, checkRect.Top + 5)
        });
    }
}

public static class IconRenderer
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public static Icon CreateMeterIcon(int value, Color circleColor, float angle)
    {
        const int size = 64;
        using Bitmap bitmap = new Bitmap(size, size);
        using Graphics graphics = Graphics.FromImage(bitmap);

        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        Rectangle circleRect = new Rectangle(6, 6, size - 12, size - 12);

        using Pen backgroundPen = new Pen(Color.White, 7);
        using Pen circlePen = new Pen(circleColor, 7)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };

        graphics.DrawEllipse(backgroundPen, circleRect);

        graphics.DrawArc(circlePen, circleRect, angle, 70);

        string text = value < 0 ? "--" : value >= 100 ? "99" : value.ToString();

        int fontSize = text.Length == 1 ? 38 : 32;

        using Font font = new Font("Arial", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);

        Rectangle textRect = new Rectangle(0, 0, size, size);

        TextRenderer.DrawText(
            graphics, text, font, textRect, Color.White,
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPadding
        );

        IntPtr hIcon = bitmap.GetHicon();

        try
        {
            using Icon tempIcon = Icon.FromHandle(hIcon);
            return (Icon)tempIcon.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }
}

public sealed class CpuUsageReader
{
    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint LowDateTime;
        public uint HighDateTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(
        out FILETIME idleTime,
        out FILETIME kernelTime,
        out FILETIME userTime
    );

    private ulong previousIdle;
    private ulong previousTotal;
    private bool hasPrevious;

    public int GetCpuUsagePercent()
    {
        if (!GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user))
            return 0;

        ulong idleTime = FileTimeToUInt64(idle);
        ulong kernelTime = FileTimeToUInt64(kernel);
        ulong userTime = FileTimeToUInt64(user);

        ulong totalTime = kernelTime + userTime;

        if (!hasPrevious)
        {
            previousIdle = idleTime;
            previousTotal = totalTime;
            hasPrevious = true;
            return 0;
        }

        ulong totalDelta = totalTime - previousTotal;
        ulong idleDelta = idleTime - previousIdle;

        previousIdle = idleTime;
        previousTotal = totalTime;

        if (totalDelta == 0)
            return 0;

        double usage = 100.0 * (1.0 - ((double)idleDelta / totalDelta));

        return Math.Clamp((int)Math.Round(usage), 0, 100);
    }

    private static ulong FileTimeToUInt64(FILETIME fileTime)
    {
        return ((ulong)fileTime.HighDateTime << 32) | fileTime.LowDateTime;
    }
}

public static class MemoryReader
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    public static int GetMemoryUsagePercent()
    {
        MEMORYSTATUSEX status = new()
        {
            dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>()
        };

        if (!GlobalMemoryStatusEx(ref status))
            return 0;

        return Math.Clamp((int)status.dwMemoryLoad, 0, 100);
    }
}

public sealed class HardwareReader : IDisposable
{
    private readonly LibreHardwareMonitor.Hardware.Computer computer;

    public HardwareReader()
    {
        computer = new LibreHardwareMonitor.Hardware.Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMotherboardEnabled = true,
            IsControllerEnabled = true
        };
        computer.Open();
    }

    public float? GetCpuTemperatureCelsius()
    {
        UpdateAllHardware();

        // First: search CPU hardware directly.
        foreach (IHardware hardware in computer.Hardware)
        {
            if (hardware.HardwareType != HardwareType.Cpu)
                continue;

            string[] preferredNames =
            {
            "package",
            "core max",
            "core average",
            "ccd",
            "cpu",
            "core",
            "tctl",
            "tdie"
        };

            foreach (string name in preferredNames)
            {
                float? temp = FindValidTemperatureByName(hardware, name);

                if (temp.HasValue)
                    return temp.Value;
            }

            float? highestCpuTemp = FindHighestValidSensorValue(hardware, SensorType.Temperature);

            if (highestCpuTemp.HasValue)
                return highestCpuTemp.Value;
        }

        // Fallback: motherboard / Super I/O sensors.
        foreach (IHardware hardware in computer.Hardware)
        {
            if (hardware.HardwareType != HardwareType.Motherboard)
                continue;

            string[] motherboardCpuNames =
            {
            "cpu",
            "package",
            "core",
            "tctl",
            "tdie"
        };

            foreach (string name in motherboardCpuNames)
            {
                float? temp = FindValidTemperatureByName(hardware, name);

                if (temp.HasValue)
                    return temp.Value;
            }

            float? highestMotherboardTemp = FindHighestValidSensorValue(hardware, SensorType.Temperature);

            if (highestMotherboardTemp.HasValue)
                return highestMotherboardTemp.Value;
        }
        return null;
    }

    private static float? FindValidTemperatureByName(IHardware hardware, string namePart)
    {
        foreach (ISensor sensor in hardware.Sensors)
        {
            if (sensor.SensorType != SensorType.Temperature)
                continue;

            if (!sensor.Value.HasValue)
                continue;

            float value = sensor.Value.Value;

            // Ignore impossible / broken readings.
            if (value <= 0 || value > 130)
                continue;

            if (sensor.Name.Contains(namePart, StringComparison.OrdinalIgnoreCase))
                return value;
        }

        foreach (IHardware subHardware in hardware.SubHardware)
        {
            float? value = FindValidTemperatureByName(subHardware, namePart);

            if (value.HasValue)
                return value.Value;
        }

        return null;
    }

    private static float? FindHighestValidSensorValue(IHardware hardware, SensorType sensorType)
    {
        float? highest = null;

        foreach (ISensor sensor in hardware.Sensors)
        {
            if (sensor.SensorType != sensorType)
                continue;

            if (!sensor.Value.HasValue)
                continue;

            float value = sensor.Value.Value;

            // Ignore invalid CPU/GPU temperature readings.
            if (sensorType == SensorType.Temperature && (value <= 0 || value > 130))
                continue;

            if (!highest.HasValue || value > highest.Value)
                highest = value;
        }

        foreach (IHardware subHardware in hardware.SubHardware)
        {
            float? subValue = FindHighestValidSensorValue(subHardware, sensorType);

            if (subValue.HasValue && (!highest.HasValue || subValue.Value > highest.Value))
                highest = subValue.Value;
        }

        return highest;
    }

    public float? GetGpuUsagePercent()
    {
        UpdateAllHardware();

        foreach (IHardware hardware in computer.Hardware)
        {
            if (!IsGpu(hardware))
                continue;

            float? gpuCoreLoad = FindLoadByName(hardware, "core");

            if (gpuCoreLoad.HasValue)
                return gpuCoreLoad.Value;

            float? gpuLoad = FindLoadByName(hardware, "gpu");

            if (gpuLoad.HasValue)
                return gpuLoad.Value;

            return FindHighestSensorValue(hardware, SensorType.Load);
        }

        return null;
    }

    public float? GetGpuTemperatureCelsius()
    {
        UpdateAllHardware();

        foreach (IHardware hardware in computer.Hardware)
        {
            if (!IsGpu(hardware))
                continue;

            float? gpuCoreTemp = FindTemperatureByName(hardware, "core");

            if (gpuCoreTemp.HasValue)
                return gpuCoreTemp.Value;

            float? gpuTemp = FindTemperatureByName(hardware, "gpu");

            if (gpuTemp.HasValue)
                return gpuTemp.Value;

            return FindHighestSensorValue(hardware, SensorType.Temperature);
        }

        return null;
    }

    private void UpdateAllHardware()
    {
        foreach (IHardware hardware in computer.Hardware)
        {
            UpdateHardware(hardware);
        }
    }

    private static void UpdateHardware(IHardware hardware)
    {
        hardware.Update();

        foreach (IHardware subHardware in hardware.SubHardware)
        {
            UpdateHardware(subHardware);
        }
    }

    private static bool IsGpu(IHardware hardware)
    {
        return hardware.HardwareType == HardwareType.GpuNvidia ||
               hardware.HardwareType == HardwareType.GpuAmd ||
               hardware.HardwareType == HardwareType.GpuIntel;
    }

    private static float? FindTemperatureByName(IHardware hardware, string namePart)
    {
        foreach (ISensor sensor in hardware.Sensors)
        {
            if (sensor.SensorType != SensorType.Temperature)
                continue;

            if (!sensor.Value.HasValue)
                continue;

            if (sensor.Name.Contains(namePart, StringComparison.OrdinalIgnoreCase))
                return sensor.Value.Value;
        }

        foreach (IHardware subHardware in hardware.SubHardware)
        {
            float? value = FindTemperatureByName(subHardware, namePart);

            if (value.HasValue)
                return value.Value;
        }

        return null;
    }

    private static float? FindLoadByName(IHardware hardware, string namePart)
    {
        foreach (ISensor sensor in hardware.Sensors)
        {
            if (sensor.SensorType != SensorType.Load)
                continue;

            if (!sensor.Value.HasValue)
                continue;

            if (sensor.Name.Contains(namePart, StringComparison.OrdinalIgnoreCase))
                return sensor.Value.Value;
        }

        foreach (IHardware subHardware in hardware.SubHardware)
        {
            float? value = FindLoadByName(subHardware, namePart);

            if (value.HasValue)
                return value.Value;
        }

        return null;
    }

    private static float? FindHighestSensorValue(IHardware hardware, SensorType sensorType)
    {
        float? highest = null;

        foreach (ISensor sensor in hardware.Sensors)
        {
            if (sensor.SensorType != sensorType)
                continue;

            if (!sensor.Value.HasValue)
                continue;

            if (!highest.HasValue || sensor.Value.Value > highest.Value)
            {
                highest = sensor.Value.Value;
            }
        }

        foreach (IHardware subHardware in hardware.SubHardware)
        {
            float? subValue = FindHighestSensorValue(subHardware, sensorType);

            if (subValue.HasValue && (!highest.HasValue || subValue.Value > highest.Value))
            {
                highest = subValue.Value;
            }
        }

        return highest;
    }

    public void Dispose()
    {
        computer.Close();
    }
}