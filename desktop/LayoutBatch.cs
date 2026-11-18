namespace HoyoFileClean.Desktop;

// Suspend descendants as well: suspending a form alone does not stop nested auto-sized tables.
internal sealed class LayoutBatch : IDisposable
{
    private readonly List<Control> controls = [];

    internal LayoutBatch(Control root) => Suspend(root);

    private void Suspend(Control control)
    {
        control.SuspendLayout();
        controls.Add(control);
        foreach (Control child in control.Controls) Suspend(child);
    }

    public void Dispose()
    {
        // Parents remain suspended while their children settle to their final state.
        for (int i = controls.Count - 1; i >= 0; i--) controls[i].ResumeLayout(true);
    }
}
