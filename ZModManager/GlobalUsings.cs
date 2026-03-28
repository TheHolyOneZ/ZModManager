// Global aliases to resolve WPF vs WinForms namespace ambiguities.
// These are needed because UseWindowsForms=true imports System.Windows.Forms
// and System.Drawing into the global namespace, which conflicts with WPF types.

global using Application    = System.Windows.Application;
global using Brush          = System.Windows.Media.Brush;
global using Color          = System.Windows.Media.Color;
global using Cursor         = System.Windows.Input.Cursor;
global using MessageBox     = System.Windows.MessageBox;
global using Clipboard      = System.Windows.Clipboard;
global using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
global using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
