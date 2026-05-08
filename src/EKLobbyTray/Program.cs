// src/EKLobbyTray/Program.cs
using System.Windows.Forms;
using EKLobbyTray;

Application.SetHighDpiMode(HighDpiMode.SystemAware);
Application.EnableVisualStyles();
Application.SetCompatibleTextRenderingDefault(false);

using var tray = new TrayApp();
Application.Run();
