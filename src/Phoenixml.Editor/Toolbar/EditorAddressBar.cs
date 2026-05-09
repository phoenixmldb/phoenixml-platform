using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Phoenixml.Platform.Editor.Xml;

namespace Phoenixml.Editor.Toolbar;

/// <summary>
/// Displays and edits the active document's file path. Pressing Enter opens the file at the
/// typed path; Escape reverts to the committed path. Updates automatically on
/// active-document changes.
/// </summary>
internal sealed class EditorAddressBar : Border
{
    private readonly TextBox _pathBox;
    private readonly EditorMainWindow _window;

    public EditorAddressBar(EditorMainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _window = window;
        BorderThickness = new Thickness(0, 0, 0, 1);
        Padding = new Thickness(4, 2);

        _pathBox = new TextBox
        {
            Watermark = "(no document)",
            Padding = new Thickness(4, 2),
        };
        _pathBox.KeyDown += OnKeyDown;
        Child = _pathBox;

        window.State.ActiveDocumentChanged += OnActiveDocumentChanged;
        OnActiveDocumentChanged(window.State.ActiveDocument);
    }

    private void OnActiveDocumentChanged(XmlDocument? doc)
    {
        if (doc?.Source is null)
        {
            _pathBox.Text = string.Empty;
            return;
        }
        _pathBox.Text = doc.Source.IsFile ? doc.Source.LocalPath : doc.Source.ToString();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var path = (_pathBox.Text ?? string.Empty).Trim();
            if (path.Length > 0)
            {
                if (Directory.Exists(path))
                    _window.Explorer.SetRoot(path);
                else if (File.Exists(path))
                    _window.OpenFileOnLaunch(path);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            OnActiveDocumentChanged(_window.State.ActiveDocument);
            e.Handled = true;
        }
    }
}
