using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace BlindDuel
{
    /// <summary>
    /// SDL3 controller wrapper for reliable gamepad input (especially analog sticks).
    /// Reads controller state independently of the game's input system.
    /// SDL3.dll must be placed in the game's root folder.
    /// </summary>
    public static class SDLController
    {
        private const uint SDL_INIT_GAMEPAD = 0x00002000;
        private const uint SDL_INIT_JOYSTICK = 0x00000200;

        public enum Button
        {
            South = 0,    // A / Cross
            East = 1,     // B / Circle
            West = 2,     // X / Square
            North = 3,    // Y / Triangle
            Back = 4,
            Guide = 5,
            Start = 6,
            LeftStick = 7,
            RightStick = 8,
            LeftShoulder = 9,
            RightShoulder = 10,
            DPadUp = 11,
            DPadDown = 12,
            DPadLeft = 13,
            DPadRight = 14,
        }

        public enum Axis
        {
            LeftX = 0,
            LeftY = 1,
            RightX = 2,
            RightY = 3,
            LeftTrigger = 4,
            RightTrigger = 5,
        }

        // P/Invoke
        private const string DLL = "SDL3.dll";

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern bool SDL_Init(uint flags);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern void SDL_Quit();

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern void SDL_PumpEvents();

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr SDL_GetGamepads(out int count);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr SDL_OpenGamepad(uint instance_id);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern void SDL_CloseGamepad(IntPtr gamepad);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern bool SDL_GamepadConnected(IntPtr gamepad);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern bool SDL_GetGamepadButton(IntPtr gamepad, Button button);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern short SDL_GetGamepadAxis(IntPtr gamepad, Axis axis);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr SDL_GetGamepadName(IntPtr gamepad);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr SDL_GetError();

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern bool SDL_SetHint(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern void SDL_free(IntPtr mem);

        // State
        private static bool _initialized;
        private static bool _available;
        private static IntPtr _gamepad = IntPtr.Zero;
        private static string _controllerName = "";

        // Axis state for trigger detection (current vs last frame)
        private static short _lastRightY;
        private static short _rightY;

        private const short StickThreshold = 16000; // ~50% of 32767

        public static bool IsAvailable => _available && _gamepad != IntPtr.Zero;
        public static string ControllerName => _controllerName;

        public static bool Initialize()
        {
            if (_initialized) return _available;
            _initialized = true;

            try
            {
                SDL_SetHint("SDL_WINDOWS_DISABLE_THREAD_NAMING", "1");

                if (!SDL_Init(SDL_INIT_GAMEPAD | SDL_INIT_JOYSTICK))
                {
                    IntPtr errPtr = SDL_GetError();
                    string err = errPtr != IntPtr.Zero ? Marshal.PtrToStringUTF8(errPtr) : "Unknown";
                    Log.Write($"[SDL3] Init failed: {err}");
                    return false;
                }

                _available = true;
                Log.Write("[SDL3] Initialized");
                OpenFirstGamepad();
                return true;
            }
            catch (DllNotFoundException)
            {
                Log.Write("[SDL3] SDL3.dll not found — controller detail reading unavailable");
                return false;
            }
            catch (Exception ex)
            {
                Log.Write($"[SDL3] Init error: {ex.Message}");
                return false;
            }
        }

        public static void Update()
        {
            if (!_available) return;

            try
            {
                SDL_PumpEvents();

                // Check connection
                if (_gamepad != IntPtr.Zero && !SDL_GamepadConnected(_gamepad))
                {
                    Log.Write($"[SDL3] Disconnected: {_controllerName}");
                    SDL_CloseGamepad(_gamepad);
                    _gamepad = IntPtr.Zero;
                    _controllerName = "";
                }

                if (_gamepad == IntPtr.Zero)
                    OpenFirstGamepad();

                if (_gamepad == IntPtr.Zero) return;

                // Store last frame, read current
                _lastRightY = _rightY;
                _rightY = SDL_GetGamepadAxis(_gamepad, Axis.RightY);
            }
            catch (Exception ex)
            {
                Log.Write($"[SDL3] Update error: {ex.Message}");
            }
        }

        // Right stick trigger detection (just crossed threshold this frame)
        public static bool RightStickUpTriggered =>
            _available && _gamepad != IntPtr.Zero &&
            _rightY < -StickThreshold && _lastRightY >= -StickThreshold;

        public static bool RightStickDownTriggered =>
            _available && _gamepad != IntPtr.Zero &&
            _rightY > StickThreshold && _lastRightY <= StickThreshold;

        public static void Shutdown()
        {
            if (!_available) return;
            try
            {
                if (_gamepad != IntPtr.Zero)
                {
                    SDL_CloseGamepad(_gamepad);
                    _gamepad = IntPtr.Zero;
                }
                SDL_Quit();
                _available = false;
                Log.Write("[SDL3] Shutdown");
            }
            catch (Exception ex) { Log.Write($"[SDL3] Shutdown error: {ex.Message}"); }
        }

        private static void OpenFirstGamepad()
        {
            try
            {
                IntPtr ptr = SDL_GetGamepads(out int count);
                if (ptr == IntPtr.Zero || count == 0) return;

                uint id = (uint)Marshal.ReadInt32(ptr);
                SDL_free(ptr);

                _gamepad = SDL_OpenGamepad(id);
                if (_gamepad != IntPtr.Zero)
                {
                    IntPtr namePtr = SDL_GetGamepadName(_gamepad);
                    _controllerName = namePtr != IntPtr.Zero
                        ? Marshal.PtrToStringUTF8(namePtr) : "Unknown";
                    Log.Write($"[SDL3] Connected: {_controllerName}");
                }
            }
            catch (Exception ex) { Log.Write($"[SDL3] OpenGamepad error: {ex.Message}"); }
        }
    }
}
