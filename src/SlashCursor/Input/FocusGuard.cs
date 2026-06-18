using System;
using System.Windows.Automation;

namespace SlashCursor.Input;

/// <summary>
/// Decides whether the keyboard focus is currently in a text-editing control.
/// Used to suppress the "/" trigger while the user is legitimately typing
/// (e.g. a browser address bar, a document, a search box).
/// </summary>
public static class FocusGuard
{
    public static bool IsTextInputFocused()
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused is null) return false;

            var ct = focused.Current.ControlType;
            if (ct == ControlType.Edit || ct == ControlType.Document)
                return true;

            // Editable combo boxes and some custom inputs expose a Value or
            // Text pattern that is writable -> treat as text input.
            if (focused.TryGetCurrentPattern(ValuePattern.Pattern, out var vp) &&
                vp is ValuePattern value && !value.Current.IsReadOnly)
            {
                return true;
            }

            if (focused.TryGetCurrentPattern(TextPattern.Pattern, out _))
            {
                // A focused control exposing a Text pattern is usually editable
                // text content. Conservative: treat as text input.
                return true;
            }

            return false;
        }
        catch
        {
            // If UIA fails for any reason, do NOT suppress typing decisions on
            // its behalf -> assume not a text field so the trigger still works.
            return false;
        }
    }
}
