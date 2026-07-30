using Macshot.Windows.Core.Input;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

using Windows.System;
using Windows.UI.Core;

namespace Macshot.Windows;

/// <summary>
/// One shortcut in preferences: shows what it is bound to, and records a new binding from
/// the keys the user presses.
/// </summary>
/// <remarks>
/// <para>
/// It replaced a text box. Typing <c>Ctrl+Shift+X</c> into a field asks the user to know
/// macshot's spelling of every modifier and to guess what a punctuation key is called,
/// and it accepts nonsense right up until Save refuses it. Pressing the keys cannot be
/// misspelled, and it is how every other program on the machine asks this question.
/// </para>
/// <para>
/// The text form survives, because that is what the settings file holds and what a person
/// editing it by hand writes. This control produces and shows exactly that string, so
/// nothing downstream had to change.
/// </para>
/// </remarks>
public sealed partial class HotkeyBox : UserControl
{
    private const string Prompt = "Press the shortcut • Esc to keep the current one";

    private string _binding = string.Empty;
    private bool _recording;

    public HotkeyBox()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Raised when a gesture completes, and not when <see cref="Binding"/> is assigned:
    /// what the settings file already held is not a change to it.
    /// </summary>
    public event EventHandler? BindingChanged;

    /// <summary>What this shortcut does, shown above the button.</summary>
    public string Header
    {
        get => HeaderText.Text;
        set => HeaderText.Text = value;
    }

    /// <summary>
    /// The binding as text, in the form the settings file holds. Round trips through
    /// <see cref="HotkeyBinding"/> so what is shown is what was stored, including a
    /// hand-written file's spelling being normalized to macshot's.
    /// </summary>
    public string Binding
    {
        get => _binding;
        set
        {
            _binding = value ?? string.Empty;
            ShowBinding();
        }
    }

    private void Record_Click(object sender, RoutedEventArgs e)
    {
        _recording = true;
        RecordButton.Content = Prompt;
    }

    private void Record_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!_recording)
        {
            return;
        }

        // Handled whatever it is: while recording, the keyboard belongs to this control.
        // Without it Space and Enter would re-arm the button they arrived on, and Tab
        // would record nothing and move the focus away mid-gesture.
        e.Handled = true;

        // A modifier alone is the user still reaching for the shortcut, not the shortcut.
        if (IsModifier(e.Key))
        {
            return;
        }

        if (e.Key == VirtualKey.Escape)
        {
            _recording = false;
            ShowBinding();
            return;
        }

        var candidate = new HotkeyBinding(HeldModifiers(), (uint)e.Key);

        if (!candidate.IsValid)
        {
            // Said while the gesture is still open rather than on Save: the answer is to
            // press it again with a modifier held, and that is worth knowing now.
            RecordButton.Content = "Hold Ctrl, Alt, Shift or Win too • Esc to keep the current one";
            return;
        }

        if (!candidate.CanBeStored)
        {
            RecordButton.Content = "That key cannot be stored • press another • Esc to keep the current one";
            return;
        }

        _recording = false;
        _binding = candidate.ToString();
        ShowBinding();
        BindingChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Clicking elsewhere abandons the recording. A control left waiting for a key it
    /// will never be sent would swallow the next one it happened to get.
    /// </summary>
    private void Record_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!_recording)
        {
            return;
        }

        _recording = false;
        ShowBinding();
    }

    private void ShowBinding() =>
        RecordButton.Content = HotkeyBinding.TryParse(_binding, out var parsed)
            ? parsed.ToString()
            : $"{_binding} (not a shortcut)";

    private static bool IsModifier(VirtualKey key) => key
        is VirtualKey.Control or VirtualKey.LeftControl or VirtualKey.RightControl
        or VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift
        or VirtualKey.Menu or VirtualKey.LeftMenu or VirtualKey.RightMenu
        or VirtualKey.LeftWindows or VirtualKey.RightWindows;

    /// <summary>
    /// The modifiers actually down right now, read from the keyboard rather than from the
    /// event: <see cref="KeyRoutedEventArgs"/> carries no modifier state.
    /// </summary>
    private static HotkeyModifiers HeldModifiers()
    {
        var modifiers = HotkeyModifiers.None;
        if (IsDown(VirtualKey.Control))
        {
            modifiers |= HotkeyModifiers.Control;
        }

        if (IsDown(VirtualKey.Menu))
        {
            modifiers |= HotkeyModifiers.Alt;
        }

        if (IsDown(VirtualKey.Shift))
        {
            modifiers |= HotkeyModifiers.Shift;
        }

        // Either Windows key, and neither is reported by the combined virtual key the
        // other three have.
        if (IsDown(VirtualKey.LeftWindows) || IsDown(VirtualKey.RightWindows))
        {
            modifiers |= HotkeyModifiers.Windows;
        }

        return modifiers;
    }

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
}
