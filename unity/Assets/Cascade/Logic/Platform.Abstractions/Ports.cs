using System;

namespace Cascade.Platform.Abstractions
{
    public enum PlatformKind { Desktop, Android }

    /// <summary>What the host is. The only place a platform question is answered (TRD §2.3).</summary>
    public sealed record HostInfo(
        PlatformKind Kind,
        bool HasMouse,
        bool HasKeyboard,
        bool HasTouch,
        bool Dev,
        float ScreenDpi);

    /// <summary>Files in the user data folder.</summary>
    public interface IStorage
    {
        byte[]? TryRead(string name);
        void WriteAtomic(string name, byte[] bytes);
        void Move(string name, string newName);
        void Delete(string name);
    }

    /// <summary>Shipped data (dials.toml, difficulty.toml) by key.</summary>
    public interface IDataSource
    {
        string ReadText(string key);
    }

    /// <summary>The only non-deterministic input to a level.</summary>
    public interface ISeedSource
    {
        ulong NextSeed();
    }

    public interface IStopwatch
    {
        double ElapsedMilliseconds { get; }
    }

    public interface ILog
    {
        void Info(string message);
        void Warn(string message);
        void Error(string message);
    }

    /// <summary>The unsafe margins of the screen in pixels: how far the safe area is inset from each edge (cutouts, system bars, gesture area).</summary>
    public readonly struct SafeArea
    {
        public SafeArea(float left, float top, float right, float bottom)
        {
            Left = left; Top = top; Right = right; Bottom = bottom;
        }

        public float Left { get; }
        public float Top { get; }
        public float Right { get; }
        public float Bottom { get; }
    }

    public readonly struct ScreenInfo
    {
        public ScreenInfo(int width, int height, SafeArea safeArea)
        {
            Width = width; Height = height; SafeArea = safeArea;
        }

        public int Width { get; }
        public int Height { get; }
        public SafeArea SafeArea { get; }
        public bool IsPortrait => Height >= Width;
    }

    public interface IAppLifecycle
    {
        event Action Paused;
        event Action Resumed;
        event Action BackPressed;
        event Action<ScreenInfo> ScreenChanged;
    }
}
