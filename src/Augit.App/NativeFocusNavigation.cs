namespace Augit.App;

internal static class NativeFocusNavigation
{
    internal static bool MoveWithinRegion(
        IReadOnlyList<nint> controls,
        nint currentFocus,
        bool backwards)
    {
        nint[] focusableControls = controls
            .Where(IsFocusable)
            .Distinct()
            .ToArray();
        if (focusableControls.Length == 0)
        {
            return false;
        }

        int currentIndex = Array.FindIndex(
            focusableControls,
            control => control == currentFocus
                || (currentFocus != 0 && NativeMethods.IsChild(control, currentFocus)));
        int nextIndex = ResolveNextIndex(focusableControls.Length, currentIndex, backwards);
        _ = NativeMethods.SetFocus(focusableControls[nextIndex]);
        return true;
    }

    internal static bool ContainsWindow(nint container, nint window)
    {
        return container != 0
            && window != 0
            && (container == window || NativeMethods.IsChild(container, window));
    }

    internal static int ResolveNextIndexForTest(int count, int currentIndex, bool backwards)
    {
        return ResolveNextIndex(count, currentIndex, backwards);
    }

    private static bool IsFocusable(nint control)
    {
        return control != 0
            && NativeMethods.IsWindow(control)
            && NativeMethods.IsWindowVisible(control)
            && NativeMethods.IsWindowEnabled(control);
    }

    private static int ResolveNextIndex(int count, int currentIndex, bool backwards)
    {
        if (count <= 0)
        {
            return -1;
        }

        if (currentIndex < 0 || currentIndex >= count)
        {
            return backwards ? count - 1 : 0;
        }

        return backwards
            ? (currentIndex + count - 1) % count
            : (currentIndex + 1) % count;
    }
}
