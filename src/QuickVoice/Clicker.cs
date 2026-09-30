using System.Windows.Automation;
using QuickVoice.Core;

namespace QuickVoice;

/// <summary>"clica em salvar": finds the control by its name in the window in front, through UI Automation, and presses it.</summary>
internal static class Clicker
{
    private static readonly ControlType[] Clickable =
    [
        ControlType.Button, ControlType.SplitButton, ControlType.MenuItem, ControlType.Hyperlink, ControlType.TabItem,
        ControlType.ListItem, ControlType.TreeItem, ControlType.CheckBox, ControlType.RadioButton, ControlType.DataItem,
    ];

    /// <summary>Runs off the UI thread: UI Automation calls back into the windows it walks, this app's included.</summary>
    public static Task ClickAsync(nint window, string target) => Task.Run(() => Click(window, target));

    private static void Click(nint window, string target)
    {
        if (window == 0) throw new InvalidOperationException("nenhuma janela em foco para clicar");
        var root = AutomationElement.FromHandle(window);
        var condition = new OrCondition(Clickable.Select(t => (Condition)new PropertyCondition(AutomationElement.ControlTypeProperty, t)).ToArray());
        var wanted = Words(target);
        var best = root.FindAll(TreeScope.Descendants, condition).Cast<AutomationElement>()
            .Select(e => (Element: e, Score: Score(Name(e), wanted)))
            .Where(c => c.Score > 0 && !IsOffscreen(c.Element))
            .OrderByDescending(c => c.Score)
            .FirstOrDefault();
        if (best.Element is null) throw new InvalidOperationException($"não achei “{target}” na janela em frente");
        Press(best.Element);
    }

    /// <summary>Exact name beats a name that starts with the words, which beats one that contains them all.</summary>
    private static int Score(string name, string[] wanted)
    {
        var words = Words(name);
        if (words.Length == 0 || wanted.Length == 0) return 0;
        if (words.SequenceEqual(wanted)) return 3;
        if (words.Take(wanted.Length).SequenceEqual(wanted)) return 2;
        return wanted.All(words.Contains) ? 1 : 0;
    }

    private static void Press(AutomationElement element)
    {
        if (element.TryGetCurrentPattern(InvokePattern.Pattern, out var invoke)) ((InvokePattern)invoke).Invoke();
        else if (element.TryGetCurrentPattern(TogglePattern.Pattern, out var toggle)) ((TogglePattern)toggle).Toggle();
        else if (element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var select)) ((SelectionItemPattern)select).Select();
        else if (element.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var expand)) ((ExpandCollapsePattern)expand).Expand();
        else
        {
            var r = element.Current.BoundingRectangle;
            if (r.IsEmpty) throw new InvalidOperationException("o controle não tem onde clicar");
            Win32.ClickAt((int)(r.Left + r.Width / 2), (int)(r.Top + r.Height / 2));
        }
    }

    private static string Name(AutomationElement element)
    {
        try
        {
            return element.Current.Name ?? "";
        }
        catch (ElementNotAvailableException)
        {
            return "";
        }
    }

    private static bool IsOffscreen(AutomationElement element)
    {
        try
        {
            return element.Current.IsOffscreen;
        }
        catch (ElementNotAvailableException)
        {
            return true;
        }
    }

    /// <summary>"Salvar como..." and "salvar como" are the same button; "&amp;Arquivo" is "arquivo".</summary>
    private static string[] Words(string text) =>
        Vocabulary.Words(text.Replace("&", "").Replace("…", " ").Replace("...", " "))
            .Select(Vocabulary.Normalized).Where(w => w.Length > 0).ToArray();
}
