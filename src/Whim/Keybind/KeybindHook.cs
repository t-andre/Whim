﻿using System.Linq;
using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Whim;

/// <summary>
/// Responsible is responsible for hooking into windows and handling keybinds.
/// </summary>
internal class KeybindHook : IKeybindHook
{
	private readonly IContext _context;
	private readonly IInternalContext _internalContext;
	private readonly HOOKPROC _lowLevelKeyboardProc;
	private UnhookWindowsHookExSafeHandle? _unhookKeyboardHook;
	private bool _disposedValue;

	public KeybindHook(IContext context, IInternalContext internalContext)
	{
		_context = context;
		_internalContext = internalContext;
		_lowLevelKeyboardProc = LowLevelKeyboardProcWrapper;
	}

	public void PostInitialize()
	{
		Logger.Debug("Initializing keybind manager...");
		_unhookKeyboardHook = _internalContext.CoreNativeManager.SetWindowsHookEx(
			WINDOWS_HOOK_ID.WH_KEYBOARD_LL,
			_lowLevelKeyboardProc,
			null,
			0
		);
	}

	private LRESULT LowLevelKeyboardProcWrapper(int nCode, WPARAM wParam, LPARAM lParam)
	{
		try
		{
			return LowLevelKeyboardProc(nCode, wParam, lParam);
		}
		catch (Exception e)
		{
			_context.HandleUncaughtException(nameof(LowLevelKeyboardProc), e);
			return _internalContext.CoreNativeManager.CallNextHookEx(nCode, wParam, lParam);
		}
	}

	/// <summary>
	/// For relevant documentation, see https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelkeyboardproc
	/// </summary>
	/// <param name="nCode"></param>
	/// <param name="wParam"></param>
	/// <param name="lParam"></param>
	/// <returns></returns>
	private LRESULT LowLevelKeyboardProc(int nCode, WPARAM wParam, LPARAM lParam)
	{
		Logger.Verbose($"{nCode} {wParam.Value} {lParam.Value}");
		if (nCode != 0 || ((nuint)wParam != PInvoke.WM_KEYDOWN && (nuint)wParam != PInvoke.WM_SYSKEYDOWN))
		{
			return _internalContext.CoreNativeManager.CallNextHookEx(nCode, wParam, lParam);
		}

		if (_internalContext.CoreNativeManager.PtrToStructure<KBDLLHOOKSTRUCT>(lParam) is not KBDLLHOOKSTRUCT kbdll)
		{
			return _internalContext.CoreNativeManager.CallNextHookEx(nCode, wParam, lParam);
		}

		VIRTUAL_KEY key = (VIRTUAL_KEY)kbdll.vkCode;

		// Ignore key modifiers which are a modifier.
		if (_context.KeybindManager.Modifiers.Contains(key))
		{
			return _internalContext.CoreNativeManager.CallNextHookEx(nCode, wParam, lParam);
		}

		if (GetKeybindForKey(key) is Keybind keybind && DoKeyboardEvent(keybind))
		{
			return (LRESULT)1;
		}

		return _internalContext.CoreNativeManager.CallNextHookEx(nCode, wParam, lParam);
	}

	private IKeybind? GetKeybindForKey(VIRTUAL_KEY eventKey)
	{
		List<VIRTUAL_KEY> pressedModifiers = [];
		foreach (VIRTUAL_KEY modifier in _context.KeybindManager.Modifiers)
		{
			if (IsModifierPressed(modifier))
			{
				pressedModifiers.Add(modifier);
			}
		}

		return new Keybind(pressedModifiers, eventKey);
	}

	private bool IsModifierPressed(VIRTUAL_KEY key) =>
		(_internalContext.CoreNativeManager.GetKeyState((int)key) & 0x8000) == 0x8000;

	private bool DoKeyboardEvent(Keybind keybind)
	{
		Logger.Verbose(keybind.ToString());
		ICommand[] commands = _context.KeybindManager.GetCommands(keybind);

		if (commands.Length == 0)
		{
			Logger.Verbose($"No handler for {keybind}");
			return false;
		}

		foreach (ICommand command in commands)
		{
			command.TryExecute();
		}

		return true;
	}

	protected virtual void Dispose(bool disposing)
	{
		if (!_disposedValue)
		{
			if (disposing)
			{
				// dispose managed state (managed objects)
				_unhookKeyboardHook?.Dispose();
			}

			// free unmanaged resources (unmanaged objects) and override finalizer
			// set large fields to null
			_disposedValue = true;
		}
	}

	public void Dispose()
	{
		// Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}
}
