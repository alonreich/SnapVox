namespace snapvox.foundation.Interfaces
{
    public enum ScreenCaptureMode
    {
        Auto,
        FullScreen,
        Fixed
    }

    public enum WindowCaptureMode
    {
        Screen,
        GDI,
        Aero,
        AeroTransparent,
        Auto
    }

    public enum CaptureMode
    {
        Region,
        Window,
        Fullscreen,
        LastRegion,
        Clipboard
    }
}
